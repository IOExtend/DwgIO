using System.Buffers.Binary;
using DwgIO.Model;

namespace DwgIO.Internal.Parsing;

/// <summary>
/// Parses the decompressed <c>AcDb:AuxHeader</c> payload: a fixed 123-byte little-endian layout (plus up
/// to three trailing words in R2018 files), per libredwg's auxheader.spec. AutoCAD's R2018 variant is
/// normalised to the ODA layout before the fields are read.
/// </summary>
internal static class AuxHeaderParser
{
    // Offsets into the normalised ODA layout.
    private const int MinSize = 123;

    public static DwgAuxHeader Parse(byte[] data)
    {
        if (data.Length < MinSize)
        {
            throw new DwgException($"AuxHeader is {data.Length} bytes; the fixed layout needs {MinSize}.");
        }

        var bytes = (byte[])data.Clone();
        NormaliseR2018(bytes, data.Length);
        var created = ReadTime(bytes, 63);
        var modified = ReadTime(bytes, 71);
        return new DwgAuxHeader
        {
            Intro = [bytes[0], bytes[1], bytes[2]],
            DwgVersion = ReadU16(bytes, 3),
            MaintVersion = ReadU16(bytes, 5),
            Numsaves = ReadU32(bytes, 7),
            CreatedRaw = created,
            ModifiedRaw = modified,
            Handseed = ReadU64(bytes, 79),
            CreatedUtc = ToUtc(created),
            ModifiedUtc = ToUtc(modified),
        };
    }

    /// <summary>AutoCAD's R2018 auxheader writes the maint_version as 32 bits and two extra words in the
    /// version area; when the -1 marker sits at [13..17) instead of [11..15), compact it in place.</summary>
    private static void NormaliseR2018(byte[] bytes, int length)
    {
        if (length < 129)
        {
            return;
        }

        uint minusOneOda = ReadU32(bytes, 11);
        uint minusOneAcad = ReadU32(bytes, 13);
        if (minusOneAcad == 0xFFFFFFFF && minusOneOda != 0xFFFFFFFF)
        {
            // Compact out the RL-sized maint_version word and the two extra RS fields.
            Buffer.BlockCopy(bytes, 9, bytes, 7, 18);  // through dwg_version_1
            Buffer.BlockCopy(bytes, 27, bytes, 25, 2); // maint_version_1
            Buffer.BlockCopy(bytes, 31, bytes, 27, 4); // dwg/maint_version_2
            Buffer.BlockCopy(bytes, 37, bytes, 31, 92); // unknown words through zero_8
            Array.Clear(bytes, 123, 6);
        }
    }

    private static ushort ReadU16(byte[] b, int pos) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(pos));

    private static uint ReadU32(byte[] b, int pos) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos));

    private static ulong ReadU64(byte[] b, int pos) => BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(pos));

    private static (uint, uint) ReadTime(byte[] b, int pos) => (ReadU32(b, pos), ReadU32(b, pos + 4));

    private static DateTime? ToUtc((uint JulianDay, uint Milliseconds) time) =>
        SummaryInfoParser.ToDateTimeUtc(time.JulianDay, time.Milliseconds) is long ticks
            ? new DateTime(ticks, DateTimeKind.Utc)
            : null;
}
