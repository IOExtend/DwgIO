using DwgIO.Internal.Container;
using Xunit;

namespace DwgIO.Tests;

public class BitReaderTests
{
    [Fact]
    public void ReadBits_MsbFirst()
    {
        // 0b10110000_11001100: first 4 bits = 1011 = 0xB, next 4 = 0000 = 0, etc.
        var reader = new BitReaderMsb([0b1011_0000, 0b1100_1100]);

        Assert.Equal(0xBUL, reader.ReadBits(4));
        Assert.Equal(0x0UL, reader.ReadBits(4));
        Assert.Equal(0b1100_1100UL, reader.ReadBits(8));
    }

    /// <summary>Reads a little-endian 32-bit integer at the current bit position.</summary>
    [Fact]
    public void ReadRawUInt32_LittleEndian()
    {
        var reader = new BitReaderMsb([0x78, 0x56, 0x34, 0x12]);
        Assert.Equal(0x12345678u, reader.ReadRawUInt32());
    }

    [Fact]
    public void ReadVarUInt_MultiByteContinuation()
    {
        // 0x80 | 0x01 then 0x02: value = 1 | 2 << 7 = 0x101.
        var reader = new BitReaderMsb([0x81, 0x02]);
        Assert.Equal(0x101UL, reader.ReadVarUInt());
    }

    [Fact]
    public void ReadVarInt_Negative()
    {
        // Terminator byte 0x40|0x01: value 1, sign set → -1.
        var reader = new BitReaderMsb([0x41]);
        Assert.Equal(-1L, reader.ReadVarInt());
    }

    [Fact]
    public void ReadVarInt_MultiBytePositive()
    {
        // 0xE4 0x00: continuation 0x64 = 100, terminator 0x00 (6 bits, positive) → 100.
        var reader = new BitReaderMsb([0xE4, 0x00]);
        Assert.Equal(100L, reader.ReadVarInt());
    }

    [Fact]
    public void ReadHandle_CodeAndCounter()
    {
        // code=5, counter=2, bytes 0x12 0x34 → value 0x1234.
        var reader = new BitReaderMsb([0x52, 0x12, 0x34]);
        var handle = reader.ReadHandle();
        Assert.Equal(5, handle.Code);
        Assert.Equal(2, handle.Counter);
        Assert.Equal(0x1234UL, handle.Value);
    }

    [Fact]
    public void ReadObjectType_R2010_Dispatch()
    {
        // Tag 00 → one byte 0x30; tag 01 → one byte 0x01 + 0x1F0; tag 10 → two bytes LE.
        // (Each tag is the first 2 bits, so the following bytes straddle byte boundaries.)
        var reader = new BitReaderMsb([0x0C, 0x10, 0x18, 0xD0, 0x48]);
        Assert.Equal(0x30, reader.ReadObjectType(r2010Plus: true));
        Assert.Equal(0x1F0 + 0x01, reader.ReadObjectType(r2010Plus: true));
        Assert.Equal(0x1234, reader.ReadObjectType(r2010Plus: true));
    }
}
