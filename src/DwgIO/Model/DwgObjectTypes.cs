namespace DwgIO.Model;

/// <summary>
/// The fixed object type codes of the DWG format: the name and the entity-or-object flag of every fixed
/// code, per libredwg's include/dwg.h. Class-defined records (codes 500 and above) take their name from the
/// drawing's class list, which the reader does not decode yet, so they fall back to <c>Class_XX</c>.
/// </summary>
public static class DwgObjectTypes
{
    private const ushort ProxyEntityCode = 0x1F2;
    private const ushort ProxyObjectCode = 0x1F3;

    private static readonly Dictionary<ushort, string> FixedNames = new()
    {
        [0x01] = "TEXT",
        [0x02] = "ATTRIB",
        [0x03] = "ATTDEF",
        [0x04] = "BLOCK",
        [0x05] = "ENDBLK",
        [0x06] = "SEQEND",
        [0x07] = "INSERT",
        [0x08] = "MINSERT",
        [0x0A] = "VERTEX_2D",
        [0x0B] = "VERTEX_3D",
        [0x0C] = "VERTEX_MESH",
        [0x0D] = "VERTEX_PFACE",
        [0x0E] = "VERTEX_PFACE_FACE",
        [0x0F] = "POLYLINE_2D",
        [0x10] = "POLYLINE_3D",
        [0x11] = "ARC",
        [0x12] = "CIRCLE",
        [0x13] = "LINE",
        [0x14] = "DIMENSION_ORDINATE",
        [0x15] = "DIMENSION_LINEAR",
        [0x16] = "DIMENSION_ALIGNED",
        [0x17] = "DIMENSION_ANG3PT",
        [0x18] = "DIMENSION_ANG2LN",
        [0x19] = "DIMENSION_RADIUS",
        [0x1A] = "DIMENSION_DIAMETER",
        [0x1B] = "POINT",
        [0x1C] = "3DFACE",
        [0x1D] = "POLYLINE_PFACE",
        [0x1E] = "POLYLINE_MESH",
        [0x1F] = "SOLID",
        [0x20] = "TRACE",
        [0x21] = "SHAPE",
        [0x22] = "VIEWPORT",
        [0x23] = "ELLIPSE",
        [0x24] = "SPLINE",
        [0x25] = "REGION",
        [0x26] = "3DSOLID",
        [0x27] = "BODY",
        [0x28] = "RAY",
        [0x29] = "XLINE",
        [0x2A] = "DICTIONARY",
        [0x2B] = "OLEFRAME",
        [0x2C] = "MTEXT",
        [0x2D] = "LEADER",
        [0x2E] = "TOLERANCE",
        [0x2F] = "MLINE",
        [0x30] = "BLOCK_CONTROL",
        [0x31] = "BLOCK_RECORD",
        [0x32] = "LAYER_CONTROL",
        [0x33] = "LAYER",
        [0x34] = "STYLE_CONTROL",
        [0x35] = "STYLE",
        [0x38] = "LTYPE_CONTROL",
        [0x39] = "LTYPE",
        [0x3C] = "VIEW_CONTROL",
        [0x3D] = "VIEW",
        [0x3E] = "UCS_CONTROL",
        [0x3F] = "UCS",
        [0x40] = "VPORT_CONTROL",
        [0x41] = "VPORT",
        [0x42] = "APPID_CONTROL",
        [0x43] = "APPID",
        [0x44] = "DIMSTYLE_CONTROL",
        [0x45] = "DIMSTYLE",
        [0x46] = "VX_CONTROL",
        [0x47] = "VX_TABLE_RECORD",
        [0x48] = "GROUP",
        [0x49] = "MLINESTYLE",
        [0x4A] = "OLE2FRAME",
        [0x4B] = "DUMMY",
        [0x4C] = "LONG_TRANSACTION",
        [0x4D] = "LWPOLYLINE",
        [0x4E] = "HATCH",
        [0x4F] = "XRECORD",
        [0x50] = "PLACEHOLDER",
        [0x51] = "VBA_PROJECT",
        [0x52] = "LAYOUT",
        [ProxyEntityCode] = "PROXY_ENTITY",
        [ProxyObjectCode] = "PROXY_OBJECT",
    };

    /// <summary>Codes that are drawable entities outside the 0x01-0x2F range (per libredwg).</summary>
    private static readonly HashSet<ushort> ExtraEntities = [0x4A, 0x4D, 0x4E, ProxyEntityCode];

    /// <summary>Name for a fixed type code: the AutoCAD or table name, <c>Class_XX</c> for the class-defined
    /// codes and <c>Object_XX</c> for an unknown fixed code.</summary>
    public static string Name(ushort typeCode) =>
        FixedNames.TryGetValue(typeCode, out var name)
            ? name
            : typeCode >= 500 ? $"Class_{typeCode}" : $"Object_{typeCode:X}";

    /// <summary>Tells whether a record of this code is a drawable entity: the 0x01-0x2F range without
    /// the reserved 0x09 and DICTIONARY (0x2A), plus LWPOLYLINE, HATCH, OLE2FRAME and PROXY_ENTITY. Class-defined codes
    /// (500+) are entities when their class says so; without a decoded class list they are treated as objects.</summary>
    public static bool IsEntity(ushort typeCode) =>
        (typeCode is >= 0x01 and <= 0x2F and not (0x09 or 0x2A)) || ExtraEntities.Contains(typeCode);

    /// <summary>Every fixed type name the table knows, for validating a filter list.</summary>
    public static IReadOnlyCollection<string> KnownNames => FixedNames.Values;
}
