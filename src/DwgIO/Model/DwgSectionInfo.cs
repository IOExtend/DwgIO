namespace DwgIO.Model;

/// <summary>
/// One named section of the R2004-family container,
/// surfaced for inspection without reading its payload.
/// </summary>
public sealed class DwgSectionInfo
{
    /// <summary>Section name as stored, e.g. "AcDb:Header", "AcDb:AcDbObjects", "AcDb:SummaryInfo".</summary>
    public required string Name { get; init; }

    /// <summary>Total decompressed size across all pages.</summary>
    public required long Size { get; init; }

    /// <summary>Number of data pages the section is split into.</summary>
    public required int PageCount { get; init; }

    /// <summary>Max decompressed size of a single page of this section (usually 0x7400).</summary>
    public required long MaxPageSize { get; init; }

    /// <summary>Compression flag: 1 = uncompressed, 2 = DWG LZ77.</summary>
    public required int Compression { get; init; }

    /// <summary>Encryption flag: 0 = none, 1 = encrypted, 2 = only properties encrypted (payload readable).</summary>
    public required int Encryption { get; init; }

    /// <summary>Section number used by its data pages.</summary>
    public required uint SectionId { get; init; }
}
