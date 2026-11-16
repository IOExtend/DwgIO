using System.Buffers.Binary;
using DwgIO.Internal.Container;
using DwgIO.Internal.Parsing;
using DwgIO.Model;
using Xunit;

namespace DwgIO.Tests;

public class MetadataParserTests
{
    private const string VersionEntryId = "1BD848F3CC0A3E4DBAB1CF81F7B450B3";
    private const string CommentEntryId = "B8D0F025A1D79349B2FA9BF9286FA1FD";
    private const string ProductXmlEntryId = "E8E09651C5CEB244A8BFF6E83B859D44";

    private static void WriteUnicodeString(List<byte> list, string text)
    {
        // AC1021+ layout: u16 char count + count×2 bytes, where the counted content
        // includes the NUL.
        var units = text.Select(c => (ushort)c).Append((ushort)0).ToArray();
        list.Add((byte)(units.Length & 0xFF));
        list.Add((byte)(units.Length >> 8));
        foreach (var unit in units)
        {
            list.Add((byte)(unit & 0xFF));
            list.Add((byte)(unit >> 8));
        }
    }

    private static void WriteAnsiString32(List<byte> list, string text)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(text);
        list.Add((byte)(bytes.Length & 0xFF));
        list.Add((byte)(bytes.Length >> 8));
        list.Add((byte)(bytes.Length >> 16));
        list.Add((byte)(bytes.Length >> 24));
        list.AddRange(bytes);
    }

    private static void WriteAppInfoEntry(List<byte> data, string id, string text)
    {
        data.AddRange(Convert.FromHexString(id));
        WriteUnicodeString(data, text);
    }

    private static byte[] BuildUnicodeAppInfo(string name, params (string Id, string Text)[] entries)
    {
        var data = new List<byte>();
        data.AddRange([0x03, 0x00, 0x00, 0x00]); // variant marker
        WriteUnicodeString(data, name);
        WriteU32(data, (uint)entries.Length);
        foreach (var entry in entries)
        {
            WriteAppInfoEntry(data, entry.Id, entry.Text);
        }

        data.AddRange(new byte[8]); // trailing reserve
        return [.. data];
    }

    [Fact]
    public void AppInfo_SingleCommentEntry_Parses()
    {
        var data = BuildUnicodeAppInfo(
            "AppInfoDataList",
            (CommentEntryId, "Autodesk DWG. Trusted DWG notice."));

        var appInfo = AppInfoParser.Parse(data, 1251);

        Assert.Equal("AppInfoDataList", appInfo.Name);
        Assert.Equal("Autodesk DWG. Trusted DWG notice.", appInfo.Comment);
        Assert.Equal(string.Empty, appInfo.Version);
        Assert.Equal(string.Empty, appInfo.ProductXml);
    }

    [Fact]
    public void AppInfo_ThreeEntries_MapsEachByIdentifier()
    {
        var data = BuildUnicodeAppInfo(
            "AppInfoDataList",
            (ProductXmlEntryId, "<ProductInformation build_version=\"Q.163.0.0(x64)\"/>"),
            (CommentEntryId, "Autodesk DWG. Trusted DWG notice."),
            (VersionEntryId, "23.1.163.0.0"));

        var appInfo = AppInfoParser.Parse(data, 1251);

        Assert.Equal("AppInfoDataList", appInfo.Name);
        Assert.Equal("23.1.163.0.0", appInfo.Version);
        Assert.Equal("Autodesk DWG. Trusted DWG notice.", appInfo.Comment);
        Assert.Contains("Q.163.0.0", appInfo.ProductXml);
    }

    [Fact]
    public void AppInfo_EntryOrderDoesNotMatter()
    {
        var data = BuildUnicodeAppInfo(
            "AppInfoDataList",
            (ProductXmlEntryId, "<ProductInformation build_version=\"D.40.0.200\"/>"),
            (VersionEntryId, "18.0.40.0.200"));

        var appInfo = AppInfoParser.Parse(data, 1251);

        // The explicit version entry wins over the XML build_version attribute.
        Assert.Equal("18.0.40.0.200", appInfo.Version);
        Assert.Contains("D.40.0.200", appInfo.ProductXml);
        Assert.Equal(string.Empty, appInfo.Comment);
    }

    [Fact]
    public void AppInfo_UnknownEntry_IsSkipped()
    {
        var data = BuildUnicodeAppInfo(
            "AppInfoDataList",
            ("000102030405060708090A0B0C0D0E0F", "ignored"),
            (CommentEntryId, "notice"));

        var appInfo = AppInfoParser.Parse(data, 1251);

        Assert.Equal("notice", appInfo.Comment);
        Assert.Equal(string.Empty, appInfo.Version);
        Assert.Equal(string.Empty, appInfo.ProductXml);
    }

    [Fact]
    public void AppInfo_UnicodeLayout_ParsesVersionFromXml()
    {
        var data = new List<byte>();
        data.AddRange([0x02, 0x00, 0x00, 0x00]); // variant marker
        WriteUnicodeString(data, "AppInfoDataList");
        data.AddRange([0x02, 0x00, 0x00, 0x00]);
        WriteAppInfoEntry(data, CommentEntryId, "Autodesk DWG. Trusted DWG notice.");
        WriteAppInfoEntry(
            data,
            ProductXmlEntryId,
            "<ProductInformation name=\"Writer\" build_version=\"1.2.3.4\"/>");
        data.AddRange(new byte[8]); // trailing reserve

        var appInfo = AppInfoParser.Parse([.. data], 1251);

        Assert.Equal("AppInfoDataList", appInfo.Name);
        Assert.Equal("Autodesk DWG. Trusted DWG notice.", appInfo.Comment);
        Assert.Contains("1.2.3.4", appInfo.Version);
        Assert.Contains("ProductInformation", appInfo.ProductXml);
    }

    [Fact]
    public void FileDepList_RoundTrip()
    {
        var data = new List<byte>();
        data.AddRange([0x01, 0x00, 0x00, 0x00]); // feature count
        WriteAnsiString32(data, "Feature1");
        data.AddRange([0x01, 0x00, 0x00, 0x00]); // file count
        WriteAnsiString32(data, "C:\\refs\\logo.png");
        WriteAnsiString32(data, "C:\\found\\logo.png");
        WriteAnsiString32(data, "11112222-3333-4444-5555-666677778888");
        WriteAnsiString32(data, "99998888-7777-6666-5555-444433332222");
        data.AddRange([0x00, 0x00, 0x00, 0x00]); // feature index
        data.AddRange([0x40, 0x0E, 0x13, 0x64]); // timestamp 0x64130E40 = 1678970432
        data.AddRange([0x10, 0x00, 0x00, 0x00]); // filesize 16
        data.AddRange([0x01, 0x00]); // affects graphics
        data.AddRange([0x01, 0x00, 0x00, 0x00]); // reference count

        var deps = FileDepListParser.Parse([.. data], 1251);

        var file = Assert.Single(deps.Files);
        Assert.Equal("C:\\refs\\logo.png", file.Filename);
        Assert.Equal("C:\\found\\logo.png", file.FoundPath);
        Assert.Equal(16, file.FileSize);
        Assert.True(file.AffectsGraphics);
        Assert.Equal("Feature1", Assert.Single(deps.Features));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1678970432), FileDepListParser.AsUnixTime(file.Timestamp));
    }

    [Fact]
    public void Preview_PngExtraction()
    {
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xEE };

        var data = new List<byte>();
        data.AddRange(PreviewParserSentinels.Start);
        data.AddRange([0x50, 0x00, 0x00, 0x00]); // overall size
        data.Add(0x01); // one image present
        data.Add(0x06); // PNG code
        int start = 0x20;
        data.AddRange([(byte)start, 0x00, 0x00, 0x00]);
        data.AddRange([(byte)pngBytes.Length, 0x00, 0x00, 0x00]);
        // pad up to start
        while (data.Count < start)
        {
            data.Add(0);
        }

        data.AddRange(pngBytes);
        data.AddRange(PreviewParserSentinels.End);

        var preview = PreviewParser.Parse([.. data]);

        Assert.Equal(DwgPreviewImageKind.Png, preview.ImageKind);
        Assert.Equal(pngBytes, preview.ImageBytes);
        Assert.True(preview.HasImage);
    }

    [Fact]
    public void Preview_AbsoluteOffset_MapsThroughPageFileOffset()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xEE };
        const long pageFileOffset = 1000;
        const int position = 100;
        var data = BuildPreviewSection(
            [(0x06, (uint)(pageFileOffset + position), (uint)png.Length)],
            position,
            png);
        var layout = new[] { new SectionPageLayout(0, data.Length, pageFileOffset, true) };

        var preview = PreviewParser.Parse(data, layout);

        Assert.Equal(DwgPreviewImageKind.Png, preview.ImageKind);
        Assert.Equal(png, preview.ImageBytes);
    }

    [Theory]
    [InlineData((ushort)8, 1078)]
    [InlineData((ushort)24, 54)]
    public void Preview_BareDib_GetsBmpFileHeader(ushort bitCount, int expectedOffBits)
    {
        var dib = BuildDib(bitCount);
        const long pageFileOffset = 2048;
        const int position = 64;
        var data = BuildPreviewSection(
            [(0x02, (uint)(pageFileOffset + position), (uint)dib.Length)],
            position,
            dib);
        var layout = new[] { new SectionPageLayout(0, data.Length, pageFileOffset, true) };

        var preview = PreviewParser.Parse(data, layout);

        Assert.Equal(DwgPreviewImageKind.Bmp, preview.ImageKind);
        Assert.Equal("BM", System.Text.Encoding.ASCII.GetString(preview.ImageBytes, 0, 2));
        Assert.Equal(14 + dib.Length, preview.ImageBytes.Length);
        Assert.Equal((uint)preview.ImageBytes.Length, BinaryPrimitives.ReadUInt32LittleEndian(preview.ImageBytes.AsSpan(2)));
        Assert.Equal((uint)expectedOffBits, BinaryPrimitives.ReadUInt32LittleEndian(preview.ImageBytes.AsSpan(10)));
        Assert.Equal(dib, preview.ImageBytes[14..]);
    }

    [Fact]
    public void Preview_InvalidImageBytes_ReturnsNoImage()
    {
        var junk = Enumerable.Repeat((byte)0x11, 100).ToArray();
        const long pageFileOffset = 500;
        const int position = 40;
        var data = BuildPreviewSection(
            [(0x02, (uint)(pageFileOffset + position), (uint)junk.Length)],
            position,
            junk);
        var layout = new[] { new SectionPageLayout(0, data.Length, pageFileOffset, true) };

        var preview = PreviewParser.Parse(data, layout);

        Assert.Equal(DwgPreviewImageKind.None, preview.ImageKind);
        Assert.False(preview.HasImage);
    }

    private static byte[] BuildPreviewSection(
        (byte Code, uint Start, uint Size)[] entries,
        int imageOffset,
        byte[] image)
    {
        var data = new List<byte>();
        data.AddRange(PreviewParserSentinels.Start);
        WriteU32(data, (uint)(16 + 4 + 1 + entries.Length * 9 + imageOffset + image.Length));
        data.Add((byte)entries.Length);
        foreach (var entry in entries)
        {
            data.Add(entry.Code);
            WriteU32(data, entry.Start);
            WriteU32(data, entry.Size);
        }

        while (data.Count < imageOffset)
        {
            data.Add(0);
        }

        data.AddRange(image);
        data.AddRange(PreviewParserSentinels.End);
        return [.. data];
    }

    private static byte[] BuildDib(ushort bitCount)
    {
        const int width = 2;
        const int height = 2;
        int paletteEntries = bitCount <= 8 ? 1 << bitCount : 0;
        int stride = (width * bitCount + 31) / 32 * 4;
        int pixelBytes = stride * height;
        var dib = new byte[40 + paletteEntries * 4 + pixelBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(0), 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), bitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(20), (uint)pixelBytes);
        return dib;
    }

    private static void WriteU32(List<byte> data, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        data.AddRange(buffer.ToArray());
    }

    private static class PreviewParserSentinels
    {
        public static readonly byte[] Start =
            [0x1F, 0x25, 0x6D, 0x07, 0xD4, 0x36, 0x28, 0x28, 0x9D, 0x57, 0xCA, 0x3F, 0x9D, 0x44, 0x10, 0x2B];

        public static readonly byte[] End =
            [0xE0, 0xDA, 0x92, 0xF8, 0x2B, 0xC9, 0xD7, 0xD7, 0x62, 0xA8, 0x35, 0xC0, 0x62, 0xBB, 0xEF, 0xD4];
    }
}
