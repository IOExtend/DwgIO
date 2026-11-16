using System.Buffers.Binary;
using DwgIO.Internal.Container;
using DwgIO.Model;

namespace DwgIO.Internal.Parsing;

/// <summary>
/// Parses the decompressed <c>AcDb:Preview</c> payload and extracts
/// the thumbnail image (BMP code 2, WMF code 3, PNG code 6 — AutoCAD 2013+).
/// The directory stores absolute file offsets; they are mapped through the
/// section page layout when one is supplied. Falls back to carving image magic
/// bytes out of the payload.
/// </summary>
internal static class PreviewParser
{
    private static readonly byte[] StartSentinel =
        [0x1F, 0x25, 0x6D, 0x07, 0xD4, 0x36, 0x28, 0x28, 0x9D, 0x57, 0xCA, 0x3F, 0x9D, 0x44, 0x10, 0x2B];

    private static readonly byte[] EndSentinel =
        [0xE0, 0xDA, 0x92, 0xF8, 0x2B, 0xC9, 0xD7, 0xD7, 0x62, 0xA8, 0x35, 0xC0, 0x62, 0xBB, 0xEF, 0xD4];

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly byte[] WmfPlaceableMagic = [0xD7, 0xCD, 0xC6, 0x9A];

    private static readonly uint[] DibHeaderSizes = [12, 16, 40, 52, 56, 64, 108, 124];

    /// <summary>Parses a Preview payload and returns its thumbnail image.</summary>
    public static DwgPreview Parse(byte[] data) => Parse(data, null);

    /// <summary>Parses a Preview payload, mapping absolute directory offsets through
    /// the section's page layout when it is supplied.</summary>
    public static DwgPreview Parse(byte[] data, IReadOnlyList<SectionPageLayout>? pages)
    {
        if (data.Length < 20 || !data.AsSpan(0, 16).SequenceEqual(StartSentinel))
        {
            throw new DwgException("AcDb:Preview payload is missing its start sentinel.");
        }

        var cursor = new MetadataByteCursor(data, 1252);
        cursor.Skip(16);
        uint overallSize = cursor.ReadU32();
        byte imagesPresent = cursor.ReadU8();

        (uint start, uint size)? bmp = null;
        (uint start, uint size)? wmf = null;
        (uint start, uint size)? png = null;
        for (int i = 0; i < imagesPresent; i++)
        {
            byte code = cursor.ReadU8();
            uint start = cursor.ReadU32();
            uint size = cursor.ReadU32();
            switch (code)
            {
                case 2: bmp = (start, size); break;
                case 3: wmf = (start, size); break;
                case 6: png = (start, size); break;
            }
        }

        // Prefer the slot each format belongs to, but keep looking when a slot
        // carries bytes that are not a valid image of that format. Keep the kind
        // reported by FindImage: re-detecting would drop WMF, which DetectKind
        // historically did not know.
        byte[]? image = null;
        var kind = DwgPreviewImageKind.None;
        foreach (var slot in new[] { bmp, png, wmf })
        {
            var bytes = FindImage(data, slot, pages, out var found);
            if (bytes is not null)
            {
                image = bytes;
                kind = found;
                break;
            }
        }

        if (image is null)
        {
            image = CarveImageMagic(data);
            kind = image is null ? DwgPreviewImageKind.None : DetectKind(image);
        }

        if (kind == DwgPreviewImageKind.None)
        {
            image = null;
        }

        return new DwgPreview
        {
            OverallSize = overallSize,
            ImageKind = kind,
            ImageBytes = image ?? [],
        };
    }

    private static byte[]? FindImage(
        byte[] data,
        (uint start, uint size)? range,
        IReadOnlyList<SectionPageLayout>? pages,
        out DwgPreviewImageKind kind)
    {
        kind = DwgPreviewImageKind.None;
        var bytes = Extract(data, range, pages);
        if (bytes is null)
        {
            return null;
        }

        if (IsPng(bytes))
        {
            kind = DwgPreviewImageKind.Png;
            return bytes;
        }

        if (TryWrapBmp(bytes, out var bmpBytes))
        {
            kind = DwgPreviewImageKind.Bmp;
            return bmpBytes;
        }

        if (IsWmf(bytes))
        {
            kind = DwgPreviewImageKind.Wmf;
            return bytes;
        }

        return null;
    }

    private static DwgPreviewImageKind DetectKind(byte[] bytes) =>
        IsPng(bytes) ? DwgPreviewImageKind.Png
        : bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M' ? DwgPreviewImageKind.Bmp
        : IsWmf(bytes) ? DwgPreviewImageKind.Wmf
        : DwgPreviewImageKind.None;

    private static bool IsPng(byte[] bytes) =>
        bytes.Length >= PngMagic.Length && bytes.AsSpan(0, PngMagic.Length).SequenceEqual(PngMagic);

    private static bool IsWmf(byte[] bytes) =>
        bytes.Length >= WmfPlaceableMagic.Length && bytes.AsSpan(0, WmfPlaceableMagic.Length).SequenceEqual(WmfPlaceableMagic)
        || bytes.Length >= 18
            && BinaryPrimitives.ReadUInt16LittleEndian(bytes) is 1 or 2
            && BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)) == 9;

    /// <summary>Adds the 14-byte BMP file header when the bytes are a bare DIB.
    /// A bitmap directory entry stores the bitmap info header and pixels without "BM".</summary>
    private static bool TryWrapBmp(byte[] bytes, out byte[] bmpBytes)
    {
        bmpBytes = bytes;
        if (bytes.Length >= 14 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
        {
            return true;
        }

        if (!TryReadDibHeader(bytes, out int offBits))
        {
            return false;
        }

        var wrapped = new byte[14 + bytes.Length];
        wrapped[0] = (byte)'B';
        wrapped[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(wrapped.AsSpan(2), (uint)wrapped.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(wrapped.AsSpan(10), (uint)offBits);
        Buffer.BlockCopy(bytes, 0, wrapped, 14, bytes.Length);
        bmpBytes = wrapped;
        return true;
    }

    private static bool TryReadDibHeader(byte[] bytes, out int offBits)
    {
        offBits = 0;
        if (bytes.Length < 12)
        {
            return false;
        }

        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (!DibHeaderSizes.Contains(headerSize) || headerSize > bytes.Length)
        {
            return false;
        }

        if (headerSize == 12)
        {
            ushort bitCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10));
            int paletteEntries = bitCount <= 8 ? 1 << bitCount : 0;
            long offset = 14 + 12 + paletteEntries * 3L;
            if (offset > 14 + bytes.Length)
            {
                return false;
            }

            offBits = (int)offset;
            return true;
        }

        ushort bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14));
        uint coloursUsed = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(32));
        long entries = coloursUsed != 0 ? coloursUsed : bitsPerPixel <= 8 ? 1u << bitsPerPixel : 0;
        // With BI_BITFIELDS (3) three DWORD colour masks, and with
        // BI_ALPHABITFIELDS (6) four DWORD masks (red, green, blue, alpha),
        // follow the 40-byte BITMAPINFOHEADER before the palette.
        uint compression = headerSize >= 20 && bytes.Length >= 20
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16))
            : 0;
        int maskCount = headerSize == 40 ? compression switch
        {
            3 => 3,
            6 => 4,
            _ => 0,
        }
        : 0;
        long headerOffset = 14 + headerSize + maskCount * 4L + entries * 4L;
        if (headerOffset > 14 + bytes.Length)
        {
            return false;
        }

        offBits = (int)headerOffset;
        return true;
    }

    private static byte[]? Extract(
        byte[] data,
        (uint start, uint size)? range,
        IReadOnlyList<SectionPageLayout>? pages)
    {
        if (range is not { } r || r.size == 0 || r.size > int.MaxValue)
        {
            return null;
        }

        if (pages is not null)
        {
            // Absolute offsets map through the page holding the image start. The image
            // may span pages: its bytes are contiguous in the assembled section, with
            // a file-header gap between page payloads, so verify that the whole
            // section range is covered by consecutive uncompressed pages.
            long? sectionStart = null;
            foreach (var page in pages)
            {
                if (!page.IsStoredUncompressed)
                {
                    continue;
                }

                if ((long)r.start < page.FileOffset)
                {
                    continue;
                }

                long relative = (long)r.start - page.FileOffset;
                if (relative >= page.Length)
                {
                    continue;
                }

                sectionStart = (long)page.SectionOffset + relative;
                break;
            }

            if (sectionStart.HasValue)
            {
                long sectionEnd = sectionStart.Value + r.size;
                long covered = sectionStart.Value;
                foreach (var page in pages
                    .Where(p => p.IsStoredUncompressed)
                    .OrderBy(p => p.SectionOffset))
                {
                    long intervalStart = page.SectionOffset;
                    long intervalEnd = intervalStart + page.Length;
                    if (intervalEnd <= covered)
                    {
                        continue;
                    }

                    if (intervalStart > covered)
                    {
                        break; // gap in the assembled section
                    }

                    covered = intervalEnd;
                    if (covered >= sectionEnd)
                    {
                        break;
                    }
                }

                if (covered >= sectionEnd)
                {
                    var spanning = Take(data, sectionStart.Value, (int)r.size);
                    if (spanning is not null)
                    {
                        return spanning;
                    }
                }
            }
        }

        // Older writers stored section-relative offsets; accept them when the absolute
        // interpretation does not fit any page.
        return Take(data, r.start, (int)r.size);
    }

    private static byte[]? Take(byte[] data, long start, int size)
    {
        if (start < 0 || size <= 0 || start > data.Length - size)
        {
            return null;
        }

        var bytes = new byte[size];
        Buffer.BlockCopy(data, (int)start, bytes, 0, size);
        return bytes;
    }

    private static byte[]? CarveImageMagic(byte[] data)
    {
        int start = -1;
        for (int i = 16; i + PngMagic.Length <= data.Length; i++)
        {
            if (data.AsSpan(i, PngMagic.Length).SequenceEqual(PngMagic))
            {
                start = i;
                break;
            }

            if (i + 2 <= data.Length && data[i] == (byte)'B' && data[i + 1] == (byte)'M' && data.Length - i > 54)
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        int end = data.Length;
        for (int i = data.Length - EndSentinel.Length; i >= start; i--)
        {
            if (data.AsSpan(i, EndSentinel.Length).SequenceEqual(EndSentinel))
            {
                end = i;
                break;
            }
        }

        if (end <= start)
        {
            return null;
        }

        var bytes = new byte[end - start];
        Buffer.BlockCopy(data, start, bytes, 0, bytes.Length);
        return bytes;
    }
}
