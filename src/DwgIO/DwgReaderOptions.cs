namespace DwgIO;

/// <summary>Options controlling what <see cref="DwgDocument.Open"/> reads.</summary>
public sealed class DwgReaderOptions
{
    /// <summary>Options with every default value.</summary>
    public static DwgReaderOptions Default { get; } = new();

    /// <summary>Parse the page map and section map (default: true).</summary>
    public bool ReadSectionMap { get; set; } = true;

    /// <summary>
    /// Verify data-page checksums while reading sections and throw
    /// <see cref="DwgException"/> on a mismatch (default: false).
    /// </summary>
    public bool VerifyChecksums { get; set; }

    /// <summary>Parse the <c>AcDb:SummaryInfo</c> section when present (default: true).</summary>
    public bool ReadSummaryInfo { get; set; } = true;

    /// <summary>Parse metadata sections: AppInfo, FileDepList, Preview (default: true).</summary>
    public bool ReadMetadata { get; set; } = true;

    /// <summary>
    /// Decompress <c>AcDb:Handles</c> + <c>AcDb:AcDbObjects</c> and build the
    /// handle→offset index and object inventory (default: true).
    /// </summary>
    public bool ReadObjects { get; set; } = true;

    /// <summary>Decode entity bodies (common preamble + geometry for LINE/CIRCLE/ARC/POINT)
    /// into <see cref="DwgDocument.Entities"/> (default: true; requires ReadObjects).</summary>
    public bool ReadEntities { get; set; } = true;
}
