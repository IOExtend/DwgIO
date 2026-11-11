using System.Buffers.Binary;

namespace DwgIO.Model;

/// <summary>
/// The 128-byte prefix all DWG files share.
/// </summary>
public sealed class DwgFileHeader
{
    /// <summary>Bytes 0x00-0x05: release identified by the version magic.</summary>
    public required DwgVersionInfo Version { get; init; }

    /// <summary>Byte 0x0B: maintenance release version.</summary>
    public required byte MaintenanceVersion { get; init; }

    /// <summary>Bytes 0x0D-0x10: file offset of the preview image.</summary>
    public required uint PreviewOffset { get; init; }

    /// <summary>Bytes 0x13-0x14: DWG codepage.</summary>
    public required ushort Codepage { get; init; }

    /// <summary>Codepage name for diagnostics (AutoCAD's own enumeration: 29 = ANSI_1251, 30 = ANSI_1252).</summary>
    public string CodepageName => Codepage switch
    {
        28 => "ANSI_1250",
        29 => "ANSI_1251",
        30 => "ANSI_1252",
        32 => "ANSI_1253",
        33 => "ANSI_1254",
        36 => "ANSI_1257",
        38 => "ANSI_932",
        39 => "ANSI_936",
        40 => "ANSI_949",
        41 => "ANSI_950",
        _ => $"codepage {Codepage}",
    };

    /// <summary>Parses the shared prefix from the start of a DWG file.</summary>
    internal static DwgFileHeader Parse(byte[] bytes)
    {
        if (bytes.Length < 0x15)
        {
            throw new DwgException($"File too short for a DWG header: {bytes.Length} bytes.");
        }

        var magic = new byte[6];
        Buffer.BlockCopy(bytes, 0, magic, 0, 6);
        return new DwgFileHeader
        {
            Version = DwgVersionInfo.FromMagic(magic),
            MaintenanceVersion = bytes[0x0B],
            PreviewOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x0D)),
            Codepage = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0x13)),
        };
    }
}
