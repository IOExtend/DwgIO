using DwgIO.Internal.Parsing;
using DwgIO.Model;
using Xunit;

namespace DwgIO.Tests;

public class EntityTypeTests
{
    [Fact]
    public void Dictionary_IsNotAnEntity()
    {
        Assert.Equal("DICTIONARY", EntityDecoder.TypeName(0x2A));
        Assert.False(EntityDecoder.IsEntityCode(0x2A));
    }

    [Theory]
    [InlineData(0x01)] // TEXT
    [InlineData(0x13)] // LINE
    [InlineData(0x2B)] // OLEFRAME
    [InlineData(0x2F)] // MLINE
    public void DrawableFixedCodes_AreEntities(ushort typeCode)
    {
        Assert.True(EntityDecoder.IsEntityCode(typeCode));
    }

    [Theory]
    [InlineData(0x00)] // UNUSED
    [InlineData(0x09)] // reserved in the supported versions
    [InlineData(0x30)] // BLOCK CONTROL OBJ
    public void ObjectsAndOutOfRangeCodes_AreNotEntities(ushort typeCode)
    {
        Assert.False(EntityDecoder.IsEntityCode(typeCode));
    }

    [Theory]
    [InlineData(0x08, "MINSERT")]
    [InlineData(0x0B, "VERTEX_3D")]
    [InlineData(0x14, "DIMENSION_ORDINATE")]
    [InlineData(0x1D, "POLYLINE_PFACE")]
    [InlineData(0x1F, "SOLID")]
    [InlineData(0x21, "SHAPE")]
    [InlineData(0x23, "ELLIPSE")]
    [InlineData(0x2E, "TOLERANCE")]
    [InlineData(0x31, "BLOCK_RECORD")]
    [InlineData(0x4A, "OLE2FRAME")]
    [InlineData(0x4D, "LWPOLYLINE")]
    [InlineData(0x50, "PLACEHOLDER")]
    [InlineData(0x52, "LAYOUT")]
    [InlineData(0x1F2, "PROXY_ENTITY")]
    [InlineData(0x1F3, "PROXY_OBJECT")]
    public void FixedCodes_HaveTheirLibredwgNames(ushort typeCode, string expected)
    {
        Assert.Equal(expected, EntityDecoder.TypeName(typeCode));
        Assert.Equal(expected, DwgObjectTypes.Name(typeCode));
    }

    [Theory]
    [InlineData(0x4D)] // LWPOLYLINE
    [InlineData(0x4E)] // HATCH
    [InlineData(0x4A)] // OLE2FRAME
    [InlineData(0x1F2)] // PROXY_ENTITY
    public void DrawableCodesOutsideTheClassicRange_AreEntities(ushort typeCode)
    {
        Assert.True(EntityDecoder.IsEntityCode(typeCode));
        Assert.True(DwgObjectTypes.IsEntity(typeCode));
    }

    [Theory]
    [InlineData(0x30)] // BLOCK_CONTROL
    [InlineData(0x32)] // LAYER_CONTROL
    [InlineData(0x39)] // LTYPE
    [InlineData(0x48)] // GROUP
    [InlineData(0x4F)] // XRECORD
    [InlineData(0x51)] // VBA_PROJECT
    [InlineData(0x52)] // LAYOUT
    [InlineData(0x1F3)] // PROXY_OBJECT
    public void TableAndControlObjects_AreNotEntities(ushort typeCode)
    {
        Assert.False(EntityDecoder.IsEntityCode(typeCode));
        Assert.False(DwgObjectTypes.IsEntity(typeCode));
    }

    [Fact]
    public void EveryCodeInsideTheTableHasANameAndAFlag()
    {
        foreach (ushort code in new ushort[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x38, 0x39, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F, 0x50, 0x51, 0x52 })
        {
            string name = DwgObjectTypes.Name(code);
            Assert.False(name.StartsWith("Object_", StringComparison.Ordinal), $"code 0x{code:X} has no name");
            // The flag exists by definition; the test pins the pairs that must agree.
            Assert.Equal(code is >= 0x01 and <= 0x2F and not 0x2A || code is 0x4A or 0x4D or 0x4E, DwgObjectTypes.IsEntity(code));
        }

        Assert.Contains("TEXT", DwgObjectTypes.KnownNames);
        Assert.Contains("HATCH", DwgObjectTypes.KnownNames);
    }

    [Fact]
    public void UnknownAndClassCodes_FallBack()
    {
        Assert.Equal("Object_53", DwgObjectTypes.Name(0x53));
        Assert.Equal("Class_502", DwgObjectTypes.Name(502));
        Assert.False(DwgObjectTypes.IsEntity(502)); // no decoded class list yet: treated as an object
    }

    [Fact]
    public void Dictionary_IsNotDecoded()
    {
        var info = new DwgObjectInfo
        {
            StreamOffset = 4,
            SizeBytes = 2,
            TypeCode = 0x2A,
            Handle = 0x10,
            StreamHandle = new DwgHandle(0, 1, 0x10),
            BitsStart = 4,
            BitsLength = 16,
        };

        Assert.Null(EntityDecoder.Decode(info, [], 2018));
    }
}
