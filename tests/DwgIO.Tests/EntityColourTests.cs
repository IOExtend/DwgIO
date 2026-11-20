using System.Text.Json;
using DwgIO.Internal.Container;
using DwgIO.Internal.Parsing;
using DwgIO.Json;
using DwgIO.Model;
using Xunit;

namespace DwgIO.Tests;

/// <summary>Synthetic entity records, bit-built, exercising the common entity data (#40).</summary>
public class EntityColourTests
{
    [Fact]
    public void ShortConstantColour256_DecodesAsByLayer()
    {
        var entity = DecodeLine(writer => writer.WritePackedShortTag3(), expectTransparency: false);

        Assert.Equal(256, entity.ColorIndex);
    }

    [Fact]
    public void FullWidthColour0x2100_DecodesAsByLayerWithTransparency()
    {
        var entity = DecodeLine(writer => writer.WritePackedShortRaw(0x2100), expectTransparency: true);

        // 0x2100 = index 256 in the low 9 bits, transparency flag in the upper byte.
        Assert.Equal(256, entity.ColorIndex);
    }

    [Fact]
    public void FullWidthColourWithTransparencyKeepsTheIndexByte()
    {
        // 0x2005 = transparency flag (raw bit 13) plus index 5 in the low 9 bits.
        var entity = DecodeLine(writer => writer.WritePackedShortRaw(0x2005), expectTransparency: true);

        Assert.Equal(5, entity.ColorIndex);
    }

    [Fact]
    public void JsonExport_TellsByBlockAndByLayerApart()
    {
        var document = new DwgDocument(
            "synthetic.dwg",
            new DwgFileHeader
            {
                Version = new DwgVersionInfo
                {
                    Magic = "AC1024",
                    Release = "R2010",
                    Family = DwgContainerFamily.R2004Family,
                    Year = 2010,
                },
                MaintenanceVersion = 0,
                PreviewOffset = 0,
                Codepage = 30,
            },
            [
                new DwgEntity { Handle = 0x2A, TypeCode = 0x13, TypeName = "LINE", Kind = DwgEntityKind.Line, Geometry = [0, 0, 0, 1, 0, 0], ColorIndex = 0 },
                new DwgEntity { Handle = 0x2B, TypeCode = 0x13, TypeName = "LINE", Kind = DwgEntityKind.Line, Geometry = [0, 0, 0, 2, 0, 0], ColorIndex = 256 },
            ]);

        using var stream = new MemoryStream();
        DwgJsonExporter.Write(document, stream, DwgJsonExportOptions.Default);

        using var json = JsonDocument.Parse(stream.ToArray());
        var entities = json.RootElement.GetProperty("entities");
        Assert.Equal(0, entities[0].GetProperty("colorIndex").GetInt32());
        Assert.Equal(256, entities[1].GetProperty("colorIndex").GetInt32());
    }

    private static DwgEntity DecodeLine(Action<RecordBitWriter> writeColour, bool expectTransparency)
    {
        var writer = new RecordBitWriter();
        writer.WriteRawByte(0x00);              // R2010+ record-size VarUInt placeholder
        writer.WriteTwoBits(0);                 // object type: tag 00 → next byte
        writer.WriteRawByte(0x13);              // LINE
        writer.WriteHandle(code: 0, count: 0);  // handle 0
        writer.WriteTwoBits(2);                 // extended data: packed short tag 10 → 0 (end)
        writer.WriteBit(false);                 // no graphics preview
        writer.WriteTwoBits(0);                 // entity mode
        writer.WriteTwoBits(2);                 // reactors: packed int tag 10 → 0
        writer.WriteBit(false);                 // no extension dictionary
        writer.WriteBit(false);                 // no AcDs data (R2013+)
        writeColour(writer);
        if (expectTransparency)
        {
            writer.WriteTwoBits(2);             // transparency value: packed int tag 10 → 0
        }

        writer.WriteTwoBits(1);                 // linetype scale: packed double tag 01 → 1.0
        writer.WriteTwoBits(0);                 // linetype flags
        writer.WriteTwoBits(0);                 // plot-style flags
        writer.WriteTwoBits(0);                 // material flag (read for every release today, see #23)
        writer.WriteRawByte(0);                 // shadow flags
        writer.WriteBit(false);                 // full visual style (R2010+)
        writer.WriteBit(false);                 // face visual style
        writer.WriteBit(false);                 // edge visual style
        writer.WriteTwoBits(2);                 // invisibility → 0
        writer.WriteRawByte(0);                 // lineweight
        // LINE body
        writer.WriteBit(true);                  // z is zero
        writer.WriteRawDouble(1.0);
        writer.WriteTwoBits(0);                 // end x = start x
        writer.WriteRawDouble(2.0);
        writer.WriteTwoBits(0);                 // end y = start y
        writer.WriteBit(true);                  // thickness → 0.0
        writer.WriteBit(true);                  // extrusion → default

        var info = new DwgObjectInfo
        {
            StreamOffset = 0,
            SizeBytes = 2,
            TypeCode = 0x13,
            Handle = 0x10,
            StreamHandle = new DwgHandle(0, 1, 0x10),
            BitsStart = 0,
            BitsLength = (writer.BitsUsed + 7) / 8,
        };

        var entity = EntityDecoder.Decode(info, writer.ToArray(), 2018);
        Assert.NotNull(entity);
        Assert.Equal(DwgEntityKind.Line, entity.Kind);
        return entity;
    }

}
