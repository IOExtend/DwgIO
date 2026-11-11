namespace DwgIO.Model;

/// <summary>One file dependency recorded in <c>AcDb:FileDepList</c>.</summary>
public sealed class DwgFileDependency
{
    /// <summary>File name as referenced by the drawing.</summary>
    public required string Filename { get; init; }

    /// <summary>Path where the file was found when the drawing was saved.</summary>
    public required string FoundPath { get; init; }

    /// <summary>Fingerprint GUID of the referenced file (as stored).</summary>
    public required string FingerprintGuid { get; init; }

    /// <summary>Version GUID of the referenced file (as stored).</summary>
    public required string VersionGuid { get; init; }

    /// <summary>Index into <see cref="DwgFileDepList.Features"/> naming the dependency kind.</summary>
    public required int FeatureIndex { get; init; }

    /// <summary>Unix timestamp of the dependency file (as stored).</summary>
    public required int Timestamp { get; init; }

    /// <summary>Dependency file size in bytes (as stored).</summary>
    public required int FileSize { get; init; }

    /// <summary>Whether the dependency affects how the drawing is displayed.</summary>
    public required bool AffectsGraphics { get; init; }

    /// <summary>Number of references to this file in the drawing.</summary>
    public required int ReferenceCount { get; init; }
}

/// <summary>
/// The file dependency list from <c>AcDb:FileDepList</c>: feature names and
/// external references (xrefs, images, fonts, plot styles) the drawing depends on.
/// </summary>
public sealed class DwgFileDepList
{
    /// <summary>Feature names (dependency kinds such as fonts or xrefs).</summary>
    public IReadOnlyList<string> Features { get; init; } = [];

    /// <summary>File dependency records.</summary>
    public IReadOnlyList<DwgFileDependency> Files { get; init; } = [];
}
