namespace DwgIO.Model;

/// <summary>
/// Drawing-level metadata from the <c>AcDb:SummaryInfo</c> section
/// (the SUMMARYINFO dialog in AutoCAD).
/// </summary>
public sealed class DwgSummaryInfo
{
    /// <summary>Drawing title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Drawing subject.</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>Author name.</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>Keywords.</summary>
    public string Keywords { get; init; } = string.Empty;

    /// <summary>Comments.</summary>
    public string Comments { get; init; } = string.Empty;

    /// <summary>Name of the user who last saved the drawing.</summary>
    public string LastSavedBy { get; init; } = string.Empty;

    /// <summary>Revision number.</summary>
    public string RevisionNumber { get; init; } = string.Empty;

    /// <summary>Base address for relative hyperlinks.</summary>
    public string HyperlinkBase { get; init; } = string.Empty;

    /// <summary>Create date-time converted from the stored Julian day + milliseconds.</summary>
    public DateTime? CreatedUtc { get; init; }

    /// <summary>Modified date-time converted from the stored Julian day + milliseconds.</summary>
    public DateTime? ModifiedUtc { get; init; }

    /// <summary>Custom key/value properties from the Drawing Properties dialog.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Properties { get; init; } = [];
}
