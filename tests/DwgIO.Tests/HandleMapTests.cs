using DwgIO.Model;
using Xunit;

namespace DwgIO.Tests;

public class HandleMapTests
{
    private static byte[] BuildSection(params (ulong HandleDelta, long OffsetDelta)[] pairs)
    {
        var payload = new List<byte>();
        foreach (var (handleDelta, offsetDelta) in pairs)
        {
            payload.AddRange(EncodeVarUInt(handleDelta));
            payload.AddRange(EncodeVarInt(offsetDelta));
        }

        var result = new List<byte>
        {
            (byte)(((payload.Count + 2) >> 8) & 0xFF), // big-endian u16 size (payload + 2-byte CRC)
            (byte)((payload.Count + 2) & 0xFF),
        };
        result.AddRange(payload);
        result.AddRange([0xAA, 0xBB]); // CRC placeholder — not validated by the reader
        result.AddRange([0x00, 0x00]); // empty block terminates the map
        return [.. result];
    }

    private static List<byte> EncodeVarUInt(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            bytes.Add(value > 0 ? (byte)(b | 0x80) : b);
        }
        while (value > 0);
        return bytes;
    }

    private static List<byte> EncodeVarInt(long value)
    {
        bool negative = value < 0;
        ulong magnitude = (ulong)Math.Abs(value);
        var bytes = new List<byte>();
        while (magnitude > 0x3F)
        {
            bytes.Add((byte)(0x80 | (magnitude & 0x7F)));
            magnitude >>= 7;
        }

        byte last = (byte)magnitude;
        if (negative)
        {
            last |= 0x40;
        }

        bytes.Add(last);
        return bytes;
    }

    [Fact]
    public void Parse_SingleBlock_AbsoluteFirstPair()
    {
        var data = BuildSection((5, 32), (1, 5), (2, 16));

        var map = DwgHandleMap.Parse(data);

        Assert.Equal(3, map.Count);
        Assert.Equal(5UL, map.Entries[0].Handle);
        Assert.Equal(32UL, map.Entries[0].Offset);
        Assert.Equal(6UL, map.Entries[1].Handle);
        Assert.Equal(37UL, map.Entries[1].Offset);
        Assert.Equal(8UL, map.Entries[2].Handle);
        Assert.Equal(53UL, map.Entries[2].Offset);
        Assert.NotNull(map.Find(6));
        Assert.Null(map.Find(999));
    }

    [Fact]
    public void Parse_NegativeOffsetDelta()
    {
        var map = DwgHandleMap.Parse(BuildSection((10, 100), (1, -30)));

        Assert.Equal(11UL, map.Entries[1].Handle);
        Assert.Equal(70UL, map.Entries[1].Offset);
    }

    [Fact]
    public void Parse_EmptyTerminator_Stops()
    {
        // [size=2 (CRC only)][CRC][terminator 00 00][garbage after — must be ignored]
        byte[] data = [0x00, 0x02, 0xAA, 0xBB, 0x00, 0x00, 0xFF, 0xFF, 0xFF];

        Assert.Empty(DwgHandleMap.Parse(data).Entries);
    }
}
