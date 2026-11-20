using IOScene;

namespace DwgIO.Scene;

/// <summary>
/// The scene of a drawing and the report of the conversion: what was converted, what was left out and why.
/// </summary>
public sealed class DwgSceneResult
{
    /// <summary>The scene document, with one model per drawing.</summary>
    public required SceneDocument Document { get; init; }

    /// <summary>Findings of the conversion, in the order they were made.</summary>
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Number of elements the scene holds.</summary>
    public int ElementCount { get; init; }

    /// <summary>Number of elements that carry at least one geometry.</summary>
    public int ElementsWithGeometry { get; init; }

    /// <summary>Number of entities the drawing decoded but the scene left out, because the record had no usable
    /// geometry or a kind the conversion does not place yet.</summary>
    public int EntitiesWithoutGeometry { get; init; }

    /// <summary>The length unit the conversion used, e.g. <c>m</c>.</summary>
    public string? LengthUnit { get; init; }

    /// <summary>Metres per drawing unit the conversion used, or null when unknown.</summary>
    public double? MetresPerUnit { get; init; }

    /// <summary>True when the unit came from the default instead of the drawing, which never names its unit.</summary>
    public bool UnitAssumed { get; init; }
}
