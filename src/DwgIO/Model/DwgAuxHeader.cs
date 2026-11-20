namespace DwgIO.Model;

/// <summary>
/// The auxiliary file header of the <c>AcDb:AuxHeader</c> section (R2000 and later files): a fixed layout
/// that mirrors parts of the file header and keeps the save counters and the drawing times.
/// </summary>
public sealed class DwgAuxHeader
{
    /// <summary>The three intro bytes, <c>FF 77 01</c> in the files observed.</summary>
    public IReadOnlyList<byte> Intro { get; init; } = [];

    /// <summary>DWG version code (matches <see cref="DwgFileHeader.Version"/>'s container value).</summary>
    public ushort DwgVersion { get; init; }

    /// <summary>Maintenance version.</summary>
    public ushort MaintVersion { get; init; }

    /// <summary>Number of times the drawing was saved.</summary>
    public uint Numsaves { get; init; }

    /// <summary>Creation time of the drawing.</summary>
    public DateTime? CreatedUtc { get; init; }

    /// <summary>Last update time of the drawing.</summary>
    public DateTime? ModifiedUtc { get; init; }

    /// <summary>The next free handle at the last save.</summary>
    public ulong Handseed { get; init; }

    /// <summary>Creation time as the section stores it: Julian day and milliseconds of the day.</summary>
    public (uint JulianDay, uint Milliseconds) CreatedRaw { get; init; }

    /// <summary>Last update time as the section stores it.</summary>
    public (uint JulianDay, uint Milliseconds) ModifiedRaw { get; init; }
}
