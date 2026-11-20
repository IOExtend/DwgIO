namespace DwgIO.Scene;

/// <summary>Options of the DWG → scene conversion.</summary>
public sealed class DwgSceneOptions
{
    /// <summary>
    /// Length unit of the drawing's coordinates and the scale the conversion applies, as one value:
    /// <c>mm</c>, <c>cm</c>, <c>m</c> (the default, one drawing unit = one metre), <c>ft</c> or <c>in</c>.
    /// A DWG stores plain numbers without a unit; the header variable that names the unit is not decoded yet,
    /// so the unit is a caller decision. The scene records the choice in <see cref="IOScene.SceneModel.SourceLengthUnit"/>
    /// and reports it in <see cref="DwgSceneResult.UnitAssumed"/>.
    /// </summary>
    public string LengthUnit { get; set; } = "m";

    /// <summary>
    /// Key of the scene model: stable, unique and non-empty, written to <see cref="IOScene.SceneModel.Key"/>. The
    /// default is the source file name without directory. A key must be given when a document converted from memory
    /// is read, which has no file name.
    /// </summary>
    public string? ModelKey { get; set; }
}
