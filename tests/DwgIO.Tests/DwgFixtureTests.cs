using DwgIO.Model;
using Xunit;

namespace DwgIO.Tests;

/// <summary>
/// Smoke tests against private DWG files located by <see cref="TestFiles"/> (<c>DWGIO_TEST_FILES</c> or a
/// <c>test_files</c> folder; no fixtures are committed to the repository). Each test checks every file and reports
/// all failing files at once; with no file available it is reported as skipped with the reason.
/// </summary>
public class DwgFixtureTests
{
    /// <summary>Runs <paramref name="check"/> on every fixture and fails with one line per failing file (name only,
    /// never the full path). The check returns false when a file legitimately lacks what it needs (an optional
    /// section); when no file had it, the test is skipped rather than passed.</summary>
    private static void CheckAll(Func<string, bool> check, string requirement)
    {
        var failures = new List<string>();
        int checkedFiles = 0;
        foreach (string path in TestFiles.DwgFiles())
        {
            try
            {
                if (check(path)) checkedFiles++;
            }
            catch (Exception ex) when (ex is not SkipException)
            {
                failures.Add($"{Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        if (checkedFiles == 0) throw new SkipException($"No private fixture has {requirement}.");
    }

    private static void CheckAll(Action<string> check) =>
        CheckAll(path => { check(path); return true; }, "a readable container");

    /// <summary>True when the section exists and its payload is not encrypted, i.e. the reader is expected to parse it.</summary>
    private static bool HasReadableSection(DwgDocument doc, string name) =>
        doc.Sections.Any(s => s.Name == name && s.Encryption != 1);

    private static string Diagnostics(DwgDocument doc) => string.Join(" | ", doc.Diagnostics);

    [SkippableFact]
    public void Open_ParsesSectionMap() => CheckAll(path =>
    {
        var doc = DwgDocument.Open(path);

        Assert.True(doc.FileSize > 0);
        Assert.NotEqual(DwgContainerFamily.R13R15, doc.Version.Family);
        Assert.NotEmpty(doc.Sections);
        Assert.NotNull(doc.ContainerHeader);
        Assert.Equal("AcFssFcAJMB", doc.ContainerHeader!.FileId);
        Assert.True(doc.PageCount > 0);
    });

    [SkippableFact]
    public void ReadSection_AcDbHeader_Decodes() => CheckAll(path =>
    {
        var doc = DwgDocument.Open(path);

        var header = doc.ReadSection("AcDb:Header");
        var expectedSize = doc.Sections.Single(s => s.Name == "AcDb:Header").Size;
        Assert.Equal(expectedSize, header.LongLength);
        Assert.True(header.Length > 0);
    });

    [SkippableFact]
    public void CrcVerification_PassesOnRealFiles() => CheckAll(path =>
    {
        var doc = DwgDocument.Open(path, new DwgReaderOptions { VerifyChecksums = true });

        Assert.True(doc.ContainerHeader!.HeaderCrcValid, "header CRC mismatch");
        doc.ReadSection("AcDb:Header");
        doc.ReadSection("AcDb:Classes");
    });

    [SkippableFact]
    public void SummaryInfo_ParsesWhenPresent() => CheckAll(path =>
    {
        var doc = DwgDocument.Open(path);
        if (!HasReadableSection(doc, "AcDb:SummaryInfo"))
        {
            return false;
        }

        Assert.True(doc.SummaryInfo is not null, $"AcDb:SummaryInfo present but not parsed; diagnostics: {Diagnostics(doc)}");
        Assert.True(doc.SummaryInfo.CreatedUtc is null || doc.SummaryInfo.CreatedUtc.Value.Year is >= 1990 and <= 2100,
            $"implausible create date {doc.SummaryInfo.CreatedUtc}");
        Assert.True(doc.SummaryInfo.ModifiedUtc is null || doc.SummaryInfo.ModifiedUtc.Value.Year is >= 1990 and <= 2100,
            $"implausible modified date {doc.SummaryInfo.ModifiedUtc}");
        return true;
    }, "a readable AcDb:SummaryInfo section");

    [SkippableFact]
    public void AppInfo_IdentifiesProducer() => CheckAll(path =>
    {
        var doc = DwgDocument.Open(path);
        if (!HasReadableSection(doc, "AcDb:AppInfo"))
        {
            return false;
        }

        Assert.True(doc.AppInfo is not null, $"AcDb:AppInfo present but not parsed; diagnostics: {Diagnostics(doc)}");
        Assert.Equal("AppInfoDataList", doc.AppInfo.Name);
        return true;
    }, "a readable AcDb:AppInfo section");

    [SkippableFact]
    public void HandleMap_AndObjectInventory_AreConsistent() => CheckAll(path =>
    {
        var doc = DwgDocument.Open(path);
        var diagnostics = Diagnostics(doc);
        Assert.True(doc.HandleMap is not null, $"handle map null; diagnostics: {diagnostics}");
        Assert.True(doc.HandleMap.Count > 0, "empty handle map");

        // Objects sit inside the decompressed AcDbObjects section; at least 99% of their handles
        // resolve through the map (some objects are absent from the map), and the two counts
        // agree within about 1%.
        var acDbObjects = doc.Sections.Single(s => s.Name == "AcDb:AcDbObjects");
        Assert.True(doc.Objects.Count > 0, $"object inventory empty; diagnostics: {diagnostics}");
        int mapped = doc.Objects.Count(o => doc.HandleMap.Find(o.Handle) is not null);
        Assert.True(mapped >= doc.Objects.Count * 99 / 100,
            $"only {mapped}/{doc.Objects.Count} object handles resolved; diagnostics: {diagnostics}");
        Assert.True(doc.Objects.Count <= doc.HandleMap.Count * 102 / 100 + 64,
            $"inventory {doc.Objects.Count} far exceeds handle map {doc.HandleMap.Count}");
        Assert.All(doc.Objects, o =>
            Assert.True(o.StreamOffset >= 0 && o.StreamOffset < acDbObjects.Size));
    });

    [SkippableFact]
    public void Preview_HasImageWhenSectionPresent() => CheckAll(path =>
    {
        var doc = DwgDocument.Open(path);
        if (!HasReadableSection(doc, "AcDb:Preview"))
        {
            return false;
        }

        Assert.True(doc.Preview is not null, $"AcDb:Preview present but not parsed; diagnostics: {Diagnostics(doc)}");

        // AC1032 files carry PNG previews that extract cleanly; older writers may store
        // a body whose declared size overruns the section (a blank/FF thumbnail), and
        // those legitimately extract to nothing.
        if (doc.Preview.ImageKind != DwgPreviewImageKind.None)
        {
            Assert.True(doc.Preview.HasImage, $"preview kind {doc.Preview.ImageKind} but no bytes");
            Assert.True(doc.Preview.ImageBytes.Length > 8);
        }

        return true;
    }, "a readable AcDb:Preview section");
}
