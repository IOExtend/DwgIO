using System.Buffers.Binary;
using DwgIO.Model;

namespace DwgIO.Internal.Parsing;

/// <summary>
/// Parses the decompressed <c>AcDb:Preview</c> payload and extracts
/// the thumbnail image (BMP code 2, WMF code 3, PNG code 6 — AutoCAD 2013+).
/// Falls back to carving image magic bytes out of the payload.
/// </summary>
internal static class PreviewParser
{
    private static readonly byte[] StartSentinel =
        [0x1F, 0x25, 0x6D, 0x07, 0xD4, 0x36, 0x28, 0x28, 0x9D, 0x57, 0xCA, 0x3F, 0x9D, 0x44, 0x10, 0x2B];

    private static readonly byte[] EndSentinel =
        [0xE0, 0xDA, 0x92, 0xF8, 0x2B, 0xC9, 0xD7, 0xD7, 0x62, 0xA8, 0x35, 0xC0, 0x62, 0xBB, 0xEF, 0xD4];

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Parses a Preview payload and returns its thumbnail image.</summary>
    public static DwgPreview Parse(byte[] data)
    {
        if (data.Length < 20 || !data.AsSpan(0, 16).SequenceEqual(StartSentinel))
        {
            throw new DwgException("AcDb:Preview payload is missing its start sentinel.");
        }

        var cursor = new MetadataByteCursor(data, 1252);
        cursor.Skip(16);
        uint overallSize = cursor.ReadU32();
        byte imagesPresent = cursor.ReadU8();

        (int start, int size)? headerData = null;
        (int start, int size)? bmp = null;
        (int start, int size)? wmf = null;
        (int start, int size)? png = null;
        for (int i = 0; i < imagesPresent; i++)
        {
            byte code = cursor.ReadU8();
            uint start = cursor.ReadU32();
            uint size = cursor.ReadU32();
            var range = (start: (int)start, size: (int)size);
            switch (code)
            {
                case 1: headerData = range; break;
                case 2: bmp = range; break;
                case 3: wmf = range; break;
                case 6: png = range; break;
            }
        }

        // PNG previews masquerade as the bitmap slot on modern files.
        var image = Extract(data, bmp) ?? Extract(data, png);
        var kind = bmp is not null || image is not null ? DwgPreviewImageKind.Bmp : DwgPreviewImageKind.None;
        if (IsPng(image))
        {
            kind = DwgPreviewImageKind.Png;
        }

        var wmfBytes = Extract(data, wmf);
        if (image is null && wmfBytes is not null)
        {
            image = wmfBytes;
            kind = DwgPreviewImageKind.Wmf;
        }

        if (image is null)
        {
            image = CarveImageMagic(data);
            kind = image is null ? DwgPreviewImageKind.None
                : IsPng(image) ? DwgPreviewImageKind.Png
                : DwgPreviewImageKind.Bmp;
        }

        return new DwgPreview
        {
            OverallSize = overallSize,
            ImageKind = kind,
            ImageBytes = image ?? [],
        };
    }

    private static bool IsPng(byte[]? bytes) =>
        bytes is not null && bytes.Length >= PngMagic.Length && bytes.AsSpan(0, PngMagic.Length).SequenceEqual(PngMagic);

    private static byte[]? Extract(byte[] data, (int start, int size)? range)
    {
        if (range is not { } r || r.start <= 0 || r.size <= 0 || r.size > data.Length - r.start)
        {
            return null;
        }

        var bytes = new byte[r.size];
        Buffer.BlockCopy(data, r.start, bytes, 0, r.size);
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
