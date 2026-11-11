using DwgIO.Internal.Parsing;
using DwgIO.Model;
using Xunit;

namespace DwgIO.Tests;

public class MetadataParserTests
{
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

    [Fact]
    public void AppInfo_UnicodeLayout_ParsesVersionFromXml()
    {
        var data = new List<byte>();
        data.AddRange([0x02, 0x00, 0x00, 0x00]); // variant marker
        WriteUnicodeString(data, "AppInfoDataList");
        data.AddRange([0x02, 0x00, 0x00, 0x00]);
        data.AddRange(new byte[16]); // checksum
        WriteUnicodeString(data, "Autodesk DWG. Trusted DWG notice.");
        data.AddRange(new byte[16]); // checksum
        WriteUnicodeString(data, "<ProductInformation name=\"Writer\" build_version=\"1.2.3.4\"/>");
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

    private static class PreviewParserSentinels
    {
        public static readonly byte[] Start =
            [0x1F, 0x25, 0x6D, 0x07, 0xD4, 0x36, 0x28, 0x28, 0x9D, 0x57, 0xCA, 0x3F, 0x9D, 0x44, 0x10, 0x2B];

        public static readonly byte[] End =
            [0xE0, 0xDA, 0x92, 0xF8, 0x2B, 0xC9, 0xD7, 0xD7, 0x62, 0xA8, 0x35, 0xC0, 0x62, 0xBB, 0xEF, 0xD4];
    }
}
