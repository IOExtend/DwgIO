using System.Buffers.Binary;
using System.Text;
using DwgIO.Internal.Cipher;
using DwgIO.Internal.Container;
using DwgIO.Internal.Parsing;
using DwgIO.Model;
using Xunit;

namespace DwgIO.Tests;

/// <summary>Crafted inputs must fail with <see cref="DwgException"/> or be ignored, never crash or over-allocate.</summary>
public class InputHardeningTests
{
    private static readonly byte[] PreviewStart =
        [0x1F, 0x25, 0x6D, 0x07, 0xD4, 0x36, 0x28, 0x28, 0x9D, 0x57, 0xCA, 0x3F, 0x9D, 0x44, 0x10, 0x2B];

    // LZ77 stream of 12 literals followed by the terminator.
    private static readonly byte[] HelloLz77 = [0x09, .. "Hello World!"u8, 0x11];

    [Fact]
    public void Preview_RangeEndOverflowingInt_IsIgnored()
    {
        var data = new List<byte>(PreviewStart);
        data.AddRange(new byte[4]); // overall size
        data.Add(0x01); // one image
        data.Add(0x02); // bitmap code
        data.AddRange(BitConverter.GetBytes(0x7FFF_FFF0)); // start + size overflows int
        data.AddRange(BitConverter.GetBytes(0x20));

        var preview = PreviewParser.Parse([.. data]);

        Assert.Equal(DwgPreviewImageKind.None, preview.ImageKind);
        Assert.Empty(preview.ImageBytes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Lz77_DeclaredSizeOutOfRange_OnlyPresizes(int expectedSize)
    {
        Assert.Equal("Hello World!", Encoding.ASCII.GetString(Lz77Decoder.Decompress(HelloLz77, expectedSize)));
    }

    [Fact]
    public void Lz77_OverlappingCopyWhileBufferGrows_CopiesCurrentBytes()
    {
        // 4 literals, then opcode 0x20 with length 0xFF + 0x0C + 0x21 = 300 bytes at offset 1, terminator.
        byte[] input = [0x01, (byte)'A', (byte)'B', (byte)'C', (byte)'D', 0x20, 0x00, 0x0C, 0x00, 0x00, 0x11];

        var output = Lz77Decoder.Decompress(input, expectedSize: 1);

        Assert.Equal("ABCD" + new string('D', 300), Encoding.ASCII.GetString(output));
    }

    [Fact]
    public void ReadSection_ValidPage_Assembles()
    {
        var (file, pages) = BuildFile(Page(HelloLz77));
        var entry = Section(size: 12, maxPageSize: 12, (1, 0));

        var bytes = Container(pages, entry).ReadSection(file, entry);

        Assert.Equal("Hello World!", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void ReadSection_SizeBeyondPageCapacity_Throws()
    {
        var (file, pages) = BuildFile(Page(HelloLz77));
        var entry = Section(size: 0x7401, maxPageSize: 0x7400, (1, 0));

        Assert.Throws<DwgException>(() => Container(pages, entry).ReadSection(file, entry));
    }

    [Fact]
    public void ReadSection_SizeBeyondArrayLimit_Throws()
    {
        var (file, pages) = BuildFile(Page(HelloLz77));
        // 12 pages of the 256 MiB page limit cover the size, so only the array limit rejects it.
        var entry = Section(size: 3UL << 30, maxPageSize: uint.MaxValue, [.. Enumerable.Repeat<(uint, ulong)>((1, 0), 12)]);

        Assert.Throws<DwgException>(() => Container(pages, entry).ReadSection(file, entry));
    }

    [Fact]
    public void ReadSection_PageLargerThanPageSize_Throws()
    {
        var (file, pages) = BuildFile(Page(HelloLz77));
        var entry = Section(size: 8, maxPageSize: 8, (1, 0));

        Assert.Throws<DwgException>(() => Container(pages, entry).ReadSection(file, entry));
    }

    [Fact]
    public void ReadSection_PagePastSectionEnd_IsNotDecompressed()
    {
        // Page 2 holds a truncated LZ77 stream but starts at the section end.
        var (file, pages) = BuildFile(Page(HelloLz77), Page([0x01, (byte)'A']));
        var entry = Section(size: 12, maxPageSize: 12, (1, 0), (2, 12));

        var bytes = Container(pages, entry).ReadSection(file, entry);

        Assert.Equal("Hello World!", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void ReadSection_RepeatedPagesBeyondDocumentBudget_Throws()
    {
        var (file, pages) = BuildFile(Page(HelloLz77));
        var entry = Section(size: 12, maxPageSize: 12, (1, 0), (1, 0));
        // 12 for the section buffer and 12 for the first page fit; the repeated page does not.
        var container = Container(pages, entry, maxDecompressedBytes: 30);

        Assert.Throws<DwgException>(() => container.ReadSection(file, entry));
    }

    [Fact]
    public void ReadSection_SectionSizeBeyondDocumentBudget_ThrowsBeforeReadingPages()
    {
        // The page payload is invalid, so reaching it would fail differently.
        var (file, pages) = BuildFile(Page([0x01, (byte)'A']));
        var entry = Section(size: 12, maxPageSize: 12, (1, 0));
        var container = Container(pages, entry, maxDecompressedBytes: 10);

        var ex = Assert.Throws<DwgException>(() => container.ReadSection(file, entry));
        Assert.Contains("in total", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadSection_PageDataSizePastFileEnd_Throws(bool verifyChecksums)
    {
        var (file, pages) = BuildFile(Page(HelloLz77, dataSize: 0xFFFF_FF00));
        var entry = Section(size: 12, maxPageSize: 12, (1, 0));

        Assert.Throws<DwgException>(() => Container(pages, entry).ReadSection(file, entry, verifyChecksums));
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    public void SectionMap_EntryCountBeyondBuffer_Throws(int entryCount)
    {
        var data = new byte[0x14];
        BinaryPrimitives.WriteInt32LittleEndian(data, entryCount);

        Assert.Throws<DwgException>(() => SectionMap.DecodeSectionEntries(data));
    }

    [Fact]
    public void SectionMap_PageCountBeyondBuffer_Throws()
    {
        var data = new byte[0x14 + 0x60];
        BinaryPrimitives.WriteInt32LittleEndian(data, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x14 + 8), uint.MaxValue);

        Assert.Throws<DwgException>(() => SectionMap.DecodeSectionEntries(data));
    }

    [Fact]
    public void SummaryInfoDate_MillisecondsPastLastDate_IsNull()
    {
        const uint LastDay = 5_373_484; // 9999-12-31

        Assert.NotNull(SummaryInfoParser.ToDateTimeUtc(LastDay, 0));
        Assert.Null(SummaryInfoParser.ToDateTimeUtc(LastDay, uint.MaxValue));
    }

    [Fact]
    public void Open_NegativePageMapOffset_ThrowsDwgException()
    {
        var bytes = new byte[0x100];
        "AC1018"u8.CopyTo(bytes);
        var block = new byte[DwgCipher.HeaderBlockLength];
        "AcFssFcAJMB"u8.CopyTo(block);
        // Read as a signed offset, this plus the 0x100 file header is int.MinValue.
        BinaryPrimitives.WriteUInt64LittleEndian(block.AsSpan(0x54), 0xFFFF_FFFF_7FFF_FF00);
        DwgCipher.XorHeaderBlock(block);
        block.CopyTo(bytes, 0x80);

        string path = Path.Combine(Path.GetTempPath(), $"dwgio-{Guid.NewGuid():N}.dwg");
        File.WriteAllBytes(path, bytes);
        try
        {
            var ex = Assert.Throws<DwgException>(() => DwgDocument.Open(path));
            Assert.IsType<ArgumentOutOfRangeException>(ex.InnerException);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A compressed data page: masked 0x20-byte header (for the given file offset) plus payload.
    private static Func<uint, byte[]> Page(byte[] payload, uint? dataSize = null) => offset =>
    {
        var page = new byte[0x20 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(page, DataPageHeader.ExpectedPageType);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(8), dataSize ?? (uint)payload.Length);
        DwgCipher.UnmaskPageHeader(page.AsSpan(0, 0x20), offset);
        payload.CopyTo(page, 0x20);
        return page;
    };

    // Lays the pages out back to back; page numbers start at 1.
    private static (byte[] File, PageMap Pages) BuildFile(params Func<uint, byte[]>[] pages)
    {
        var file = new List<byte>();
        var entries = new List<PageMapEntry>();
        foreach (var page in pages)
        {
            var bytes = page((uint)file.Count);
            entries.Add(new PageMapEntry { Number = entries.Count + 1, Size = (uint)bytes.Length, FileOffset = (ulong)file.Count, IsGap = false });
            file.AddRange(bytes);
        }

        return ([.. file], new PageMap { Entries = entries });
    }

    private static SectionEntry Section(ulong size, uint maxPageSize, params (uint Number, ulong Start)[] pages) => new()
    {
        Name = "Test",
        Size = size,
        PageCount = (uint)pages.Length,
        MaxPageSize = maxPageSize,
        Compression = 2,
        SectionId = 1,
        Encryption = 0,
        Pages = [.. pages.Select(p => new PageRef { PageNumber = p.Number, DataSize = 0, StartOffset = p.Start })],
    };

    private static R2004Container Container(PageMap pages, SectionEntry entry, long maxDecompressedBytes = 1L << 30) => new()
    {
        Header = new DwgContainerHeader
        {
            FileHeader = new DwgFileHeader
            {
                Version = DwgVersionInfo.FromMagic("AC1018"u8.ToArray()),
                MaintenanceVersion = 0,
                PreviewOffset = 0,
                Codepage = 0,
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
        PageMap = pages,
        SectionMap = new SectionMap { Sections = [entry] },
        MaxDecompressedBytes = maxDecompressedBytes,
    };
}
