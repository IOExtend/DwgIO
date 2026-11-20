using DwgIO.Model;

namespace DwgIO.Internal.Parsing;

/// <summary>
/// Parses the decompressed <c>AcDb:RevHistory</c> payload: two version words, a count and that many
/// 32-bit values, one per recorded save (per libredwg's revhistory.spec).
/// </summary>
internal static class RevHistoryParser
{
    /// <summary>Maximum number of history entries accepted before the payload is considered malformed.</summary>
    private const int MaxHistories = 1 << 20;

    public static DwgRevHistory Parse(byte[] data)
    {
        var cursor = new MetadataByteCursor(data, 0);
        uint version = cursor.ReadU32();
        uint minor = cursor.ReadU32();
        uint count = cursor.ReadU32();
        if (count > MaxHistories)
        {
            throw new DwgException($"RevHistory declares {count} entries; the sanity cap is {MaxHistories}.");
        }

        var histories = new uint[count];
        for (uint i = 0; i < count; i++)
        {
            histories[i] = cursor.ReadU32();
        }

        return new DwgRevHistory
        {
            ClassVersion = version,
            ClassMinor = minor,
            Histories = histories,
        };
    }
}
