using DwgIO.Cli;
using DwgIO.Model;
using Xunit;

namespace DwgIO.Tests;

/// <summary>The preview command: its own exit code for a missing thumbnail and the output extension (#14).</summary>
public class CliPreviewTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dwgio-tests-" + Guid.NewGuid().ToString("N"));

    public CliPreviewTests()
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

    private static DwgDocument CreateDocument(DwgPreview? preview)
    {
        var header = new DwgFileHeader
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
        };
        return preview is null
            ? new DwgDocument("synthetic.dwg", header, [])
            : new DwgDocument("synthetic.dwg", header, [], preview);
    }

    [Fact]
    public void PreviewDocument_WithoutThumbnail_ExitsWith5()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.PreviewDocument(CreateDocument(null), Path.Combine(_directory, "thumb"), output, error);

        Assert.Equal(5, exit);
        Assert.Contains("no thumbnail", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(_directory));
    }

    [Fact]
    public void PreviewDocument_WithoutImageBytes_ExitsWith5()
    {
        var preview = new DwgPreview { OverallSize = 0, ImageKind = DwgPreviewImageKind.None, ImageBytes = [] };
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.PreviewDocument(CreateDocument(preview), Path.Combine(_directory, "thumb"), output, error);

        Assert.Equal(5, exit);
        Assert.Empty(Directory.EnumerateFiles(_directory));
    }

    [Fact]
    public void PreviewDocument_ExtensionIsAddedToMatchTheKind()
    {
        var preview = new DwgPreview { OverallSize = 4, ImageKind = DwgPreviewImageKind.Png, ImageBytes = [1, 2, 3, 4] };
        string requested = Path.Combine(_directory, "thumb");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.PreviewDocument(CreateDocument(preview), requested, output, error);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(_directory, "thumb.png")));
        Assert.Null(Program.ResolvePreviewTarget(DwgPreviewImageKind.Png, requested).Warning);
    }

    [Fact]
    public void ResolvePreviewTarget_MismatchingExtension_IsReportedAndKept()
    {
        var (target, warning) = Program.ResolvePreviewTarget(DwgPreviewImageKind.Bmp, "drawing.png");

        Assert.Equal("drawing.png", target);
        Assert.NotNull(warning);
        Assert.Contains("Bmp", warning, StringComparison.Ordinal);
        Assert.Contains(".bmp", warning, StringComparison.Ordinal);
        Assert.Contains(".png", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvePreviewTarget_MatchingExtension_IsSilent()
    {
        var (target, warning) = Program.ResolvePreviewTarget(DwgPreviewImageKind.Png, "drawing.PNG");

        Assert.Equal("drawing.PNG", target);
        Assert.Null(warning);
    }

    [Fact]
    public void ResolvePreviewTarget_DirectoryRequest_GainsTheExtension()
    {
        var (target, warning) = Program.ResolvePreviewTarget(DwgPreviewImageKind.Wmf, "out" + Path.DirectorySeparatorChar);

        Assert.Equal("out" + Path.DirectorySeparatorChar + ".wmf", target);
        Assert.Null(warning);
    }
}
