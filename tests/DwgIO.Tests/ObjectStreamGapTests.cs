using DwgIO.Internal.Container;
using DwgIO.Model;
using Xunit;

namespace DwgIO.Tests;

/// <summary>Synthetic object streams and handle maps for the gap diagnostics (#13).</summary>
public class ObjectStreamGapTests
{
    [Fact]
    public void Walk_WithoutMap_StopsAtTheGapSilently()
    {
        var stream = new RecordBitWriter();
        stream.WriteRawByte(0xCA);
        stream.WriteRawByte(0x0D);
        stream.WriteRawByte(0);
        stream.WriteRawByte(0);
        stream.WriteRecord(r => r.WriteLineRecord());
        stream.WriteZeros(9);            // the gap: zero bytes are not a record
        stream.WriteRecord(r => r.WriteLineRecord());

        var objects = ObjectStreamWalker.Walk(stream.ToArray(), r2010Plus: true);

        // The pre-fix behaviour: the walk stops at the first bad size and drops the rest.
        Assert.Single(objects);
    }

    [Fact]
    public void Walk_WithMap_ResumesAtTheNextMapOffsetAndReportsTheGap()
    {
        var stream = new RecordBitWriter();
        stream.WriteRawByte(0xCA);
        stream.WriteRawByte(0x0D);
        stream.WriteRawByte(0);
        stream.WriteRawByte(0);
        int recordAStart = stream.BytesUsed;
        stream.WriteRecord(r => r.WriteLineRecord());
        stream.WriteZeros(9);            // the gap
        int recordBStart = stream.BytesUsed;
        stream.WriteRecord(r => r.WriteLineRecord());

        var mapOffsets = new List<ulong> { (ulong)recordAStart, (ulong)recordBStart };
        var diagnostics = new List<string>();
        var objects = ObjectStreamWalker.Walk(stream.ToArray(), r2010Plus: true, mapOffsets, diagnostics.Add);

        Assert.Equal(2, objects.Count);
        Assert.Equal(recordAStart, objects[0].StreamOffset);
        Assert.Equal(recordBStart, objects[1].StreamOffset);
        var gap = Assert.Single(diagnostics, d => d.Contains("gap at", StringComparison.Ordinal));
        Assert.Contains("resumed", gap, StringComparison.Ordinal);
    }

    [Fact]
    public void Walk_ZeroGapWithoutMap_EndsCleanly()
    {
        // Zero padding is a normal stream end: the walk stops without a diagnostic.
        var stream = new RecordBitWriter();
        stream.WriteRawByte(0xCA);
        stream.WriteRawByte(0x0D);
        stream.WriteRawByte(0);
        stream.WriteRawByte(0);
        stream.WriteRecord(r => r.WriteLineRecord());
        stream.WriteZeros(9);
        stream.WriteRecord(r => r.WriteLineRecord());

        var diagnostics = new List<string>();
        var objects = ObjectStreamWalker.Walk(stream.ToArray(), r2010Plus: true, mapOffsets: [], diagnostics.Add);

        Assert.Single(objects);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Walk_TruncatedTailWithoutRecovery_ReportsTheStop()
    {
        var stream = new RecordBitWriter();
        stream.WriteRawByte(0xCA);
        stream.WriteRawByte(0x0D);
        stream.WriteRawByte(0);
        stream.WriteRawByte(0);
        stream.WriteRecord(r => r.WriteLineRecord());
        // A record whose declared size runs past the stream end: no map offset to recover to.
        stream.WriteRawByte(100 & 0xFF);
        stream.WriteRawByte(0);
        stream.WriteZeros(4);

        var diagnostics = new List<string>();
        var objects = ObjectStreamWalker.Walk(stream.ToArray(), r2010Plus: true, mapOffsets: [], diagnostics.Add);

        Assert.Single(objects);
        var stop = Assert.Single(diagnostics);
        Assert.Contains("stopped at", stop, StringComparison.Ordinal);
        Assert.Contains("past the stream end", stop, StringComparison.Ordinal);
    }

    [Fact]
    public void Walk_CleanStream_EndsWithoutDiagnostics()
    {
        var stream = new RecordBitWriter();
        stream.WriteRawByte(0xCA);
        stream.WriteRawByte(0x0D);
        stream.WriteRawByte(0);
        stream.WriteRawByte(0);
        stream.WriteRecord(r => r.WriteLineRecord());
        stream.WriteRecord(r => r.WriteLineRecord());

        var diagnostics = new List<string>();
        var objects = ObjectStreamWalker.Walk(stream.ToArray(), r2010Plus: true, mapOffsets: [], diagnostics.Add);

        Assert.Equal(2, objects.Count);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Parse_TruncatedBlock_ReportsAndKeepsThePreviousEntries()
    {
        // One well-formed block (handle 1 at offset 4), then a block whose declared size runs past the data.
        var writer = new RecordBitWriter();
        writer.WriteRawByte(0); writer.WriteRawByte(4);            // block: 2 delta bytes + 2 CRC bytes
        writer.WriteRawByte(1); writer.WriteRawByte(4);            // handle delta 1, offset delta 4
        writer.WriteRawByte(0); writer.WriteRawByte(0);            // CRC placeholder
        writer.WriteRawByte(0); writer.WriteRawByte(30);           // declared 30 bytes — truncated

        var diagnostics = new List<string>();
        var map = DwgHandleMap.Parse(writer.ToArray(), diagnostics.Add);

        Assert.Equal(1, map.Count);
        Assert.Equal(4u, map.Entries[0].Offset);
        Assert.Contains(diagnostics, d => d.Contains("truncated", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_NegativeOffset_IsDroppedAndReported()
    {
        // handle delta 1, offset delta 4 → entry (1, 4); then handle delta 1, offset delta -10 → dropped.
        var writer = new RecordBitWriter();
        writer.WriteRawByte(0); writer.WriteRawByte(6);            // block: 4 delta bytes + 2 CRC bytes
        writer.WriteRawByte(1); writer.WriteRawByte(4);            // handle 1, offset +4
        writer.WriteRawByte(1); writer.WriteRawByte(0x46);         // handle +1, offset delta -6 (VarInt: 0x40 | 6)
        writer.WriteRawByte(0); writer.WriteRawByte(0);            // CRC placeholder

        var diagnostics = new List<string>();
        var map = DwgHandleMap.Parse(writer.ToArray(), diagnostics.Add);

        Assert.Equal(1, map.Count);
        Assert.Equal(1u, map.Entries[0].Handle);
        Assert.Equal(4u, map.Entries[0].Offset);
        Assert.Contains(diagnostics, d => d.Contains("negative offset", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Contains("dropped", StringComparison.Ordinal));
    }
}
