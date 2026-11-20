using DwgIO.Internal.Container;
using DwgIO.Model;
using Xunit;

namespace DwgIO.Tests;

/// <summary>A section map with an encrypted entry: the section is skipped with a diagnostic, not silently (#38).</summary>
public class EncryptedSectionTests
{
    private static DwgDocument CreateDocument(params SectionEntry[] entries) => CreateDocument(readMetadata: false, entries);

    private static DwgDocument CreateDocument(bool readMetadata, params SectionEntry[] entries)
    {
        var container = new R2004Container
        {
            Header = new DwgContainerHeader
            {
                FileHeader = new DwgFileHeader
                {
                    Version = DwgVersionInfo.FromMagic("AC1024"u8.ToArray()),
                    MaintenanceVersion = 0,
                    PreviewOffset = 0,
                    Codepage = 30,
                },
                SecurityFlags = 0,
                SummaryInfoOffset = 0,
                VbaProjectOffset = 0,
                FileId = "AcFssFcAJMB",
                LastPageNumber = 0,
                LastPageEndOffset = 0,
                BackupHeaderOffset = 0,
                GapCount = 0,
                PageCount = 0,
                PageMapPageNumber = 0,
                PageMapOffset = 0,
                SectionMapPageNumber = 0,
                PageTableLength = 0,
                GapTableLength = 0,
                StoredCrc32 = 0,
                HeaderCrcValid = true,
            },
            PageMap = new PageMap { Entries = [] },
            SectionMap = new SectionMap { Sections = entries },
            MaxDecompressedBytes = 1L << 30,
        };

        var document = new DwgDocument(
            "synthetic.dwg",
            container,
            new DwgFileHeader
            {
                Version = DwgVersionInfo.FromMagic("AC1024"u8.ToArray()),
                MaintenanceVersion = 0,
                PreviewOffset = 0,
                Codepage = 30,
            },
            new DwgReaderOptions { ReadSummaryInfo = false, ReadMetadata = readMetadata, ReadObjects = false });
        return document;
    }

    private static SectionEntry Section(string name, uint encryption) => new()
    {
        Name = name,
        Size = 16,
        PageCount = 1,
        MaxPageSize = 16,
        Compression = 2,
        SectionId = 1,
        Encryption = encryption,
        Pages = [new PageRef { PageNumber = 1, DataSize = 0, StartOffset = 0 }],
    };


    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    public void PreviewCommand_WhenSectionCannotBeRead_ReturnsParseError(uint encryption)
    {
        var document = CreateDocument(readMetadata: true, Section("AcDb:Preview", encryption));
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(4, Cli.Program.PreviewDocument(document, "unused", output, error));
        Assert.Contains("AcDb:Preview:", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void TryGetReadableSection_EncryptedEntry_IsSkippedWithADiagnostic()
    {
        var document = CreateDocument(Section("AcDb:SummaryInfo", encryption: 1));

        Assert.False(document.TryGetReadableSection("AcDb:SummaryInfo", out _));
        var diagnostic = Assert.Single(document.Diagnostics);
        Assert.Contains("AcDb:SummaryInfo", diagnostic, StringComparison.Ordinal);
        Assert.Contains("encrypted", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetReadableSection_PlainEntry_IsReadableWithoutDiagnostics()
    {
        var document = CreateDocument(Section("AcDb:SummaryInfo", encryption: 0));

        Assert.True(document.TryGetReadableSection("AcDb:SummaryInfo", out var entry));
        Assert.NotNull(entry);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void TryGetReadableSection_MissingEntry_IsSilent()
    {
        var document = CreateDocument(Section("AcDb:SummaryInfo", encryption: 0));

        Assert.False(document.TryGetReadableSection("AcDb:AppInfo", out _));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void TryGetReadableSection_EachEncryptedSection_IsReportedOnce()
    {
        var document = CreateDocument(
            Section("AcDb:SummaryInfo", encryption: 1),
            Section("AcDb:AppInfo", encryption: 1),
            Section("AcDb:Handles", encryption: 1));

        Assert.False(document.TryGetReadableSection("AcDb:SummaryInfo", out _));
        Assert.False(document.TryGetReadableSection("AcDb:AppInfo", out _));
        Assert.False(document.TryGetReadableSection("AcDb:Handles", out _));
        // Second look at the same section does not duplicate the message.
        Assert.False(document.TryGetReadableSection("AcDb:SummaryInfo", out _));

        Assert.Equal(4, document.Diagnostics.Count);
        Assert.Contains(document.Diagnostics, d => d.Contains("AcDb:Handles", StringComparison.Ordinal));
    }
}
