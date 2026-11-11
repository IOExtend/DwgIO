using System.Buffers.Binary;
using System.Text;
using DwgIO.Internal.Cipher;
using DwgIO.Model;

namespace DwgIO.Internal.Container;

/// <summary>
/// The 32-byte masked header that precedes every data-section page:
/// each of its 8 u32 words is XOR'd with <c>0x4164536B</c> XOR the page's file offset.
/// </summary>
internal sealed class DataPageHeader
{
    /// <summary>The page-type constant every data page header must carry after unmasking.</summary>
    public const uint ExpectedPageType = 0x4163_043B;

    /// <summary>0x00: the unmasked page type, expected to equal <see cref="ExpectedPageType"/>.</summary>
    public required uint PageType { get; init; }

    /// <summary>0x04: number of the section this page belongs to.</summary>
    public required uint SectionNumber { get; init; }

    /// <summary>Stored bytes after the header (compressed for an LZ77 section).</summary>
    public required uint DataSize { get; init; }

    /// <summary>The page's size in the page map, header and padding included.</summary>
    public required uint PageSize { get; init; }

    /// <summary>The page's offset within its section (8 bytes on disk).</summary>
    public required ulong StartOffset { get; init; }

    /// <summary>0x18: stored checksum over this header.</summary>
    public required uint HeaderChecksum { get; init; }

    /// <summary>0x1C: stored checksum over the page payload.</summary>
    public required uint DataChecksum { get; init; }

    /// <summary>Whether the page checksum over the stored payload matches <see cref="DataChecksum"/>.</summary>
    public bool DataChecksumValid { get; private set; }

    /// <summary>Whether the page checksum over the zeroed header matches <see cref="HeaderChecksum"/>.</summary>
    public bool HeaderChecksumValid { get; private set; }

    /// <summary>
    /// Unmasks and parses the data page header at <paramref name="fileOffset"/>; computes both
    /// checksum flags only when <paramref name="computeChecksums"/> is set (they stay false otherwise).
    /// </summary>
    public static DataPageHeader Parse(byte[] fileBytes, ulong fileOffset, bool computeChecksums)
    {
        if (fileOffset + 0x20 > (ulong)fileBytes.Length)
        {
            throw new DwgException($"Data page header at {fileOffset} is truncated.");
        }

        var hdr = new byte[0x20];
        Buffer.BlockCopy(fileBytes, (int)fileOffset, hdr, 0, 0x20);
        DwgCipher.UnmaskPageHeader(hdr, (uint)fileOffset);

        var header = new DataPageHeader
        {
            PageType = BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(0)),
            SectionNumber = BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(4)),
            DataSize = BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(8)),
            PageSize = BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(12)),
            StartOffset = BinaryPrimitives.ReadUInt64LittleEndian(hdr.AsSpan(16)),
            HeaderChecksum = BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(24)),
            DataChecksum = BinaryPrimitives.ReadUInt32LittleEndian(hdr.AsSpan(28)),
        };

        if (!computeChecksums)
        {
            return header;
        }

        if (fileOffset + 0x20 + header.DataSize > (ulong)fileBytes.Length)
        {
            throw new DwgException($"Data page at {fileOffset} is truncated: its payload needs {header.DataSize} bytes.");
        }

        // The data checksum covers the stored payload (seed 0); the header checksum
        // covers these 0x20 bytes with its own field zeroed, seeded with the data checksum.
        header.DataChecksumValid = DwgCrc.PageChecksum(0, fileBytes.AsSpan((int)fileOffset + 0x20, (int)header.DataSize).ToArray()) == header.DataChecksum;
        hdr.AsSpan(24, 4).Clear();
        header.HeaderChecksumValid = DwgCrc.PageChecksum(header.DataChecksum, hdr) == header.HeaderChecksum;

        return header;
    }
}

/// <summary>One named entry of the section map.</summary>
internal sealed class SectionEntry
{
    /// <summary>Section name, e.g. <c>AcDb:Header</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Total decompressed size across all pages.</summary>
    public required ulong Size { get; init; }

    /// <summary>Number of page references that follow the entry.</summary>
    public required uint PageCount { get; init; }

    /// <summary>Max decompressed size of a single page of this section (usually 0x7400).</summary>
    public required uint MaxPageSize { get; init; }

    /// <summary>Compression flag: 1 = uncompressed, 2 = DWG LZ77.</summary>
    public required uint Compression { get; init; }

    /// <summary>Numeric id of the section.</summary>
    public required uint SectionId { get; init; }

    /// <summary>Encryption flag: 0 = none, 1 = encrypted, 2 = only properties encrypted.</summary>
    public required uint Encryption { get; init; }

    /// <summary>The section's pages in stored order.</summary>
    public required IReadOnlyList<PageRef> Pages { get; init; }
}

/// <summary>One page reference inside a section-map entry.</summary>
internal sealed class PageRef
{
    /// <summary>Page number to look up in the page map.</summary>
    public required uint PageNumber { get; init; }

    /// <summary>Stored size of the page payload.</summary>
    public required uint DataSize { get; init; }

    /// <summary>Offset of the page's decompressed data within the section.</summary>
    public required ulong StartOffset { get; init; }
}

/// <summary>The section map: every named section in the file.</summary>
internal sealed class SectionMap
{
    /// <summary>All section entries in stored order.</summary>
    public required IReadOnlyList<SectionEntry> Sections { get; init; }

    /// <summary>Finds a section by exact name, or null when absent.</summary>
    public SectionEntry? FindByName(string name) =>
        Sections.FirstOrDefault(s => s.Name == name);

    /// <summary>
    /// Locates the section map page in the page map (number == header.SectionMapPageNumber),
    /// decompresses its payload and parses the section entries.
    /// </summary>
    public static SectionMap Parse(byte[] fileBytes, DwgContainerHeader header, PageMap pageMap)
    {
        var mapPage = pageMap.Find((int)header.SectionMapPageNumber)
            ?? throw new DwgException($"Section map page {header.SectionMapPageNumber} not found in the page map.");

        var mapHeader = MapPageHeader.Parse(fileBytes, (long)mapPage.FileOffset);
        if (mapHeader.PageType != MapPageHeader.SectionMapType)
        {
            throw new DwgException($"Section map type 0x{mapHeader.PageType:X} != expected 0x{MapPageHeader.SectionMapType:X}.");
        }

        if (mapHeader.CompressionType != 2)
        {
            throw new DwgException($"Section map compression type {mapHeader.CompressionType} != 2 (LZ77 expected).");
        }

        var decompressed = PageMap.ReadMapPagePayload(fileBytes, (long)mapPage.FileOffset, mapHeader);
        return new SectionMap { Sections = DecodeSectionEntries(decompressed) };
    }

    internal static List<SectionEntry> DecodeSectionEntries(byte[] data)
    {
        if (data.Length < 0x14)
        {
            throw new DwgException($"Section map shorter than its 0x14-byte header: {data.Length} bytes.");
        }

        int entryCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0));
        int cursor = 0x14;

        // Counts come from the file: check them against the buffer before sizing lists.
        if (entryCount < 0 || entryCount > (data.Length - cursor) / 0x60)
        {
            throw new DwgException($"Section map declares {entryCount} entries, more than its {data.Length} bytes can hold.");
        }

        var result = new List<SectionEntry>(entryCount);
        for (int n = 0; n < entryCount; n++)
        {
            if (cursor + 0x60 > data.Length)
            {
                throw new DwgException($"Section map entry #{result.Count} overruns the buffer at 0x{cursor:X}.");
            }

            ulong size = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(cursor));
            uint pageCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(cursor + 8));
            uint maxPageSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(cursor + 12));
            uint compression = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(cursor + 20));
            uint sectionId = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(cursor + 24));
            uint encryption = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(cursor + 28));

            // Name is 64 UTF-8 bytes, NUL-padded.
            int nameEnd = Array.IndexOf(data, (byte)0, cursor + 32, 64);
            if (nameEnd < 0)
            {
                nameEnd = cursor + 96;
            }

            string name = Encoding.UTF8.GetString(data, cursor + 32, nameEnd - (cursor + 32));
            cursor += 0x60;

            if (pageCount > (uint)(data.Length - cursor) / 16)
            {
                throw new DwgException($"Section '{name}' declares {pageCount} pages, more than the section map can hold.");
            }

            var pages = new List<PageRef>((int)pageCount);
            for (uint p = 0; p < pageCount; p++)
            {
                if (cursor + 16 > data.Length)
                {
                    throw new DwgException($"Page ref {p} of section '{name}' overruns the buffer.");
                }

                pages.Add(new PageRef
                {
                    PageNumber = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(cursor)),
                    DataSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(cursor + 4)),
                    StartOffset = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(cursor + 8)),
                });
                cursor += 16;
            }

            result.Add(new SectionEntry
            {
                Name = name,
                Size = size,
                PageCount = pageCount,
                MaxPageSize = maxPageSize,
                Compression = compression,
                SectionId = sectionId,
                Encryption = encryption,
                Pages = pages,
            });
        }

        return result;
    }
}
