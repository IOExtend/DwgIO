namespace DwgIO.Model;

/// <summary>The decoded geometry flavour of an entity.</summary>
public enum DwgEntityKind
{
    /// <summary>No geometry decoder for this type code yet.</summary>
    Unknown,

    /// <summary>LINE: start and end points.</summary>
    Line,

    /// <summary>CIRCLE: centre and radius.</summary>
    Circle,

    /// <summary>ARC: centre, radius and start/end angles.</summary>
    Arc,

    /// <summary>POINT: a single location.</summary>
    Point,
}

/// <summary>
/// A decoded drawing entity (LINE/CIRCLE/ARC/POINT): common data plus geometry.
/// Entities of other type codes remain inventory-only in this milestone.
/// </summary>
public sealed class DwgEntity
{
    /// <summary>Handle of the entity.</summary>
    public required ulong Handle { get; init; }

    /// <summary>Raw object type code.</summary>
    public required ushort TypeCode { get; init; }

    /// <summary>Type name for the code, e.g. "LINE".</summary>
    public required string TypeName { get; init; }

    /// <summary>Which geometry, if any, was decoded.</summary>
    public required DwgEntityKind Kind { get; init; }

    /// <summary>Geometry payload — meaning depends on <see cref="Kind"/>:
    /// Line = [x1,y1,z1,x2,y2,z2]; Circle/Arc = [cx,cy,cz,radius] (+ [start,end] radians for Arc);
    /// Point = [x,y,z]. Null for non-decoded kinds.</summary>
    public double[]? Geometry { get; init; }

    /// <summary>DXF 62 colour index (low byte of the stored colour value) from the common preamble.</summary>
    public int ColorIndex { get; init; }

    /// <summary>DXF 48 linetype scale from the common preamble.</summary>
    public double LineTypeScale { get; init; }

    /// <summary>One-line description: type, handle and decoded geometry.</summary>
    public override string ToString() =>
        Kind switch
        {
            DwgEntityKind.Line when Geometry is { Length: 6 } g =>
                $"LINE {Handle:X}: ({g[0]:F3},{g[1]:F3},{g[2]:F3}) → ({g[3]:F3},{g[4]:F3},{g[5]:F3})",
            DwgEntityKind.Circle when Geometry is { Length: 4 } g =>
                $"CIRCLE {Handle:X}: c=({g[0]:F3},{g[1]:F3},{g[2]:F3}) r={g[3]:F3}",
            DwgEntityKind.Arc when Geometry is { Length: 6 } g =>
                $"ARC {Handle:X}: c=({g[0]:F3},{g[1]:F3},{g[2]:F3}) r={g[3]:F3} [{g[4]:F3}..{g[5]:F3} rad]",
            DwgEntityKind.Point when Geometry is { Length: 3 } g =>
                $"POINT {Handle:X}: ({g[0]:F3},{g[1]:F3},{g[2]:F3})",
            _ => $"{TypeName} {Handle:X}",
        };
}
