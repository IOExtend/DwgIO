using System.Buffers.Binary;
using Xunit;

namespace DwgIO.Tests;

/// <summary>DwgDocument.Open(Stream): identical results to Open(path) on the same bytes (#37).</summary>
public class OpenStreamTests
{
    private static byte[] CraftedFile()
    {
        // The negative-page-map-offset file of InputHardeningTests: a parseable header that fails cleanly.
        var bytes = new byte[0x100];
        "AC1018"u8.CopyTo(bytes);
        var block = new byte[Internal.Cipher.DwgCipher.HeaderBlockLength];
        "AcFssFcAJMB"u8.CopyTo(block);
        BinaryPrimitives.WriteUInt64LittleEndian(block.AsSpan(0x54), 0xFFFF_FFFF_7FFF_FF00);
        Internal.Cipher.DwgCipher.XorHeaderBlock(block);
        block.CopyTo(bytes, 0x80);
        return bytes;
    }

    [Fact]
    public void Open_Stream_SameCleanFailureAsPath()
    {
        var bytes = CraftedFile();
        string path = Path.Combine(Path.GetTempPath(), $"dwgio-{Guid.NewGuid():N}.dwg");
        File.WriteAllBytes(path, bytes);
        try
        {
            var pathException = Assert.Throws<DwgException>(() => DwgDocument.Open(path));

            using var stream = new MemoryStream(bytes);
            var streamException = Assert.Throws<DwgException>(() => DwgDocument.Open(stream, "crafted.dwg"));

            Assert.Equal(pathException.Message, streamException.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_Stream_UnsupportedVersion_SameFailureAsPath()
    {
        var bytes = new byte[0x80];
        "AC1021"u8.CopyTo(bytes);
        string path = Path.Combine(Path.GetTempPath(), $"dwgio-{Guid.NewGuid():N}.dwg");
        File.WriteAllBytes(path, bytes);
        try
        {
            var pathException = Assert.Throws<DwgUnsupportedVersionException>(() => DwgDocument.Open(path));

            using var stream = new MemoryStream(bytes);
            var streamException = Assert.Throws<DwgUnsupportedVersionException>(() => DwgDocument.Open(stream));

            // The message names the source (the path or "stream"); the rest is identical.
            Assert.Equal(Core(pathException.Message), Core(streamException.Message));

        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class NonReadableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanRead => false;
    }

    [Fact]
    public void Open_Stream_NotReadable_Throws()
    {
        using var stream = new NonReadableStream([1, 2, 3]);
        Assert.Throws<ArgumentException>(() => DwgDocument.Open(stream));
    }

    [Fact]
    public void Open_Stream_Empty_ThrowsCleanly()
    {
        using var stream = new MemoryStream([]);
        Assert.Throws<DwgException>(() => DwgDocument.Open(stream));
    }

    [Fact]
    public void Open_Stream_DefaultName_IsStream()
    {
        // A header that parses far enough to name the document: the unsupported-version path reports the name.
        var bytes = new byte[0x80];
        "AC1021"u8.CopyTo(bytes);
        using var stream = new MemoryStream(bytes);

        var ex = Assert.Throws<DwgUnsupportedVersionException>(() => DwgDocument.Open(stream));

        Assert.Contains("stream", ex.Message, StringComparison.Ordinal);
    }

    private static string Core(string message) => message[(message.IndexOf(": ", StringComparison.Ordinal) + 2)..];

    [SkippableFact]
    public void Open_Stream_MatchesPath_OnEveryAvailableDrawing()
    {
        // The same-results gate of #37; runs when private drawings are available (see docs/test-fixtures.md).
        foreach (string path in TestFiles.DwgFiles())
        {
            var fromPath = DwgDocument.Open(path, new DwgReaderOptions { ReadObjects = true });
            byte[] bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes);
            var fromStream = DwgDocument.Open(stream, Path.GetFileName(path), new DwgReaderOptions { ReadObjects = true });

            Assert.Equal(fromPath.Version.Magic, fromStream.Version.Magic);
            Assert.Equal(fromPath.FileSize, fromStream.FileSize);
            Assert.Equal(fromPath.Sections.Count, fromStream.Sections.Count);
            Assert.Equal(fromPath.Objects.Count, fromStream.Objects.Count);
            Assert.Equal(fromPath.Entities.Count, fromStream.Entities.Count);
            Assert.Equal(fromPath.Diagnostics.Count, fromStream.Diagnostics.Count);
        }
    }
}
