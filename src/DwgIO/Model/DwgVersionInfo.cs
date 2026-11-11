namespace DwgIO.Model;

/// <summary>Which container layout a DWG version uses.</summary>
public enum DwgContainerFamily
{
    /// <summary>R13–R2000: flat section-locator table. Not implemented yet.</summary>
    R13R15,
    /// <summary>R2004, R2010, R2013, R2018: XOR'd header, page map, section map, DWG LZ77.</summary>
    R2004Family,
    /// <summary>R2007 only: Reed–Solomon coded pages and a separate LZ variant. Not implemented yet.</summary>
    R2007,
}

/// <summary>An identified DWG release.</summary>
public sealed class DwgVersionInfo
{
    /// <summary>The 6-byte magic from the file start, e.g. "AC1032".</summary>
    public required string Magic { get; init; }

    /// <summary>Human-readable release name, e.g. "R2018".</summary>
    public required string Release { get; init; }

    /// <summary>Container layout used by this release.</summary>
    public required DwgContainerFamily Family { get; init; }

    /// <summary>The year label AutoCAD shows, e.g. 2018.</summary>
    public required int Year { get; init; }

    /// <summary>Release name and magic, e.g. "R2018 (AC1032)".</summary>
    public override string ToString() => $"{Release} ({Magic})";

    /// <summary>Identifies a DWG release from its 6-byte version magic.</summary>
    internal static DwgVersionInfo FromMagic(byte[] magic)
    {
        var text = System.Text.Encoding.ASCII.GetString(magic);
        return text switch
        {
            "AC1012" => new DwgVersionInfo { Magic = text, Release = "R13", Family = DwgContainerFamily.R13R15, Year = 1995 },
            "AC1014" => new DwgVersionInfo { Magic = text, Release = "R14", Family = DwgContainerFamily.R13R15, Year = 1997 },
            "AC1015" => new DwgVersionInfo { Magic = text, Release = "R2000", Family = DwgContainerFamily.R13R15, Year = 2000 },
            "AC1018" => new DwgVersionInfo { Magic = text, Release = "R2004", Family = DwgContainerFamily.R2004Family, Year = 2004 },
            "AC1021" => new DwgVersionInfo { Magic = text, Release = "R2007", Family = DwgContainerFamily.R2007, Year = 2007 },
            "AC1024" => new DwgVersionInfo { Magic = text, Release = "R2010", Family = DwgContainerFamily.R2004Family, Year = 2010 },
            "AC1027" => new DwgVersionInfo { Magic = text, Release = "R2013", Family = DwgContainerFamily.R2004Family, Year = 2013 },
            "AC1032" => new DwgVersionInfo { Magic = text, Release = "R2018", Family = DwgContainerFamily.R2004Family, Year = 2018 },
            _ => throw new DwgUnsupportedVersionException($"Unknown DWG version magic '{text}'."),
        };
    }
}
