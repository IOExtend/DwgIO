using DwgIO.Internal.Cipher;
using Xunit;

namespace DwgIO.Tests;

public class DwgCipherTests
{
    /// <summary>
    /// Pins the last 16 bytes of the 108-byte header key, so a change to the generator is caught.
    /// </summary>
    [Fact]
    public void HeaderKey_EndsWithKnownTail()
    {
        var key = DwgCipher.HeaderKey();

        Assert.Equal(0x6C, key.Length);
        byte[] expectedTail =
            [0x32, 0x3B, 0xA1, 0x22, 0xF6, 0x22, 0x91, 0x9D, 0xE1, 0x8B, 0x1F, 0xDA, 0xB0, 0xCA, 0x99, 0x02];
        Assert.Equal(expectedTail, key[^16..]);
    }

    [Fact]
    public void HeaderKey_IsDeterministic()
    {
        Assert.Equal(DwgCipher.HeaderKey(), DwgCipher.HeaderKey());
    }

    /// <summary>The header padding is the key generator continued to 0x100 bytes.</summary>
    [Fact]
    public void HeaderPadding_ContinuesTheGenerator()
    {
        var key = DwgCipher.HeaderKey();
        var padding = DwgCipher.HeaderPadding();

        Assert.Equal(0x14, padding.Length);

        // Regenerate the first 0x100 bytes of the sequence and compare the tail.
        byte[] full = new byte[0x100];
        uint seed = 1;
        for (int i = 0; i < 0x100; i++)
        {
            seed = (seed * 0x343FD) + 0x269EC3;
            full[i] = (byte)((seed >> 0x10) & 0xFF);
        }

        Assert.Equal(full[^0x14..], padding);
        // The padding and the header block must NOT overlap: 0x6C bytes were taken for the block,
        // the last 0x14 of 0x100 for the padding.
        Assert.NotEqual(key, padding);
    }

    [Fact]
    public void XorHeaderBlock_IsSelfInverse()
    {
        var block = new byte[0x6C];
        Random.Shared.NextBytes(block);
        var original = (byte[])block.Clone();

        DwgCipher.XorHeaderBlock(block);
        Assert.NotEqual(original, block);
        DwgCipher.XorHeaderBlock(block);
        Assert.Equal(original, block);
    }

    [Theory]
    [InlineData(0x0u, 0x4164_536Bu)]
    [InlineData(0x100u, 0x4164_526Bu)]
    public void PageHeaderMask_IsXorOfConstantAndOffset(uint offset, uint expected)
    {
        Assert.Equal(expected, DwgCipher.PageHeaderMask(offset));
    }
}
