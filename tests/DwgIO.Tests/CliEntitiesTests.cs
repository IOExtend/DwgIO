using System.Text.Json;
using DwgIO.Cli;
using DwgIO.Model;
using Xunit;

namespace DwgIO.Tests;

/// <summary>The --version/--help switches and the entities command (#18, #19).</summary>
public class CliEntitiesTests
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

    private static DwgDocument SampleDrawing() => CreateDocument(
        new DwgEntity { Handle = 0x2A, TypeCode = 0x13, TypeName = "LINE", Kind = DwgEntityKind.Line, Geometry = [0, 0, 0, 1, 0, 0], ColorIndex = 1 },
        new DwgEntity { Handle = 0x2B, TypeCode = 0x12, TypeName = "CIRCLE", Kind = DwgEntityKind.Circle, Geometry = [5, 0, 0, 2], ColorIndex = 40 },
        new DwgEntity { Handle = 0x2C, TypeCode = 0x13, TypeName = "LINE", Kind = DwgEntityKind.Line, Geometry = [0, 0, 0, 3, 0, 0] });

    [Fact]
    public void Version_PrintsThePackageVersionAndExits0()
    {
        using var output = new StringWriter();

        int exit = Program.Run(["--version"], output, new StringWriter());

        Assert.Equal(0, exit);
        Assert.Matches(@"^\d+\.\d+\.\d+", output.ToString().TrimEnd());
    }

    [Fact]
    public void ShortVersion_PrintsTheVersion()
    {
        using var output = new StringWriter();

        Assert.Equal(0, Program.Run(["-v"], output, new StringWriter()));
        Assert.Matches(@"^\d+\.\d+\.\d+", output.ToString().TrimEnd());
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_PrintsTheUsageAndExits0(string option)
    {
        using var output = new StringWriter();

        int exit = Program.Run([option], output, new StringWriter());

        Assert.Equal(0, exit);
        Assert.Contains("Usage:", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("entities", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownOption_Exits1WithTheOptionNamed()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["--bogus"], output, error);

        Assert.Equal(1, exit);
        Assert.Contains("--bogus", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void EntitiesDocument_ListsOneLinePerEntity()
    {
        var document = SampleDrawing();
        using var output = new StringWriter();

        int exit = Program.EntitiesDocument(document, json: false, filter: null, output);

        Assert.Equal(0, exit);
        string[] lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("LINE 2A:", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void EntitiesDocument_FiltersByType()
    {
        var document = SampleDrawing();
        using var output = new StringWriter();

        int exit = Program.EntitiesDocument(document, json: false, filter: ["LINE"], output);

        Assert.Equal(0, exit);
        string text = output.ToString();
        Assert.Equal(2, text.Split("LINE").Length - 1);
        Assert.DoesNotContain("CIRCLE", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EntitiesDocument_UnknownType_Exits1WithTheName()
    {
        using var error = new StringWriter();
        var types = new[] { "WEDGE" };

        // The type validation happens while parsing the option list, before the document is read.
        int exit = Program.Run(["entities", "--type", types[0]], new StringWriter(), error);

        Assert.Equal(1, exit);
        Assert.Contains("unknown type 'WEDGE'", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void EntitiesDocument_Json_WritesAnArrayOfEntities()
    {
        var document = SampleDrawing();
        using var output = new StringWriter();

        int exit = Program.EntitiesDocument(document, json: true, filter: null, output);

        Assert.Equal(0, exit);
        using var parsed = JsonDocument.Parse(output.ToString());
        Assert.Equal(3, parsed.RootElement.GetArrayLength());
        Assert.Equal("LINE", parsed.RootElement[0].GetProperty("type").GetString());
        Assert.Equal("2A", parsed.RootElement[0].GetProperty("handle").GetString());
        Assert.Equal(1, parsed.RootElement[0].GetProperty("colorIndex").GetInt32());
    }

    [Fact]
    public void Export_Dash_WritesTheSameJsonToStandardOutputAsToFile()
    {
        var document = CreateDocument(
            new DwgEntity { Handle = 0x2A, TypeCode = 0x13, TypeName = "LINE", Kind = DwgEntityKind.Line, Geometry = [0, 0, 0, 1, 0, 0] });

        using var stdout = new StringWriter();
        Assert.Equal(0, Program.ExportDocument(document, "-", stdout, new StringWriter()));

        using var fileJson = new MemoryStream();
        DwgIO.Json.DwgJsonExporter.Write(document, fileJson, DwgIO.Json.DwgJsonExportOptions.Default);
        Assert.Equal(System.Text.Encoding.UTF8.GetString(fileJson.ToArray()), stdout.ToString());
    }
}
