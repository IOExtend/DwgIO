using System.Text;
using System.Xml;
using DwgIO.Model;
using DwgIO.Scene;
using Xunit;

namespace DwgIO.Tests;

public class DwgSvgExporterTests
{
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

    private static DwgEntity Entity(ulong handle, DwgEntityKind kind, string typeName, double[] geometry, int colorIndex = 256) =>
        new()
        {
            Handle = handle,
            TypeCode = 0x13,
            TypeName = typeName,
            Kind = kind,
            Geometry = geometry,
            ColorIndex = colorIndex,
        };

    private static string WriteToString(DwgDocument document, DwgSvgOptions? options = null)
    {
        using var stream = new MemoryStream();
        DwgSvgExporter.Write(document, stream, options);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string SingleLineSvg() => WriteToString(CreateDocument(
        Entity(0x2A, DwgEntityKind.Line, "LINE", [10.0, 20.0, 0.0, 30.0, 20.0, 0.0], colorIndex: 1)));

    [Fact]
    public void Write_DrawsLineWithFlippedYAndAciColour()
    {
        string svg = SingleLineSvg();

        // The Y flip puts the line at y = -20, and ACI 1 is pure red.
        Assert.Contains("<line x1=\"10\" y1=\"-20\" x2=\"30\" y2=\"-20\" stroke=\"#FF0000\" />", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ViewBoxContainsEveryEntity()
    {
        var document = CreateDocument(
            Entity(0x2A, DwgEntityKind.Line, "LINE", [10.0, 20.0, 0.0, 30.0, 20.0, 0.0]),
            Entity(0x2B, DwgEntityKind.Circle, "CIRCLE", [-50.0, 0.0, 0.0, 5.0]),
            Entity(0x2C, DwgEntityKind.Arc, "ARC", [0.0, 0.0, 0.0, 40.0, 0.1, 1.7]),
            Entity(0x2D, DwgEntityKind.Point, "POINT", [100.0, -60.0, 0.0]));

        string svg = WriteToString(document);

        string viewBox = Between(svg, "viewBox=\"", "\"");
        string[] parts = viewBox.Split(' ');
        double minX = Parse(parts[0]);
        double minY = Parse(parts[1]);
        double width = Parse(parts[2]);
        double height = Parse(parts[3]);
        // Every coordinate the entities occupy after the y flip: the circle's left edge (-55), the arc's top
        // extreme at angle 90° (y -40), the point at x 100 (flipped to y 60).
        Assert.True(minX <= -55);
        Assert.True(minY <= -40);
        Assert.True(minX + width >= 100);
        Assert.True(minY + height >= 60);
    }

    [Fact]
    public void Write_ArcPathIsWellFormedAndContinuous()
    {
        // An arc through the top cardinal: from 10° to 170° around (0,0) with r 40 passes (0, 40).
        var document = CreateDocument(Entity(0x2A, DwgEntityKind.Arc, "ARC", [0.0, 0.0, 0.0, 40.0, 10.0 * Math.PI / 180.0, 170.0 * Math.PI / 180.0]));

        string svg = WriteToString(document);

        string path = Between(svg, "<path d=\"", "\"");
        string[] tokens = path.Split(' ');
        // M x y A rx ry 0 large-arc sweep x y; a 160° span is below half a circle and the y flip makes the
        // drawing's CCW arc trace with sweep 0.
        Assert.Equal("M", tokens[0]);
        Assert.Equal("A", tokens[3]);
        Assert.Equal("40", tokens[4]);
        Assert.Equal("40", tokens[5]);
        Assert.Equal("0", tokens[7]);
        Assert.Equal("0", tokens[8]);
        Assert.StartsWith("-39.39", tokens[9], StringComparison.Ordinal);
        Assert.StartsWith("-6.94", tokens[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Write_FullTurnArcUsesACircle()
    {
        var document = CreateDocument(Entity(1, DwgEntityKind.Arc, "ARC", [0, 0, 0, 5, 0, 0]));

        string svg = WriteToString(document);

        Assert.Single(System.Xml.Linq.XDocument.Parse(svg).Descendants(XName("circle")));
    }

    [Fact]
    public void Write_IgnoresGeometryOutsideModelSpace()
    {
        var paperLine = new DwgEntity
        {
            Handle = 1, TypeCode = 0x13, TypeName = "LINE", Kind = DwgEntityKind.Line,
            Geometry = [0, 0, 0, 5, 0, 0], IsModelSpace = false,
        };

        Assert.DoesNotContain("<line", WriteToString(CreateDocument(paperLine)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Write_RejectsNonFiniteScale(double scale)
    {
        Assert.Throws<ArgumentException>(() => WriteToString(CreateDocument(), new DwgSvgOptions { Scale = scale }));
    }

    [Fact]
    public void Write_EmptyDrawing_ProducesValidSvg()
    {
        string svg = WriteToString(CreateDocument());

        Assert.Contains("<svg", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<line", svg, StringComparison.Ordinal);
        var document = new XmlDocument();
        document.LoadXml(svg);
    }

    [Fact]
    public void Write_NonFiniteGeometry_SkipsAndReports()
    {
        var document = CreateDocument(
            Entity(0x2A, DwgEntityKind.Line, "LINE", [0.0, 0.0, 0.0, double.NaN, 0.0, 0.0]),
            Entity(0x2B, DwgEntityKind.Line, "LINE", [0.0, 0.0, 0.0, 5.0, 0.0, 0.0]));

        var diagnostics = new List<string>();
        string svg = WriteToString(document, new DwgSvgOptions { ReportDiagnostic = diagnostics.Add });

        Assert.Single(System.Xml.Linq.XDocument.Parse(svg).Descendants(XName("line")));
        Assert.Empty(document.Diagnostics);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("entity 2A", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_Color7AndByLayerRenderBlackOnLightBackground()
    {
        var document = CreateDocument(
            Entity(0x2A, DwgEntityKind.Line, "LINE", [0, 0, 0, 1, 0, 0], colorIndex: 7),
            Entity(0x2B, DwgEntityKind.Line, "LINE", [0, 0, 0, 2, 0, 0], colorIndex: 256),
            Entity(0x2C, DwgEntityKind.Line, "LINE", [0, 0, 0, 3, 0, 0], colorIndex: 0),
            Entity(0x2D, DwgEntityKind.Line, "LINE", [0, 0, 0, 4, 0, 0], colorIndex: 250));

        string svg = WriteToString(document);

        Assert.Contains("stroke=\"#000000\" />", svg, StringComparison.Ordinal);
        Assert.Contains("stroke=\"#545454\" />", svg, StringComparison.Ordinal);
        Assert.Equal(3, svg.Split("stroke=\"#000000\" />").Length - 1);
    }

    [Fact]
    public void Write_ScaleMultipliesWidthAndHeight()
    {
        string svg = WriteToString(CreateDocument(Entity(0x2A, DwgEntityKind.Line, "LINE", [0.0, 0.0, 0.0, 10.0, 0.0, 0.0])), new DwgSvgOptions { Scale = 2.5 });

        // 10 units × 2.5 px/unit = 25 px, plus the 8 px margin on every side.
        Assert.Equal("41", Between(svg, "width=\"", "\""));
    }

    [Fact]
    public void WriteFile_IsAtomicAndLeavesNoTemporaryFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "dwgio-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "drawing.svg");
            DwgSvgExporter.WriteFile(SingleLineSvgDocument(), path, null);

            Assert.Contains("<svg", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(directory, ".dwgio-*"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static DwgDocument SingleLineSvgDocument() => CreateDocument(
        Entity(0x2A, DwgEntityKind.Line, "LINE", [0.0, 0.0, 0.0, 10.0, 0.0, 0.0]));

    private static System.Xml.Linq.XName XName(string local) => System.Xml.Linq.XName.Get(local, "http://www.w3.org/2000/svg");

    private static string Between(string text, string start, string end)
    {
        int from = text.IndexOf(start, StringComparison.Ordinal) + start.Length;
        int to = text.IndexOf(end, from, StringComparison.Ordinal);
        return text[from..to];
    }

    private static double Parse(string value) =>
        double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
