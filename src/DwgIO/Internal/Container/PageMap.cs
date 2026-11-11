using System.Buffers.Binary;
using DwgIO.Model;

namespace DwgIO.Internal.Container;

/// <summary>
/// One row of the global page map. Positive <see cref="Number"/>
/// identifies a real page; a negative one marks a gap in the sequence.
/// </summary>
internal sealed class PageMapEntry
{
    /// <summary>Page number; negative for a gap row.</summary>
    public required int Number { get; init; }

    /// <summary>On-disk size of this page in bytes (includes its 32-byte header).</summary>
    public required uint Size { get; init; }

    /// <summary>Computed absolute file offset where this page begins.</summary>
    public required ulong FileOffset { get; init; }

    /// <summary>Whether this row marks a gap rather than a real page.</summary>
    public required bool IsGap { get; init; }
}

/// <summary>The decompressed global page map.</summary>
internal sealed class PageMap
{
    private Dictionary<int, PageMapEntry>? _byNumber;

    /// <summary>Map rows in file order, gaps included.</summary>
    public required IReadOnlyList<PageMapEntry> Entries { get; init; }

    /// <summary>Looks up a real (non-gap) page by number.</summary>
    public PageMapEntry? Find(int number)
    {
        _byNumber ??= BuildIndex();
        return _byNumber.TryGetValue(number, out var entry) ? entry : null;
    }

    private Dictionary<int, PageMapEntry> BuildIndex()
    {
        var index = new Dictionary<int, PageMapEntry>();
        foreach (var entry in Entries)
        {
            if (!entry.IsGap)
            {
                index.TryAdd(entry.Number, entry);
            }
        }

        return index;
    }

    /// <summary>Number of rows in the map, gaps included.</summary>
    public int Count => Entries.Count;

    /// <summary>
    /// Decompresses and parses the global page map:
    /// a map page at <c>header.PageMapOffset + 0x100</c> whose 20-byte
    /// plaintext header (type 0x41630E3B) is followed by an LZ77 payload of
    /// <c>(int32 number, int32 size)</c> pairs; gap entries carry 4 extra u32s.
    /// </summary>
    public static PageMap Parse(byte[] fileBytes, DwgContainerHeader header)
    {
        long pageOffset = (long)header.PageMapOffset + 0x100;
        var mapHeader = MapPageHeader.Parse(fileBytes, pageOffset);
        if (mapHeader.PageType != MapPageHeader.PageMapType)
        {
            throw new DwgException($"Page map type tag 0x{mapHeader.PageType:X} != expected 0x{MapPageHeader.PageMapType:X}.");
        }

        if (mapHeader.CompressionType != 2)
        {
            throw new DwgException($"Page map compression type {mapHeader.CompressionType} != 2 (LZ77 expected).");
        }

        var decompressed = ReadMapPagePayload(fileBytes, pageOffset, mapHeader);

        var entries = new List<PageMapEntry>();
        int i = 0;
        ulong fileOffset = 0x100; // first page sits after the global file header
        while (i + 8 <= decompressed.Length)
        {
            int number = BinaryPrimitives.ReadInt32LittleEndian(decompressed.AsSpan(i));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(decompressed.AsSpan(i + 4));
            i += 8;
            bool isGap = number < 0;
            if (isGap)
            {
                // Gap rows carry 4 extra u32 (parent, left, right, 0).
                if (i + 16 > decompressed.Length)
                {
                    throw new DwgException("Page map gap record truncated.");
                }

                i += 16;
            }

            entries.Add(new PageMapEntry
            {
                Number = number,
                Size = size,
                FileOffset = fileOffset,
                IsGap = isGap,
            });
            fileOffset += size;
        }

        return new PageMap { Entries = entries };
    }

    internal static byte[] ReadMapPagePayload(byte[] fileBytes, long pageOffset, MapPageHeader mapHeader)
    {
        long payloadStart = pageOffset + 0x14;
        long payloadEnd = payloadStart + mapHeader.CompressedSize;
        if (payloadEnd > fileBytes.Length)
        {
            throw new DwgException($"Map page at {pageOffset} truncated: payload needs {mapHeader.CompressedSize} bytes.");
        }

        var payload = new byte[mapHeader.CompressedSize];
        Buffer.BlockCopy(fileBytes, (int)payloadStart, payload, 0, (int)mapHeader.CompressedSize);
        return Lz77Decoder.Decompress(payload, (int)mapHeader.DecompressedSize);
    }
}

/// <summary>
/// The 20-byte plaintext header that precedes the page map and the section map;
/// no page-header mask is applied here.
/// </summary>
internal sealed class MapPageHeader
{
    /// <summary>Page type tag of the page map.</summary>
    public const uint PageMapType = 0x4163_0E3B;

    /// <summary>Page type tag of the section map.</summary>
    public const uint SectionMapType = 0x4163_003B;

    /// <summary>0x00: page type tag.</summary>
    public required uint PageType { get; init; }

    /// <summary>0x04: payload size after decompression.</summary>
    public required uint DecompressedSize { get; init; }

    /// <summary>0x08: stored payload size.</summary>
    public required uint CompressedSize { get; init; }

    /// <summary>0x0C: compression type (2 = DWG LZ77).</summary>
    public required uint CompressionType { get; init; }

    /// <summary>0x10: stored checksum (not verified).</summary>
    public required uint Checksum { get; init; }

    /// <summary>Reads the map page header at <paramref name="offset"/>.</summary>
    public static MapPageHeader Parse(byte[] fileBytes, long offset)
    {
        if (offset + 0x14 > fileBytes.Length)
        {
            throw new DwgException($"Map page header at {offset} is truncated.");
        }

        var hdr = fileBytes.AsSpan((int)offset, 0x14);
        return new MapPageHeader
        {
            PageType = BinaryPrimitives.ReadUInt32LittleEndian(hdr[..4]),
            DecompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(hdr.Slice(4, 4)),
            CompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(hdr.Slice(8, 4)),
            CompressionType = BinaryPrimitives.ReadUInt32LittleEndian(hdr.Slice(12, 4)),
            Checksum = BinaryPrimitives.ReadUInt32LittleEndian(hdr.Slice(16, 4)),
        };
    }
}
