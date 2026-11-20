using DwgIO.Internal.Container;

namespace DwgIO.Model;

/// <summary>One row of the handle map: absolute handle → offset into AcDb:AcDbObjects.</summary>
/// <param name="Handle">Absolute object handle.</param>
/// <param name="Offset">Byte offset of the object record in the decompressed object stream.</param>
public sealed record DwgHandleMapEntry(ulong Handle, ulong Offset);

/// <summary>
/// The handle→offset index from the <c>AcDb:Handles</c> section: a sequence of blocks,
/// each a big-endian u16 size, pairs of variable-length deltas and a 2-byte CRC,
/// ended by an empty block. Both running totals reset per block, so a block's first
/// pair is absolute.
/// </summary>
public sealed class DwgHandleMap
{
    private const int MaxEntries = 1 << 22;

    private Dictionary<ulong, DwgHandleMapEntry>? _byHandle;

    /// <summary>Map rows in file order.</summary>
    public required IReadOnlyList<DwgHandleMapEntry> Entries { get; init; }

    /// <summary>Number of rows in the map.</summary>
    public int Count => Entries.Count;

    /// <summary>Looks up the AcDbObjects offset of an object handle.</summary>
    public DwgHandleMapEntry? Find(ulong handle)
    {
        _byHandle ??= Entries.ToDictionary(e => e.Handle, e => e);
        return _byHandle.TryGetValue(handle, out var entry) ? entry : null;
    }

    /// <summary>Parses the decompressed <c>AcDb:Handles</c> section. Where the parse stops early, or an entry
    /// is dropped, a message goes to <paramref name="diagnostic"/> instead of vanishing.</summary>
    internal static DwgHandleMap Parse(byte[] data, Action<string>? diagnostic = null)
    {
        var entries = new List<DwgHandleMapEntry>();
        int pos = 0;
        int dropped = 0;
        while (pos + 2 <= data.Length)
        {
            int blockSize = (data[pos] << 8) | data[pos + 1];
            pos += 2;
            if (blockSize == 0)
            {
                break; // empty block terminates the list
            }

            if (blockSize < 2 || pos + blockSize > data.Length)
            {
                // Real writers leave a truncated final block (or padding) past the last
                // well-formed one: treat it as the end of the map, not a hard error.
                Report(diagnostic, $"handle map: block at {pos - 2} is truncated ({blockSize} declared, {data.Length - pos} present); the map ends here");
                break;
            }

            // Payload without the trailing 2-byte CRC; deltas accumulate from zero per block.
            int payloadEnd = pos + blockSize - 2;
            ulong lastHandle = 0;
            long lastOffset = 0;
            var reader = new BitReaderMsb(data[pos..payloadEnd]);
            while (reader.RemainingBits >= 8)
            {
                ulong handleDelta;
                long offsetDelta;
                try
                {
                    handleDelta = reader.ReadVarUInt();
                    offsetDelta = reader.ReadVarInt();
                }
                catch (DwgException ex)
                {
                    Report(diagnostic, $"handle map: block at {pos - 2}: unreadable delta pair after {entries.Count} entries ({ex.Message}); the rest of the block is skipped");
                    break;
                }

                lastHandle += handleDelta;
                lastOffset += offsetDelta;
                if (lastOffset < 0)
                {
                    dropped++;
                    Report(diagnostic, $"handle map: entry {lastHandle} has the negative offset {lastOffset} and is dropped");
                    continue;
                }

                if (entries.Count >= MaxEntries)
                {
                    throw new DwgException("Handle map exceeds the entry cap; file is malformed.");
                }

                entries.Add(new DwgHandleMapEntry(lastHandle, (ulong)lastOffset));
            }

            pos += blockSize;
        }

        if (dropped > 0)
        {
            Report(diagnostic, $"handle map: {dropped} of {dropped + entries.Count} entries were dropped");
        }

        return new DwgHandleMap { Entries = entries };
    }

    private static void Report(Action<string>? diagnostic, string message) => diagnostic?.Invoke(message);
}
