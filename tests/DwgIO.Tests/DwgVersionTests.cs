using System.Text;
using DwgIO.Model;
using Xunit;

namespace DwgIO.Tests;

public class DwgVersionTests
{
    [Theory]
    [InlineData("AC1012", "R13", DwgContainerFamily.R13R15)]
    [InlineData("AC1014", "R14", DwgContainerFamily.R13R15)]
    [InlineData("AC1015", "R2000", DwgContainerFamily.R13R15)]
    [InlineData("AC1018", "R2004", DwgContainerFamily.R2004Family)]
    [InlineData("AC1021", "R2007", DwgContainerFamily.R2007)]
    [InlineData("AC1024", "R2010", DwgContainerFamily.R2004Family)]
    [InlineData("AC1027", "R2013", DwgContainerFamily.R2004Family)]
    [InlineData("AC1032", "R2018", DwgContainerFamily.R2004Family)]
    public void FromMagic_IdentifiesAllKnownVersions(string magic, string release, DwgContainerFamily family)
    {
        var version = DwgVersionInfo.FromMagic(Encoding.ASCII.GetBytes(magic));

        Assert.Equal(magic, version.Magic);
        Assert.Equal(release, version.Release);
        Assert.Equal(family, version.Family);
    }

    [Theory]
    [InlineData("AC1013")]
    [InlineData("XYZXYZ")]
    [InlineData("")]
    public void FromMagic_RejectsUnknownMagic(string magic)
    {
        Assert.Throws<DwgUnsupportedVersionException>(() => DwgVersionInfo.FromMagic(Encoding.ASCII.GetBytes(magic)));
    }
}
