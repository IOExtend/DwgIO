namespace DwgIO.Tests;

/// <summary>
/// MSB-first bit writer mirroring <see cref="DwgIO.Internal.Container.BitReaderMsb"/>'s consumption, for
/// synthetic object records and handle-map blocks in the tests.
/// </summary>
internal sealed class RecordBitWriter
{
    private readonly List<byte> _bytes = [];
    private int _bitPos;

    /// <summary>Bits written so far; a partially filled trailing byte counts once.</summary>
    public int BitsUsed => _bitPos == 0 ? _bytes.Count * 8 : (_bytes.Count - 1) * 8 + _bitPos;

    public int BytesUsed => _bytes.Count;

    public byte[] ToArray() => [.. _bytes];

    public void WriteBit(bool bit) => WriteBits(bit ? 1ul : 0ul, 1);

    public void WriteTwoBits(uint value) => WriteBits(value, 2);

    public void WriteRawByte(byte value) => WriteBits(value, 8);

    public void WriteRawDouble(double value)
    {
        foreach (byte b in BitConverter.GetBytes(value))
        {
            WriteRawByte(b);
        }
    }

    public void WriteHandle(byte code, byte count)
    {
        WriteBits(code, 4);
        WriteBits(count, 4);
        for (int i = 0; i < count; i++)
        {
            WriteRawByte(0);
        }
    }

    /// <summary>Packed short tag 11 → the constant 256 (BYLAYER in the short form).</summary>
    public void WritePackedShortTag3() => WriteTwoBits(3);

    /// <summary>Packed short tag 00 followed by the two little-endian bytes of a full-width value.</summary>
    public void WritePackedShortRaw(ushort value)
    {
        WriteTwoBits(0);
        WriteRawByte((byte)(value & 0xFF));
        WriteRawByte((byte)(value >> 8));
    }

    /// <summary>Writes one object record: size prefix (15-bit words, little-endian), the bit-level payload
    /// produced by <paramref name="fill"/> and a two-byte CRC placeholder. Returns the record's start offset.</summary>
    public int WriteRecord(Action<RecordBitWriter> fill)
    {
        int recordStart = BytesUsed;
        var payload = new RecordBitWriter();
        fill(payload);
        byte[] payloadBytes = payload.ToArray();
        // The R2010+ handle-stream size VarUInt (the payload's first byte here) is not counted by the prefix.
        uint declared = (uint)(payloadBytes.Length - 1);
        WriteSizePrefix(declared);
        foreach (byte b in payloadBytes)
        {
            WriteRawByte(b);
        }

        WriteRawByte(0); // CRC placeholder, low byte
        WriteRawByte(0); // CRC placeholder, high byte
        return recordStart;
    }

    /// <summary>Writes a run of zero bytes (a gap between records).</summary>
    public void WriteZeros(int count)
    {
        for (int i = 0; i < count; i++)
        {
            WriteRawByte(0);
        }
    }

    /// <summary>The standard LINE record body used by the synthetic tests, starting with the R2010+ VarUInt
    /// handle-stream size placeholder.</summary>
    public void WriteLineRecord()
    {
        WriteRawByte(0x00);              // R2010+ record-size VarUInt placeholder
        WriteTwoBits(0);                 // object type: tag 00 → next byte
        WriteRawByte(0x13);              // LINE
        WriteHandle(code: 0, count: 0);  // handle 0
        WriteTwoBits(2);                 // extended data: packed short tag 10 → 0 (end)
        WriteBit(false);                 // no graphics preview
        WriteTwoBits(0);                 // entity mode
        WriteTwoBits(2);                 // reactors: packed int tag 10 → 0
        WriteBit(false);                 // no extension dictionary
        WriteBit(false);                 // no AcDs data (R2013+)
        WriteTwoBits(2);                 // colour: packed short tag 10 → 0 (BYBLOCK)
        WriteTwoBits(1);                 // linetype scale: packed double tag 01 → 1.0
        WriteTwoBits(0);                 // linetype flags
        WriteTwoBits(0);                 // plot-style flags
        WriteTwoBits(0);                 // material flag
        WriteRawByte(0);                 // shadow flags
        WriteBit(false);                 // full visual style (R2010+)
        WriteBit(false);                 // face visual style
        WriteBit(false);                 // edge visual style
        WriteTwoBits(2);                 // invisibility → 0
        WriteRawByte(0);                 // lineweight
        WriteBit(true);                  // z is zero
        WriteRawDouble(1.0);
        WriteTwoBits(0);                 // end x = start x
        WriteRawDouble(2.0);
        WriteTwoBits(0);                 // end y = start y
        WriteBit(true);                  // thickness → 0.0
        WriteBit(true);                  // extrusion → default
    }

    private void WriteSizePrefix(uint value)
    {
        // 15-bit little-endian words, 0x8000 marks a following word.
        while (true)
        {
            uint word = value & 0x7FFF;
            value >>= 15;
            if (value == 0)
            {
                WriteRawByte((byte)(word & 0xFF));
                WriteRawByte((byte)(word >> 8));
                return;
            }

            WriteRawByte((byte)(word & 0xFF));
            WriteRawByte((byte)((word >> 8) | 0x80));
        }
    }

    private void WriteBits(ulong value, int n)
    {
        for (int i = n - 1; i >= 0; i--)
        {
            if (_bitPos == 0)
            {
                _bytes.Add(0);
            }

            int index = _bytes.Count - 1;
            if ((value >> i & 1) != 0)
            {
                _bytes[index] |= (byte)(0x80 >> _bitPos);
            }

            _bitPos = (_bitPos + 1) & 7;
        }
    }
}
