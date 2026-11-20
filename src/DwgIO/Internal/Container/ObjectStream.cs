using DwgIO.Model;

namespace DwgIO.Internal.Container;

/// <summary>
/// Enumerates the object records of a decompressed <c>AcDb:AcDbObjects</c> stream.
/// The stream starts with a 4-byte <c>0x0DCA</c> prefix; after it the records are
/// sequential: size prefix, handle-stream size in bits (R2010+ only, not counted by the
/// size prefix), type, handle, payload and a 16-bit CRC tile the whole stream exactly,
/// with no gaps between records. The <see cref="DwgHandleMap"/> stays useful as a
/// handle → offset lookup.
/// </summary>
internal static class ObjectStreamWalker
{
    private const int MaxRecords = 1 << 21;

    /// <summary>
    /// Walks the object records sequentially and returns their inventory. The walk assumes records follow each
    /// other without gaps; when it stops at a broken record and <paramref name="mapOffsets"/> names an offset
    /// further on, it reports the gap and resumes at the next handle-map offset, so records after the gap are
    /// still inventoried.
    /// </summary>
    /// <param name="data">The decompressed AcDb:AcDbObjects stream.</param>
    /// <param name="r2010Plus">R2010 and later carry the handle-stream size in bits.</param>
    /// <param name="mapOffsets">Handle-map offsets into the stream, ascending, or null when the map is absent.</param>
    /// <param name="diagnostic">Receives one message per early stop or gap that was skipped.</param>
    public static IReadOnlyList<DwgObjectInfo> Walk(
        byte[] data,
        bool r2010Plus,
        IReadOnlyList<ulong>? mapOffsets = null,
        Action<string>? diagnostic = null)
    {
        var objects = new List<DwgObjectInfo>();
        int pos = StartOffset(data);
        while (pos + 2 < data.Length && objects.Count < MaxRecords)
        {
            var (end, failure, nextPos) = TryReadRecord(data, pos, r2010Plus, objects);
            if (!end && failure is null)
            {
                pos = nextPos;
                continue;
            }

            // A zero run (padding, or bytes erased in the file) and a broken record both end the sequential
            // walk; a handle-map offset beyond the stop point resynchronises it.
            int stop = pos;
            string reason = end ? "zero run (padding or erased bytes)" : failure!;
            int resumed = ResumeAfterGap(data, mapOffsets, stop);
            if (resumed > stop)
            {
                Report(diagnostic, $"object stream: gap at {stop}: {reason}; skipped {resumed - stop} bytes to the next handle-map offset and resumed");
                pos = resumed;
                continue;
            }

            if (end)
            {
                break; // trailing padding: the stream ends here
            }

            Report(diagnostic, $"object stream: stopped at {stop}: {reason}");
            break;
        }

        return objects;
    }

    /// <summary>
    /// Tries to decode the record at <paramref name="pos"/>; on success appends it to <paramref name="objects"/>.
    /// Returns <c>End</c> true for a clean stream end (zero-length record) and <c>Failure</c> naming a broken
    /// record that a gap resync may recover from.
    /// </summary>
    private static (bool End, string? Failure, int NextPos) TryReadRecord(byte[] data, int pos, bool r2010Plus, List<DwgObjectInfo> objects)
    {
        int nextPos = pos;
        var (sizeBytes, prefixLength) = ReadSizePrefix(data, pos);
        if (sizeBytes == 0)
        {
            return (End: true, Failure: null, NextPos: nextPos); // zero-length record: trailing padding — stream end
        }

        int payloadStart = pos + prefixLength;
        int handleBitsWidth = 0;
        if (r2010Plus)
        {
            int? width = VarUIntWidth(data, payloadStart);
            if (width is null)
            {
                return (End: false, Failure: "no terminating byte in the handle-stream size", NextPos: nextPos);
            }

            handleBitsWidth = width.Value;
        }

        long payloadEndLong = (long)payloadStart + sizeBytes + handleBitsWidth;
        if (payloadEndLong + 2 > data.Length)
        {
            return (End: false, Failure: $"record of {sizeBytes} bytes runs past the stream end", NextPos: nextPos);
        }

        int payloadEnd = (int)payloadEndLong;
        try
        {
            // The bit-level payload starts right after the size prefix.
            int bitsStart = payloadStart;
            var reader = new BitReaderMsb(data[bitsStart..payloadEnd]);
            if (r2010Plus)
            {
                reader.ReadVarUInt(); // handle-stream size in bits
            }

            ushort typeCode = reader.ReadObjectType(r2010Plus);
            var handle = reader.ReadHandle();
            objects.Add(new DwgObjectInfo
            {
                StreamOffset = pos,
                SizeBytes = sizeBytes,
                TypeCode = typeCode,
                Handle = handle.Value,
                StreamHandle = handle,
                BitsStart = bitsStart,
                BitsLength = payloadEnd - bitsStart,
            });
        }
        catch (DwgException ex)
        {
            return (End: false, Failure: $"torn record body ({ex.Message})", NextPos: nextPos);
        }

        return (End: false, Failure: null, NextPos: payloadEnd + 2);
    }

    /// <summary>The first handle-map offset strictly after <paramref name="stop"/>, or 0 when there is none.</summary>
    private static int ResumeAfterGap(byte[] data, IReadOnlyList<ulong>? mapOffsets, int stop)
    {
        if (mapOffsets is null)
        {
            return 0;
        }

        int low = 0;
        int high = mapOffsets.Count;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (mapOffsets[mid] <= (ulong)stop)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low < mapOffsets.Count && mapOffsets[low] < (ulong)(data.Length - 2)
            ? (int)mapOffsets[low]
            : 0;
    }

    private static void Report(Action<string>? diagnostic, string message) => diagnostic?.Invoke(message);

    /// <summary>The 0x0DCA stream prefix occupies 4 bytes; otherwise the stream starts at 0.</summary>
    private static int StartOffset(byte[] data) =>
        data.Length >= 4 && data[0] == 0xCA && data[1] == 0x0D && data[2] == 0 && data[3] == 0 ? 4 : 0;

    /// <summary>
    /// Byte-aligned size prefix: 15-bit little-endian words, 0x8000 marks a following word.
    /// Returns the value and the number of bytes consumed.
    /// </summary>
    private static (uint Value, int Consumed) ReadSizePrefix(byte[] data, int pos)
    {
        uint value = 0;
        uint shift = 0;
        int i = 0;
        while (true)
        {
            if (pos + i + 1 >= data.Length)
            {
                return (0, i + 2); // truncated tail: treat as end of stream
            }

            int lo = data[pos + i];
            int hi = data[pos + i + 1];
            i += 2;
            int word = (hi << 8) | lo;
            bool more = (word & 0x8000) != 0;
            value |= (uint)(word & 0x7FFF) << (int)shift;
            shift += 15;
            if (!more || shift >= 32)
            {
                return (value, i);
            }
        }
    }

    /// <summary>Width of the byte-aligned variable-length integer at <paramref name="pos"/>, or null when unterminated.</summary>
    private static int? VarUIntWidth(byte[] data, int pos)
    {
        for (int i = 0; i < 10 && pos + i < data.Length; i++)
        {
            if ((data[pos + i] & 0x80) == 0)
            {
                return i + 1;
            }
        }

        return null;
    }
}
