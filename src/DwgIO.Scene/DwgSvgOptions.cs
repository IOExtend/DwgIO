namespace DwgIO.Scene;

/// <summary>Options of the SVG export of model space.</summary>
public sealed class DwgSvgOptions
{
    /// <summary>
    /// Pixels per drawing unit. The geometry is scaled by it, so the <c>viewBox</c>, <c>width</c> and
    /// <c>height</c> grow together: the drawing keeps its aspect at every scale, and one <c>viewBox</c> unit is
    /// one pixel.
    /// </summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>Stroke width of the drawn entities, in pixels; it stays fixed while the scale zooms the drawing.</summary>
    public double StrokeWidth { get; set; } = 1.0;

    /// <summary>Background colour, as CSS. AutoCAD renders on a dark background, but SVG files are usually
    /// viewed on light ones, so the default is white and colour 7 (white/black) renders black.</summary>
    public string Background { get; set; } = "white";
}
