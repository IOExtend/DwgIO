using DwgIO.Model;

namespace DwgIO.Internal.Container;

/// <summary>
/// One decompressed page of a section: where it starts in the section and where its
/// payload starts in the file. Absolute file offsets stored inside a section only map
/// linearly back to section positions when the page is stored uncompressed.
/// </summary>
internal readonly record struct SectionPageLayout(
    int SectionOffset,
    int Length,
    long FileOffset,
    bool IsStoredUncompressed);

/// <summary>
/// The parsed R2004-family container: decrypted file header,
/// page map and section map. Assembles named sections from their pages.
/// </summary>
internal sealed class R2004Container
{
    /// <summary>
    /// Some writers declare a section a few bytes larger than its pages can hold.
    /// Accept that much overshoot, zero-fill the uncovered tail and report it, while
    /// still rejecting a declared size far beyond the page budget before allocating.
    /// </summary>
    private const ulong MaxSizeOvershoot = 0x100;

    private long _decompressedBytes;

    /// <summary>Ceiling on section buffers allocated plus page bytes produced by all section reads of one document.</summary>
    public long MaxDecompressedBytes { get; init; } = 8L * 1024 * 1024 * 1024;

    private void Charge(long bytes)
    {
        if (Interlocked.Add(ref _decompressedBytes, bytes) > MaxDecompressedBytes)
        {
            throw new DwgException($"Sections of this file expand to more than {MaxDecompressedBytes} bytes in total.");
        }
    }

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
    /// A declared size up to <see cref="MaxSizeOvershoot"/> bytes beyond what the pages
    /// can hold is zero-filled and reported through <paramref name="reportDiagnostic"/>.
    /// When <paramref name="verifyChecksums"/> is set, a page checksum mismatch throws.
    /// When <paramref name="pageLayout"/> is passed, it receives each page's section and file placement.
    /// </summary>
    public byte[] ReadSection(
        byte[] fileBytes,
        SectionEntry entry,
        bool verifyChecksums = false,
        Action<string>? reportDiagnostic = null,
        List<SectionPageLayout>? pageLayout = null)
    {
        if (entry.Encryption == 1)
        {
            throw new DwgException($"Section '{entry.Name}' is encrypted; encrypted payloads are not supported.");
        }

        // A page never decompresses to more than the section's page size.
        var pageLimits = new Lz77Decoder.Limits
        {
            MaxOutputBytes = (int)Math.Min(entry.MaxPageSize, (uint)Lz77Decoder.Limits.Default.MaxOutputBytes),
        };

        // The size comes from the file: check it before allocating the result.
        ulong pageBudget = (ulong)entry.PageCount * (ulong)pageLimits.MaxOutputBytes;
        if (entry.Size > (ulong)Array.MaxLength || entry.Size > pageBudget + MaxSizeOvershoot)
        {
            throw new DwgException(
                $"Section '{entry.Name}' declares {entry.Size} bytes, more than {entry.PageCount} pages of up to {pageLimits.MaxOutputBytes} bytes " +
                "or a single buffer can hold.");
        }

        if (entry.Size > pageBudget)
        {
            reportDiagnostic?.Invoke(
                $"{entry.Name}: declared size {entry.Size} exceeds the {pageBudget} bytes covered by {entry.PageCount} pages " +
                $"by {entry.Size - pageBudget}; the uncovered tail is zero-filled.");
        }

        int totalSize = (int)entry.Size;
        Charge(totalSize); // the result buffer counts against the document budget before it exists
        var result = new byte[totalSize];

        foreach (var pageRef in entry.Pages)
        {
            var page = PageMap.Find((int)pageRef.PageNumber)
                ?? throw new DwgException($"Section '{entry.Name}' references page {pageRef.PageNumber} missing from the page map.");

            var pageHeader = DataPageHeader.Parse(fileBytes, page.FileOffset, verifyChecksums);
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

            if (pageRef.StartOffset >= entry.Size)
            {
                continue; // zero page beyond the section end
            }

            var payload = new byte[pageHeader.DataSize];
            Buffer.BlockCopy(fileBytes, (int)payloadStart, payload, 0, (int)pageHeader.DataSize);

            byte[] decompressed = entry.Compression switch
            {
                1 => payload,
                2 => Lz77Decoder.Decompress(payload, pageLimits.MaxOutputBytes, pageLimits),
                _ => throw new DwgException($"Section '{entry.Name}' has unknown compression flag {entry.Compression}."),
            };

            // Page references may repeat, so the total work per document is capped as well.
            Charge(decompressed.Length);

            int start = (int)pageRef.StartOffset;
            int take = Math.Min(decompressed.Length, totalSize - start);
            Buffer.BlockCopy(decompressed, 0, result, start, take);
            pageLayout?.Add(new SectionPageLayout(start, take, payloadStart, entry.Compression == 1));
        }

        return result;
    }
}
