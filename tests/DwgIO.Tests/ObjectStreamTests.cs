using DwgIO.Internal.Container;
using Xunit;

namespace DwgIO.Tests;

public class ObjectStreamTests
{
    /// <summary>
    /// Builds a tiny object stream: the 0x0DCA prefix and one object record
    /// <c>[size=10][handle-stream bits=3][type 0x04][handle 7][payload][CRC]</c>.
    /// </summary>
    private static byte[] BuildStream()
    {
        var record = new List<byte>
        {
            0x0A, 0x00, // size prefix: 10
            0x03, // handle-stream size in bits = 3 (unused by the inventory walk)
            0x01, 0x00, 0x41, 0xC0, // type + handle bit-packed
        };
        record.AddRange(new byte[6]); // payload filler
        record.AddRange([0xAA, 0xBB]); // CRC placeholder

        var data = new List<byte>
        {
            0xCA, 0x0D, 0x00, 0x00, // 0x0DCA stream prefix (the object record itself sits at offset 4)
        };
        data.AddRange(record);
        return [.. data];
    }

    [Fact]
    public void Walk_Sequential_ReadsRecord()
    {
        var data = BuildStream();

        var objects = ObjectStreamWalker.Walk(data, r2010Plus: true);

        var obj = Assert.Single(objects);
        Assert.Equal(4, obj.StreamOffset);
        Assert.Equal(10u, obj.SizeBytes);
        Assert.Equal(4, obj.TypeCode);
        Assert.Equal(7UL, obj.Handle);
        Assert.Equal(7UL, obj.StreamHandle.Value);
    }

    [Fact]
    public void Walk_TornRecord_IsSkipped()
    {
        var data = BuildStream();
        // Corrupt the size prefix of the record: overwrite its size to claim more bytes
        // than the stream holds → the record must be skipped, not crash the walk.
        data[4] = 0xFE;
        data[5] = 0xFF;

        var objects = ObjectStreamWalker.Walk(data, r2010Plus: true);

        Assert.Empty(objects);
    }
}
