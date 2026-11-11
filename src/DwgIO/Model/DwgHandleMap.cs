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

    /// <summary>Parses the decompressed <c>AcDb:Handles</c> section.</summary>
    internal static DwgHandleMap Parse(byte[] data)
    {
        var entries = new List<DwgHandleMapEntry>();
        int pos = 0;
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
                catch (DwgException)
                {
                    break;
                }

                lastHandle += handleDelta;
                lastOffset += offsetDelta;
                if (entries.Count >= MaxEntries)
                {
                    throw new DwgException("Handle map exceeds the entry cap; file is malformed.");
                }

                entries.Add(new DwgHandleMapEntry(lastHandle, (ulong)lastOffset));
            }

            pos += blockSize;
        }

        return new DwgHandleMap { Entries = entries };
    }
}
