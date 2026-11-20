namespace DwgIO.Model;

/// <summary>
/// The template description of the <c>AcDb:Template</c> section: the drawing template the file was
/// created from and its unit system.
/// </summary>
public sealed class DwgTemplate
{
    /// <summary>Description of the template (for example the original file name), or empty.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Unit system: 0 = English (imperial), 1 = metric.</summary>
    public int Measurement { get; init; }

    /// <summary>True when <see cref="Measurement"/> is 1 (metric).</summary>
    public bool IsMetric => Measurement == 1;
}
