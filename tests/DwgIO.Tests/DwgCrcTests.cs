using DwgIO.Internal.Container;
using Xunit;

namespace DwgIO.Tests;

public class DwgCrcTests
{
    /// <summary>Standard IEEE CRC-32 check value: seed 0 over "123456789" → 0xCBF43926.</summary>
    [Fact]
    public void Crc32_MatchesIeeeCheckValue()
    {
        Assert.Equal(0xCBF43926u, DwgCrc.Crc32(0, "123456789"u8.ToArray()));
    }

    /// <summary>The first entries of the 16-bit CRC table match the CRC-16/ARC polynomial 0xA001.</summary>
    [Fact]
    public void Crc16Table_HasExpectedEntries()
    {
        Assert.Equal(0x0000u, DwgCrc.Crc16Table[0]);
        Assert.Equal(0xC0C1u, DwgCrc.Crc16Table[1]);
        Assert.Equal(0xC181u, DwgCrc.Crc16Table[2]);
        Assert.Equal(0x0140u, DwgCrc.Crc16Table[3]);
        Assert.Equal(256, DwgCrc.Crc16Table.Length);
    }

    /// <summary>
    /// The page checksum is Adler-style but starts sums from the seed without the
    /// canonical initial +1: for "Wikipedia" sum1 = 919, sum2 = 4573 → 0x11DD0397.
    /// </summary>
    [Fact]
    public void PageChecksum_StartsFromSeedWithoutPlusOne()
    {
        Assert.Equal(0x11DD0397u, DwgCrc.PageChecksum(0, "Wikipedia"u8.ToArray()));
    }

    [Fact]
    public void Crc32_SeedChaining_MatchesOneShot()
    {
        var data = "Hello World, chaining test data"u8.ToArray();
        var oneShot = DwgCrc.Crc32(0, data);
        var split = DwgCrc.Crc32(DwgCrc.Crc32(0, data[..10]), data[10..]);
        Assert.Equal(oneShot, split);
    }
}
