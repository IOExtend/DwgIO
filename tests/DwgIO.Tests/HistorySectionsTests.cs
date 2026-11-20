using System.Buffers.Binary;
using DwgIO.Internal.Parsing;
using Xunit;

namespace DwgIO.Tests;

/// <summary>The AcDb:AppInfoHistory, AcDb:RevHistory, AcDb:AuxHeader and AcDb:Template sections (#36).</summary>
public class HistorySectionsTests
{
    // ------------------------------------------------------------------ RevHistory

    [Fact]
    public void RevHistory_ParsesTheSaveList()
    {
        var data = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 1);  // class version
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 2);  // class minor
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 2);  // count
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 5);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 7);

        var history = RevHistoryParser.Parse(data);

        Assert.Equal(1u, history.ClassVersion);
        Assert.Equal(2u, history.ClassMinor);
        Assert.Equal([5u, 7u], history.Histories);
    }

    [Fact]
    public void RevHistory_TruncatedCount_Throws()
    {
        // The count word promises 5 entries, the payload holds none.
        var data = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 5);

        Assert.Throws<DwgException>(() => RevHistoryParser.Parse(data));
    }

    // ------------------------------------------------------------------ Template

    [Fact]
    public void Template_ParsesDescriptionAndMeasurement()
    {
        var data = new List<byte>();
        WriteAnsiString16(data, "standard.dwt");
        data.AddRange([0x01, 0x00]); // metric

        var template = TemplateParser.Parse([.. data], 1251);

        Assert.Equal("standard.dwt", template.Description);
        Assert.Equal(1, template.Measurement);
        Assert.True(template.IsMetric);
    }

    [Fact]
    public void Template_TruncatedDescription_Throws()
    {
        var data = new List<byte>();
        data.AddRange([0x20, 0x00]); // 32 bytes promised
        data.AddRange([0x61, 0x62]); // "ab"

        Assert.Throws<DwgException>(() => TemplateParser.Parse([.. data], 1251));
    }

    // ------------------------------------------------------------------ AuxHeader

    [Fact]
    public void AuxHeader_ParsesTheFixedLayout()
    {
        var data = new byte[123];
        data[0] = 0xFF;
        data[1] = 0x77;
        data[2] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(3), 27);  // dwg_version
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(5), 0);   // maint_version
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(7), 3);   // numsaves
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(11), 0xFFFFFFFF); // minus_1
        // TDCREATE at 63: Julian day 2460311, noon.
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(63), 2460311);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(67), 43200000);
        // TDUPDATE at 71.
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(71), 2460311);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(75), 86400000 - 1);
        // HANDSEED at 79.
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(79), 0x2B);

        var aux = AuxHeaderParser.Parse(data);

        Assert.Equal([0xFF, 0x77, 0x01], aux.Intro);
        Assert.Equal(27, aux.DwgVersion);
        Assert.Equal(0, aux.MaintVersion);
        Assert.Equal(3u, aux.Numsaves);
        Assert.Equal(0x2Bul, aux.Handseed);
        Assert.Equal(2460311u, aux.CreatedRaw.JulianDay);
        Assert.Equal(43200000u, aux.CreatedRaw.Milliseconds);
        Assert.NotNull(aux.CreatedUtc);
        Assert.InRange(aux.CreatedUtc!.Value.Year, 2020, 2030);
        Assert.NotNull(aux.ModifiedUtc);
        Assert.InRange(aux.ModifiedUtc!.Value.Year, 2020, 2030);
    }

    [Fact]
    public void AuxHeader_R2018AutoCADLayout_IsNormalised()
    {
        // In AutoCAD's R2018 variant the -1 marker sits at [13..17) and the maint_version takes 4 bytes.
        var data = new byte[129];
        data[0] = 0xFF;
        data[1] = 0x77;
        data[2] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(3), 32);       // dwg_version
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(5), 42);       // maint_version (RL)
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(9), 9);        // numsaves
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(13), 0xFFFFFFFF); // minus_1 (Acad variant)
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(17), 7);       // numsaves_1

        var aux = AuxHeaderParser.Parse(data);

        Assert.Equal(9u, aux.Numsaves);
        Assert.Equal(32, aux.DwgVersion);
        Assert.Equal(42, aux.MaintVersion);
    }

    [Fact]
    public void AuxHeader_Truncated_Throws()
    {
        Assert.Throws<DwgException>(() => AuxHeaderParser.Parse(new byte[50]));
    }

    // ------------------------------------------------------------------ AppInfoHistory

    [Fact]
    public void AppInfoHistory_WalksTheRecordSequence()
    {
        var data = new List<byte>();
        WriteAnsiAppInfo(data, "AppInfoDataList", "1.0", "First");
        WriteAnsiAppInfo(data, "AppInfoDataList", "2.0", "Second");

        var diagnostics = new List<string>();
        var records = AppInfoHistoryParser.Parse([.. data], 30, diagnostics.Add);

        Assert.Equal(2, records.Count);
        Assert.Equal("1.0", records[0].Version);
        Assert.Equal("2.0", records[1].Version);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void AppInfoHistory_TruncatedTail_IsReported()
    {
        var data = new List<byte>();
        WriteAnsiAppInfo(data, "AppInfoDataList", "1.0", "First");
        data.AddRange([0x00, 0x00]); // two trailing bytes: too small for a record

        var diagnostics = new List<string>();
        var records = AppInfoHistoryParser.Parse([.. data], 30, diagnostics.Add);

        Assert.Single(records);
        Assert.Contains(diagnostics, d => d.Contains("too small for a record", StringComparison.Ordinal));
    }

    [Fact]
    public void AppInfoHistory_BrokenRecord_IsReported()
    {
        var data = new List<byte>();
        WriteAnsiAppInfo(data, "AppInfoDataList", "1.0", "First");
        // A record whose string runs past the payload.
        data.AddRange([0x20, 0x00, (byte)'x', (byte)'y']);

        var diagnostics = new List<string>();
        var records = AppInfoHistoryParser.Parse([.. data], 30, diagnostics.Add);

        Assert.Single(records);
        Assert.Contains(diagnostics, d => d.Contains("does not decode", StringComparison.Ordinal));
    }

    private static void WriteAnsiString16(List<byte> data, string text)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(text);
        data.AddRange([(byte)(bytes.Length + 1), 0x00]);
        data.AddRange(bytes);
        data.Add(0);
    }

    private static void WriteAnsiAppInfo(List<byte> data, string name, string version, string product)
    {
        WriteAnsiString16(data, name);
        data.AddRange([0x00, 0x00, 0x00, 0x00]); // u32 marker/unknown word
        WriteAnsiString16(data, string.Empty);
        WriteAnsiString16(data, $"<ProductInformation name=\"{product}\" build_version=\"{version}\"/>");
        WriteAnsiString16(data, version);
    }
}
