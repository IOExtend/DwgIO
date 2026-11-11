namespace DwgIO.Model;

/// <summary>What kind of image the preview section contains.</summary>
public enum DwgPreviewImageKind
{
    /// <summary>No image found.</summary>
    None,

    /// <summary>Windows bitmap.</summary>
    Bmp,

    /// <summary>PNG image.</summary>
    Png,

    /// <summary>Windows metafile.</summary>
    Wmf,
}

/// <summary>
/// The thumbnail image embedded in the <c>AcDb:Preview</c> section.
/// AutoCAD 2013+ emits PNG previews (code 6), older files BMP (code 2) or WMF (code 3).
/// </summary>
public sealed class DwgPreview
{
    /// <summary>Overall size reported by the preview header.</summary>
    public required uint OverallSize { get; init; }

    /// <summary>Format of <see cref="ImageBytes"/>.</summary>
    public required DwgPreviewImageKind ImageKind { get; init; }

    /// <summary>Image bytes (BMP/PNG/WMF as stored) — empty when no image is present.</summary>
    public required byte[] ImageBytes { get; init; }

    /// <summary>True when <see cref="ImageBytes"/> is not empty.</summary>
    public bool HasImage => ImageBytes.Length > 0;
}
