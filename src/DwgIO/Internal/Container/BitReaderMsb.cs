using System.Buffers.Binary;
using DwgIO.Model;

namespace DwgIO.Internal.Container;

/// <summary>
/// MSB-first bit-level reader for the DWG bit stream: raw bits and bytes, the
/// tag-prefixed packed integers and doubles, variable-length integers and handles.
/// </summary>
/// <param name="bytes">Buffer to read; reading starts at bit 0 of byte 0.</param>
internal sealed class BitReaderMsb(byte[] bytes)
{
    private int _bytePos;
    private int _bitPos; // 0..7, MSB-first

    /// <summary>Bits left between the cursor and the end of the buffer.</summary>
    public int RemainingBits => (bytes.Length - _bytePos) * 8 - _bitPos;


    /// <summary>Reads <paramref name="n"/> raw bits, MSB-first.</summary>
    public ulong ReadBits(int n)
    {
        if (n > RemainingBits)
        {
            throw new DwgException($"Bit stream exhausted: wanted {n} bits, {RemainingBits} remain.");
        }

        ulong result = 0;
        int taken = 0;
        while (taken < n)
        {
            int available = 8 - _bitPos;
            int take = Math.Min(n - taken, available);
            int shiftRight = available - take;
            byte mask = take == 8 ? (byte)0xFF : (byte)((1 << take) - 1);
            ulong chunk = (ulong)((bytes[_bytePos] >> shiftRight) & mask);
            result = (result << take) | chunk;
            _bitPos += take;
            if (_bitPos == 8)
            {
                _bytePos++;
                _bitPos = 0;
            }

            taken += take;
        }

        return result;
    }

    /// <summary>Raw byte (honours bit alignment).</summary>
    public byte ReadRawByte() => (byte)ReadBits(8);

    /// <summary>Raw little-endian 32-bit integer (honours bit alignment).</summary>
    public uint ReadRawUInt32()
    {
        uint b0 = (uint)ReadBits(8);
        uint b1 = (uint)ReadBits(8);
        uint b2 = (uint)ReadBits(8);
        uint b3 = (uint)ReadBits(8);
        return (b3 << 24) | (b2 << 16) | (b1 << 8) | b0;
    }

    /// <summary>Two bits, used as the tag of the packed values.</summary>
    public uint ReadTwoBits() => (uint)ReadBits(2);

    /// <summary>One bit.</summary>
    public bool ReadBit() => ReadBits(1) != 0;

    /// <summary>
    /// Packed 32-bit integer: a 2-bit tag selects {32-bit LE, 8-bit, 0} payload.
    /// </summary>
    public uint ReadPackedInt()
    {
        uint tag = ReadTwoBits();
        return tag switch
        {
            0 => (uint)ReadBits(32),
            1 => (uint)ReadBits(8),
            _ => 0,
        };
    }

    /// <summary>
    /// Packed 16-bit integer: a 2-bit tag selects {16-bit LE, 8-bit, 0, 256} payload.
    /// </summary>
    public uint ReadPackedShort()
    {
        uint tag = ReadTwoBits();
        return tag switch
        {
            0 => (uint)ReadBits(16),
            1 => (uint)ReadBits(8),
            2 => 0,
            _ => 256,
        };
    }

    /// <summary>
    /// Packed double: a 2-bit tag selects 00 → 8 raw bytes LE, 01 → 1.0, 10 → 0.0, 11 → reserved.
    /// </summary>
    public double ReadPackedDouble()
    {
        uint tag = ReadTwoBits();
        switch (tag)
        {
            case 0:
            {
                var bs = new byte[8];
                for (int i = 0; i < 8; i++)
                {
                    bs[i] = ReadRawByte();
                }

                return BitConverter.ToDouble(bs, 0);
            }
            case 1:
                return 1.0;
            case 2:
                return 0.0;
            default:
                throw new DwgException("Packed double with the reserved tag 11 encountered.");
        }
    }

    /// <summary>
    /// Double stored relative to a default: 00 → default, 01 → patch the low 4 bytes,
    /// 10 → patch the low 6 bytes, 11 → raw 8 bytes.
    /// </summary>
    public double ReadDoubleWithDefault(double defaultValue)
    {
        uint tag = ReadTwoBits();
        switch (tag)
        {
            case 0:
                return defaultValue;
            case 1:
            {
                var bs = BitConverter.GetBytes(defaultValue);
                for (int i = 0; i < 4; i++)
                {
                    bs[i] = ReadRawByte();
                }

                return BitConverter.ToDouble(bs, 0);
            }
            case 2:
            {
                var bs = BitConverter.GetBytes(defaultValue);
                bs[4] = ReadRawByte();
                bs[5] = ReadRawByte();
                for (int i = 0; i < 4; i++)
                {
                    bs[i] = ReadRawByte();
                }

                return BitConverter.ToDouble(bs, 0);
            }
            default:
            {
                var bs = new byte[8];
                for (int i = 0; i < 8; i++)
                {
                    bs[i] = ReadRawByte();
                }

                return BitConverter.ToDouble(bs, 0);
            }
        }
    }

    /// <summary>Packed 64-bit integer: a 3-bit byte count followed by that many little-endian bytes.</summary>
    public ulong ReadPackedLong()
    {
        int byteCount = (int)ReadBits(3);
        ulong value = 0;
        for (int i = 0; i < byteCount; i++)
        {
            value |= (ulong)ReadRawByte() << (i * 8);
        }

        return value;
    }

    /// <summary>Raw little-endian IEEE-754 double (honours bit alignment).</summary>
    public double ReadRawDouble()
    {
        var bs = new byte[8];
        for (int i = 0; i < 8; i++)
        {
            bs[i] = ReadRawByte();
        }

        return BitConverter.ToDouble(bs, 0);
    }

    /// <summary>Skips forward <paramref name="count"/> bytes from the current bit position.</summary>
    public void SkipBytes(int count)
    {
        long total = _bytePos * 8L + _bitPos + count * 8L;
        if (total > bytes.Length * 8L)
        {
            throw new DwgException("Bit stream exhausted while skipping bytes.");
        }

        _bytePos = (int)(total / 8);
        _bitPos = (int)(total % 8);
    }

    /// <summary>
    /// Variable-length unsigned integer: 7-bit groups, low group first, 0x80 marks a following byte.
    /// </summary>
    public ulong ReadVarUInt()
    {
        ulong value = 0;
        uint shift = 0;
        while (true)
        {
            ulong b = ReadBits(8);
            bool more = (b & 0x80) != 0;
            value |= (b & 0x7F) << (int)shift;
            shift += 7;
            if (!more || shift >= 64)
            {
                return value;
            }
        }
    }

    /// <summary>
    /// Variable-length signed integer: 0x80 marks a following byte; the last byte
    /// contributes 6 bits and carries the sign in bit 0x40.
    /// </summary>
    public long ReadVarInt()
    {
        ulong value = 0;
        uint shift = 0;
        bool negate = false;
        while (true)
        {
            ulong b = ReadBits(8);
            bool more = (b & 0x80) != 0;
            value |= (b & (more ? 0x7FUL : 0x3FUL)) << (int)shift;
            shift += (uint)(more ? 7 : 6);
            if (!more)
            {
                negate = (b & 0x40) != 0;
                break;
            }

            if (shift >= 64)
            {
                break;
            }
        }

        if (negate)
        {
            return value == (1UL << 63) ? long.MinValue : -(long)value;
        }

        return (long)value;
    }

    /// <summary>
    /// Object type: R2010+ uses a 2-bit dispatch (00 → next byte,
    /// 01 → next byte + 0x1F0, else → next 2 bytes LE); earlier releases use a packed 16-bit integer.
    /// </summary>
    public ushort ReadObjectType(bool r2010Plus)
    {
        if (!r2010Plus)
        {
            return (ushort)ReadPackedShort();
        }

        uint tag = ReadTwoBits();
        return tag switch
        {
            0 => ReadRawByte(),
            1 => (ushort)(ReadRawByte() + 0x1F0),
            _ => BinaryPrimitives.ReadUInt16LittleEndian([ReadRawByte(), ReadRawByte()]),
        };
    }

    /// <summary>Handle: 4-bit code + 4-bit byte counter + counter bytes.</summary>
    public DwgHandle ReadHandle()
    {
        byte code = (byte)ReadBits(4);
        byte counter = (byte)ReadBits(4);
        if (counter > 8)
        {
            throw new DwgException($"Handle counter {counter} exceeds 8 bytes (code 0x{code:X}).");
        }

        ulong value = 0;
        for (int i = 0; i < counter; i++)
        {
            value = (value << 8) | ReadRawByte();
        }

        return new DwgHandle(code, counter, value);
    }
}
