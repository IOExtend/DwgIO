using DwgIO.Model;

namespace DwgIO.Internal.Container;

/// <summary>
/// The parsed R2004-family container: decrypted file header,
/// page map and section map. Assembles named sections from their pages.
/// </summary>
internal sealed class R2004Container
{
    /// <summary>Decrypted file header.</summary>
    public required DwgContainerHeader Header { get; init; }

    /// <summary>Global page map: page number → file offset.</summary>
    public required PageMap PageMap { get; init; }

    /// <summary>Named sections and their page lists.</summary>
    public required SectionMap SectionMap { get; init; }

    /// <summary>Parses the header, page map and section map of an R2004-family file.</summary>
    public static R2004Container Parse(byte[] fileBytes)
    {
        var header = DwgContainerHeader.Parse(fileBytes);
        var pageMap = PageMap.Parse(fileBytes, header);
        var sectionMap = SectionMap.Parse(fileBytes, header, pageMap);
        return new R2004Container
        {
            Header = header,
            PageMap = pageMap,
            SectionMap = sectionMap,
        };
    }

    /// <summary>
    /// Assembles one named section's decompressed bytes from its pages.
    /// Pages starting past the section end are zero pages and skipped;
    /// gaps between page start offsets are filled with zeroes.
    /// When <paramref name="verifyChecksums"/> is set, a page checksum mismatch throws.
    /// </summary>
    public byte[] ReadSection(byte[] fileBytes, SectionEntry entry, bool verifyChecksums = false)
    {
        if (entry.Encryption == 1)
        {
            throw new DwgException($"Section '{entry.Name}' is encrypted; encrypted payloads are not supported.");
        }

        long totalSize = (long)entry.Size;
        var result = new byte[totalSize];

        foreach (var pageRef in entry.Pages)
        {
            var page = PageMap.Find((int)pageRef.PageNumber)
                ?? throw new DwgException($"Section '{entry.Name}' references page {pageRef.PageNumber} missing from the page map.");

            var pageHeader = DataPageHeader.Parse(fileBytes, page.FileOffset);
            if (pageHeader.PageType != DataPageHeader.ExpectedPageType)
            {
                throw new DwgException(
                    $"Section '{entry.Name}' page {pageRef.PageNumber} has wrong page type 0x{pageHeader.PageType:X} (expected 0x{DataPageHeader.ExpectedPageType:X}).");
            }

            if (verifyChecksums && (!pageHeader.DataChecksumValid || !pageHeader.HeaderChecksumValid))
            {
                throw new DwgException(
                    $"Section '{entry.Name}' page {pageRef.PageNumber} failed page checksum verification " +
                    $"(data={pageHeader.DataChecksumValid}, header={pageHeader.HeaderChecksumValid}).");
            }

            long payloadStart = (long)page.FileOffset + 0x20;
            long payloadEnd = payloadStart + pageHeader.DataSize;
            if (payloadEnd > fileBytes.Length)
            {
                throw new DwgException($"Section '{entry.Name}' page {pageRef.PageNumber} payload is truncated.");
            }

            var payload = new byte[pageHeader.DataSize];
            Buffer.BlockCopy(fileBytes, (int)payloadStart, payload, 0, (int)pageHeader.DataSize);

            byte[] decompressed = entry.Compression switch
            {
                1 => payload,
                2 => Lz77Decoder.Decompress(payload, (int)entry.MaxPageSize),
                _ => throw new DwgException($"Section '{entry.Name}' has unknown compression flag {entry.Compression}."),
            };

            long start = (long)pageRef.StartOffset;
            if (start >= totalSize)
            {
                continue; // zero page beyond the section end
            }

            int take = (int)Math.Min(decompressed.Length, totalSize - start);
            Buffer.BlockCopy(decompressed, 0, result, (int)start, take);
        }

        return result;
    }
}
