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

    /// <summary>Walks the object records sequentially and returns their inventory.</summary>
    public static IReadOnlyList<DwgObjectInfo> Walk(byte[] data, bool r2010Plus)
    {
        var objects = new List<DwgObjectInfo>();
        int pos = StartOffset(data);
        while (pos + 2 < data.Length && objects.Count < MaxRecords)
        {
            var (sizeBytes, prefixLength) = ReadSizePrefix(data, pos);
            if (sizeBytes == 0)
            {
                break; // zero-length record: trailing padding — stream end
            }

            int payloadStart = pos + prefixLength;
            int handleBitsWidth = 0;
            if (r2010Plus)
            {
                int? width = VarUIntWidth(data, payloadStart);
                if (width is null)
                {
                    break; // no terminating byte — not a record start
                }

                handleBitsWidth = width.Value;
            }

            long payloadEndLong = (long)payloadStart + sizeBytes + handleBitsWidth;
            if (payloadEndLong + 2 > data.Length)
            {
                break; // malformed tail
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
            catch (DwgException)
            {
                break; // torn record body: keep the walk deterministic by stopping
            }

            pos = payloadEnd + 2;
        }

        return objects;
    }

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
