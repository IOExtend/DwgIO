using System.Text.Json;
using DwgIO.Model;
using DwgIO.Scene;
using Xunit;

namespace DwgIO.Tests;

public class DwgViewerExportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dwgio-tests-" + Guid.NewGuid().ToString("N"));

    public DwgViewerExportTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static DwgDocument CreateDocument(params DwgEntity[] entities) =>
        new(
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
            entities);

    private static DwgDocument SampleDrawing() => CreateDocument(
        new DwgEntity
        {
            Handle = 0x2A,
            TypeCode = 0x13,
            TypeName = "LINE",
            Kind = DwgEntityKind.Line,
            Geometry = [0.0, 0.0, 0.0, 10.0, 0.0, 0.0],
            ColorIndex = 1,
        },
        new DwgEntity
        {
            Handle = 0x2B,
            TypeCode = 0x12,
            TypeName = "CIRCLE",
            Kind = DwgEntityKind.Circle,
            Geometry = [5.0, 0.0, 0.0, 5.0],
            ColorIndex = 40,
        },
        new DwgEntity
        {
            Handle = 0x2C,
            TypeCode = 0x1B,
            TypeName = "POINT",
            Kind = DwgEntityKind.Point,
            Geometry = [2.0, 3.0, 0.0],
        });

    [Fact]
    public void SaveViewer_WritesSelfContainedHtml()
    {
        string htmlPath = Environment.GetEnvironmentVariable("DWGIO_VIEWER_DEBUG_PATH") is { Length: > 0 } debug
            ? debug
            : Path.Combine(_directory, "drawing.html");

        var result = DwgViewerExport.SaveViewer(SampleDrawing(), htmlPath);

        Assert.Equal(3, result.ElementCount);
        Assert.Contains(result.Document.Geometries, geometry => geometry.Topology == IOScene.SceneTopology.Lines);
        Assert.Contains(result.Document.Geometries, geometry => geometry.Topology == IOScene.SceneTopology.Points);
        string html = File.ReadAllText(htmlPath);
        // A standalone viewer: the WebGL runtime is embedded, no external script or stylesheet is referenced.
        Assert.Contains("<!DOCTYPE html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("canvas", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script src=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link rel=\"stylesheet\" href=\"http", html, StringComparison.OrdinalIgnoreCase);
        // The scene payload is embedded in script elements of the viewer runtime; no external file is needed.
        Assert.Contains("id=\"ioviewer-meta\"", html, StringComparison.Ordinal);
        int metaStart = html.IndexOf('>', html.IndexOf("id=\"ioviewer-meta\"", StringComparison.Ordinal)) + 1;
        int metaEnd = html.IndexOf("</script>", metaStart, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(html[metaStart..metaEnd]);
        Assert.Equal(3, metadata.RootElement.GetProperty("geometries").GetArrayLength());
        Assert.Contains("id=\"ioviewer-bin\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveViewer_TitleFollowsTheScene()
    {
        string htmlPath = Path.Combine(_directory, "drawing.html");

        DwgViewerExport.SaveViewer(SampleDrawing(), htmlPath);

        string html = File.ReadAllText(htmlPath);
        Assert.Contains("drawing.html", html, StringComparison.Ordinal);
    }
}
