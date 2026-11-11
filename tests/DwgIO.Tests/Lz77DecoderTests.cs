using System.Text;
using DwgIO.Internal.Container;
using Xunit;

namespace DwgIO.Tests;

public class Lz77DecoderTests
{
    /// <summary>A literal-only stream: length byte 0x09 (→ 12 literals), the literals, terminator 0x11.</summary>
    [Fact]
    public void Decompress_LiteralsOnly()
    {
        byte[] input =
        [
            0x09, (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o', (byte)' ', (byte)'W', (byte)'o',
            (byte)'r', (byte)'l', (byte)'d', (byte)'!', 0x11,
        ];

        Assert.Equal("Hello World!", Encoding.ASCII.GetString(Lz77Decoder.Decompress(input)));
    }

    /// <summary>
    /// Compact back-reference (0x40-0xFF class) with overlap: copy 3 bytes from offset 1 → RLE run.
    /// Stream: literal length 0x01 (→ 4 literals "ABCD"), opcode 0x41 (3 bytes, offset 1, 1 literal),
    /// literal 'E', terminator. Expected "ABCD" + "DDD" + "E".
    /// </summary>
    [Fact]
    public void Decompress_OverlappingBackReference()
    {
        byte[] input =
        [
            0x01, (byte)'A', (byte)'B', (byte)'C', (byte)'D', 0x41, 0x00, (byte)'E', 0x11,
        ];

        Assert.Equal("ABCDDDDE", Encoding.ASCII.GetString(Lz77Decoder.Decompress(input)));
    }

    /// <summary>The 0x00 literal-length extension: 0x00, then a non-zero byte b → 0x0F + b + 3 literals.</summary>
    [Fact]
    public void Decompress_ExtendedLiteralLength()
    {
        // 0x00 + 0x14 (20) → 0x0F + 0x14 + 3 = 0x26 = 38 literals, then terminator.
        var input = new byte[1 + 1 + 38 + 1];
        input[0] = 0x00;
        input[1] = 0x14;
        for (int i = 0; i < 38; i++)
        {
            input[2 + i] = (byte)('a' + (i % 26));
        }

        input[^1] = 0x11;

        var expected = new byte[38];
        for (int i = 0; i < 38; i++)
        {
            expected[i] = (byte)('a' + (i % 26));
        }

        Assert.Equal(expected, Lz77Decoder.Decompress(input));
    }

    /// <summary>An offset larger than the produced output is rejected.</summary>
    [Fact]
    public void Decompress_BackReferenceBeforeStart_Throws()
    {
        // 4 literals, then a 0x40-class opcode referencing offset 0x1000.
        byte[] input = [0x01, (byte)'A', (byte)'B', (byte)'C', (byte)'D', 0x40, 0xC0, 0x11];

        Assert.ThrowsAny<DwgException>(() => Lz77Decoder.Decompress(input));
    }

    /// <summary>A stream without a terminator reports truncation.</summary>
    [Fact]
    public void Decompress_Truncated_Throws()
    {
        byte[] input = [0x05, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o'];

        Assert.ThrowsAny<DwgException>(() => Lz77Decoder.Decompress(input));
    }
}
