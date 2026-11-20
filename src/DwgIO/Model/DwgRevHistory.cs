namespace DwgIO.Model;

/// <summary>
/// The revision history of the <c>AcDb:RevHistory</c> section: a version pair and the list of save
/// entries in file order.
/// </summary>
public sealed class DwgRevHistory
{
    /// <summary>Major version of the section layout.</summary>
    public uint ClassVersion { get; init; }

    /// <summary>Minor version of the section layout.</summary>
    public uint ClassMinor { get; init; }

    /// <summary>Stored revision values in file order.</summary>
    public IReadOnlyList<uint> Histories { get; init; } = [];
}
