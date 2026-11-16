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
    [InlineData(0x30)] // BLOCK CONTROL OBJ
    public void ObjectsAndOutOfRangeCodes_AreNotEntities(ushort typeCode)
    {
        Assert.False(EntityDecoder.IsEntityCode(typeCode));
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
