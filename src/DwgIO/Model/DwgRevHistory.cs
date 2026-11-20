namespace DwgIO.Model;

/// <summary>
/// The revision history of the <c>AcDb:RevHistory</c> section: a version pair and the list of save
/// references. The section names one entry per stored AppInfoHistory record, in file order.
/// </summary>
public sealed class DwgRevHistory
{
    /// <summary>Major version of the section layout.</summary>
    public uint ClassVersion { get; init; }

    /// <summary>Minor version of the section layout.</summary>
    public uint ClassMinor { get; init; }

    /// <summary>One value per recorded save, in file order; the values reference the AppInfoHistory entries.</summary>
    public IReadOnlyList<uint> Histories { get; init; } = [];
}
