using System.Globalization;
using DwgIO.Cli;
using DwgIO.Model;
using Xunit;

namespace DwgIO.Tests;

public class CliOutputTests
{
    [Fact]
    public void Count_GroupsWithTheInvariantSeparator_UnderANonEnglishCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // Turkish uses a dot as the thousands separator and, notably, different digits rules; the output
            // must not follow it.
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            Assert.Equal("1,234,567", Program.Count(1_234_567));
            Assert.Equal("10", Program.Count(10));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Count_IsStableUnderTheDefaultCulture()
    {
        Assert.Equal("1,234,567", Program.Count(1_234_567));
    }

    [Fact]
    public void Inspect_PrintsDiagnosticsWithoutDecodedEntities()
    {
        var document = new DwgDocument(
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
            [],
            diagnostics: ["AcDb:Handles: unexpected end of stream"]);

        using var output = new StringWriter();
        int exit = Program.InspectDocument(document, output);

        Assert.Equal(0, exit);
        string text = output.ToString();
        Assert.Contains("Diagnostics: 1", text, StringComparison.Ordinal);
        Assert.Contains("AcDb:Handles: unexpected end of stream", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Entities decoded", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_PrintsTheDiagnosticTotal_Invariantly()
    {
        var document = new DwgDocument(
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
            [],
            diagnostics: Enumerable.Range(0, 1234).Select(i => $"problem {i}"));

        using var output = new StringWriter();
        Program.InspectDocument(document, output);

        Assert.Contains("Diagnostics: 1,234", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_DoesNotPrintTheDiagnosticBlockWithoutDiagnostics()
    {
        var document = new DwgDocument(
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
            []);

        using var output = new StringWriter();
        Program.InspectDocument(document, output);

        Assert.DoesNotContain("Diagnostics:", output.ToString(), StringComparison.Ordinal);
    }
}
