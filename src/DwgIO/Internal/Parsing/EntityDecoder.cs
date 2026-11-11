using DwgIO.Internal.Container;
using DwgIO.Model;

namespace DwgIO.Internal.Parsing;

/// <summary>
/// Decodes entity records: the common entity preamble followed by the
/// type-specific body. This milestone decodes geometry for LINE, CIRCLE, ARC and POINT;
/// other entity codes are reported by name only.
/// </summary>
internal static class EntityDecoder
{
    /// <summary>Well-known DWG type codes (subset covering the common ones).</summary>
    private static readonly Dictionary<ushort, string> TypeNames = new()
    {
        [0x01] = "TEXT",
        [0x02] = "ATTRIB",
        [0x03] = "ATTDEF",
        [0x04] = "BLOCK",
        [0x05] = "ENDBLK",
        [0x06] = "SEQEND",
        [0x07] = "INSERT",
        [0x0B] = "VERTEX",
        [0x0F] = "2DPOLYLINE",
        [0x10] = "3DPOLYLINE",
        [0x11] = "ARC",
        [0x12] = "CIRCLE",
        [0x13] = "LINE",
        [0x1B] = "POINT",
        [0x1C] = "3DFACE",
        [0x21] = "ELLIPSE",
        [0x22] = "VIEWPORT",
        [0x24] = "SPLINE",
        [0x26] = "3DSOLID",
        [0x28] = "RAY",
        [0x29] = "XLINE",
        [0x2A] = "DICTIONARY",
        [0x2C] = "MTEXT",
        [0x2D] = "LEADER",
        [0x31] = "BLOCK_RECORD",
        [0x32] = "LAYER_TABLE",
        [0x33] = "LAYER",
        [0x34] = "STYLE_TABLE",
        [0x35] = "STYLE",
        [0x38] = "LTYPE_TABLE",
        [0x39] = "LTYPE",
        [0x4E] = "HATCH",
        [0x4F] = "XRECORD",
    };

    /// <summary>Name for a DWG type code, or <c>Object_XX</c> when the code is not in the table.</summary>
    public static string TypeName(ushort typeCode) =>
        TypeNames.TryGetValue(typeCode, out var name) ? name : $"Object_{typeCode:X}";

    /// <summary>Entities are the drawable codes 0x01..0x2F; higher codes are dictionary/table objects.</summary>
    public static bool IsEntityCode(ushort typeCode) => typeCode is >= 0x01 and <= 0x2F;

    /// <summary>
    /// Decodes an entity from the record payload (starting right after the size prefix).
    /// Returns null when the type has no geometry decoder yet or the record is not an entity.
    /// </summary>
    public static DwgEntity? Decode(DwgObjectInfo info, byte[] stream, int year)
    {
        if (!IsEntityCode(info.TypeCode))
        {
            return null;
        }

        var reader = new BitReaderMsb(stream[info.BitsStart..(info.BitsStart + info.BitsLength)]);
        // -- Record header re-read ---------------------------------------------
        // The payload layout is [handle-stream size in bits (R2010+)][type][handle][common data…];
        // the inventory walk already consumed these fields for its own fields, so the body
        // decoder re-reads them to position itself at the common entity data.
        if (year >= 2010)
        {
            reader.ReadVarUInt();
        }

        reader.ReadObjectType(year >= 2010);
        reader.ReadHandle();

        // -- Common entity data ------------------------------------------------
        SkipExtendedData(reader);
        if (reader.ReadBit())
        {
            // graphics preview block: raw 32-bit size up to R2007, packed 64-bit size from R2010
            ulong graphicsSize = year >= 2010 ? reader.ReadPackedLong() : reader.ReadRawUInt32();
            if (graphicsSize > 64 * 1024 * 1024)
            {
                throw new DwgException($"Entity {info.Handle:X}: graphics block of {graphicsSize} bytes exceeds the sanity cap.");
            }

            reader.SkipBytes((int)graphicsSize);
        }

        reader.ReadTwoBits(); // entity mode
        reader.ReadPackedInt(); // reactor count: back-reference handles consumed from the tail later
        reader.ReadBit(); // no extension dictionary flag (R2004+)
        if (year >= 2013)
        {
            reader.ReadBit(); // has AcDs binary data
        }

        uint colorValue = reader.ReadPackedShort();
        int colorIndex = (int)(colorValue & 0xFF);
        uint colorFlags = colorValue >> 8;
        if ((colorFlags & 0x40) == 0 && (colorFlags & 0x80) != 0)
        {
            reader.ReadPackedInt(); // true-colour RGB
        }

        if ((colorFlags & 0x20) != 0)
        {
            reader.ReadPackedInt(); // transparency
        }

        if ((colorFlags & 0x41) == 0x41)
        {
            SkipText(reader, year);
        }

        if ((colorFlags & 0x42) == 0x42)
        {
            SkipText(reader, year);
        }

        double lineTypeScale = reader.ReadPackedDouble();
        reader.ReadTwoBits(); // linetype flags
        reader.ReadTwoBits(); // plot-style flags (R2000+)
        reader.ReadTwoBits(); // material flag (R2007+)
        reader.ReadRawByte(); // shadow flags (R2007+)
        if (year >= 2010)
        {
            reader.ReadBit(); // full visual style
            reader.ReadBit(); // face visual style
            reader.ReadBit(); // edge visual style
        }

        reader.ReadPackedShort(); // invisibility
        reader.ReadRawByte(); // lineweight (R2000+)

        // -- Type-specific body ------------------------------------------------
        (DwgEntityKind kind, double[]? geometry)? body = info.TypeCode switch
        {
            0x13 => DecodeLine(reader),
            0x12 => DecodeCircle(reader),
            0x11 => DecodeArc(reader),
            0x1B => DecodePoint(reader),
            _ => null,
        };

        return new DwgEntity
        {
            Handle = info.Handle,
            TypeCode = info.TypeCode,
            TypeName = TypeName(info.TypeCode),
            Kind = body?.kind ?? DwgEntityKind.Unknown,
            Geometry = body?.geometry,
            ColorIndex = colorIndex,
            LineTypeScale = lineTypeScale,
        };
    }

    private static void SkipExtendedData(BitReaderMsb reader)
    {
        for (int i = 0; i < 256; i++)
        {
            uint size = reader.ReadPackedShort();
            if (size == 0)
            {
                return;
            }

            reader.ReadHandle(); // appid
            for (uint j = 0; j < size; j++)
            {
                reader.ReadRawByte();
            }
        }

        throw new DwgException("Extended data loop exceeded 256 iterations; payload is malformed.");
    }

    private static void SkipText(BitReaderMsb reader, int year)
    {
        // Text value: packed 16-bit length; R2007+ counts UTF-16 characters, earlier releases count
        // codepage bytes.
        int length = (int)reader.ReadPackedShort();
        if (year >= 2007)
        {
            reader.SkipBytes(length * 2);
        }
        else
        {
            reader.SkipBytes(length);
        }
    }

    private static double ReadThickness(BitReaderMsb reader) => reader.ReadBit() ? 0.0 : reader.ReadPackedDouble();

    private static double[] ReadExtrusion(BitReaderMsb reader)
    {
        if (reader.ReadBit())
        {
            return [0.0, 0.0, 1.0]; // default extrusion
        }

        return [reader.ReadPackedDouble(), reader.ReadPackedDouble(), reader.ReadPackedDouble()];
    }

    private static (DwgEntityKind, double[]) DecodeLine(BitReaderMsb reader)
    {
        // R2000+: z-is-zero flag, start coordinates as raw doubles, end coordinates relative
        // to them, thickness, extrusion.
        bool zIsZero = reader.ReadBit();
        double x1 = reader.ReadRawDouble();
        double x2 = reader.ReadDoubleWithDefault(x1);
        double y1 = reader.ReadRawDouble();
        double y2 = reader.ReadDoubleWithDefault(y1);
        double z1 = 0.0;
        double z2 = 0.0;
        if (!zIsZero)
        {
            z1 = reader.ReadRawDouble();
            z2 = reader.ReadDoubleWithDefault(z1);
        }

        ReadThickness(reader);
        ReadExtrusion(reader);
        return (DwgEntityKind.Line, [x1, y1, z1, x2, y2, z2]);
    }

    private static (DwgEntityKind, double[]) DecodeCircle(BitReaderMsb reader)
    {
        double cx = reader.ReadPackedDouble();
        double cy = reader.ReadPackedDouble();
        double cz = reader.ReadPackedDouble();
        double radius = reader.ReadPackedDouble();
        ReadThickness(reader);
        ReadExtrusion(reader);
        return (DwgEntityKind.Circle, [cx, cy, cz, radius]);
    }

    private static (DwgEntityKind, double[]) DecodeArc(BitReaderMsb reader)
    {
        double cx = reader.ReadPackedDouble();
        double cy = reader.ReadPackedDouble();
        double cz = reader.ReadPackedDouble();
        double radius = reader.ReadPackedDouble();
        ReadThickness(reader);
        ReadExtrusion(reader);
        double startAngle = reader.ReadPackedDouble();
        double endAngle = reader.ReadPackedDouble();
        return (DwgEntityKind.Arc, [cx, cy, cz, radius, startAngle, endAngle]);
    }

    private static (DwgEntityKind, double[]) DecodePoint(BitReaderMsb reader)
    {
        double x = reader.ReadPackedDouble();
        double y = reader.ReadPackedDouble();
        double z = reader.ReadPackedDouble();
        ReadThickness(reader);
        ReadExtrusion(reader);
        reader.ReadPackedDouble(); // x-axis angle
        return (DwgEntityKind.Point, [x, y, z]);
    }
}
