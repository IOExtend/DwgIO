namespace DwgIO.Model;

/// <summary>
/// Producer information from the <c>AcDb:AppInfo</c> section.
/// </summary>
public sealed class DwgAppInfo
{
    /// <summary>Name of the AppInfo record; "AppInfoDataList" in the files observed.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Application version string, e.g. "Z.75.0.0" for AutoCAD 2018.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Free-form comment string (AC1021+ files; usually a producer notice).</summary>
    public string Comment { get; init; } = string.Empty;

    /// <summary>The product named by the product XML's <c>name</c> attribute (e.g. "AutoCAD", "AutoCAD LT",
    /// "Teigha(R)"); empty when the XML is absent or carries no name.</summary>
    public string Product { get; init; } = string.Empty;

    /// <summary>XML product blob (ProductInformation element), present in both layouts.</summary>
    public string ProductXml { get; init; } = string.Empty;
}
