namespace DwgIO.Internal.Container;

/// <summary>
/// Decompressor for the DWG LZ77 dialect of the R2004-family container.
/// </summary>
/// <remarks>
/// Differences from zlib: opcode <c>0x11</c> terminates the stream; literal lengths use a
/// 0x00 run-length extension; offset encoding depends on the opcode class. Every offset
/// class is 1-based: long offsets add 0x4000 and short offsets add 1 (as observed on
/// AutoCAD-written files).
/// Back-references may overlap their own output (RLE-style): copies are byte-at-a-time.
/// </remarks>
internal static class Lz77Decoder
{
    /// <summary>Decompression-bomb guards.</summary>
    public sealed class Limits
    {
        /// <summary>The guards used when the caller passes none.</summary>
        public static Limits Default { get; } = new();

        /// <summary>Hard ceiling on the decompressed output length.</summary>
        public int MaxOutputBytes { get; init; } = 256 * 1024 * 1024;

        /// <summary>Ceiling on any single back-reference copy.</summary>
        public int MaxBackrefLength { get; init; } = 1024 * 1024;
    }

    private const int MaxPresizeBytes = 1024 * 1024;

    /// <summary>
    /// Decompresses <paramref name="input"/>; <paramref name="expectedSize"/> only presizes
    /// the output buffer and <paramref name="limits"/> defaults to <see cref="Limits.Default"/>.
    /// </summary>
    public static byte[] Decompress(byte[] input, int? expectedSize = null, Limits? limits = null)
    {
        limits ??= Limits.Default;
        // The expected size comes from the file, so it only presizes within the limit and a small cap.
        var output = new MemoryStream(Math.Clamp(expectedSize ?? 4096, 0, Math.Min(limits.MaxOutputBytes, MaxPresizeBytes)));
        int position = 0;

        int ReadByte()
        {
            if (position >= input.Length)
            {
                throw new DwgException("LZ77 stream truncated.");
            }

            return input[position++];
        }

        void EnsureOutput(int additional)
        {
            if (output.Length + additional > limits.MaxOutputBytes)
            {
                throw new DwgException($"LZ77 output exceeds limit of {limits.MaxOutputBytes} bytes.");
            }
        }

        // Literal-length reader: 0x00 starts a running-total extension (each 0x00 adds
        // 0xFF, first non-zero adds itself, +3 at the end). A byte with the high nibble set is
        // an opcode, not a length — roll the cursor back and report zero literals.
        int ReadLiteralLengthOrPeekOpcode()
        {
            int b = ReadByte();
            if (b == 0x00)
            {
                long total = 0x0F;
                while (true)
                {
                    int bb = ReadByte();
                    if (bb == 0x00)
                    {
                        total += 0xFF;
                    }
                    else
                    {
                        return (int)(total + bb + 3);
                    }
                }
            }

            if ((b & 0xF0) != 0)
            {
                position--;
                return 0;
            }

            return b + 3;
        }

        void CopyLiteral(int count)
        {
            EnsureOutput(count);
            for (int i = 0; i < count; i++)
            {
                output.WriteByte((byte)ReadByte());
            }
        }

        // Extended compressed-byte count for opcode classes 0x12-0x1F and 0x21-0x3F: the
        // low bits of the opcode when non-zero, otherwise a 0x00 running total, +2 at the end.
        int ReadCompressedBytesExtended(int op1, int validBits)
        {
            long cb = op1 & validBits;
            if (cb == 0)
            {
                while (true)
                {
                    int b = ReadByte();
                    if (b == 0)
                    {
                        cb += 0xFF;
                    }
                    else
                    {
                        cb += b + validBits;
                        break;
                    }
                }
            }

            return (int)(cb + 2);
        }

        // Long compression offset: first byte non-zero → use as-is; 0x00 → running total
        // starting at 0xFF, each further 0x00 adds 0xFF, first non-zero adds itself.
        int ReadLongCompressionOffset()
        {
            int b = ReadByte();
            if (b != 0x00)
            {
                return b;
            }

            long total = 0xFF;
            while (true)
            {
                int bb = ReadByte();
                if (bb == 0x00)
                {
                    total += 0xFF;
                }
                else
                {
                    return (int)(total + bb);
                }
            }
        }

        // Two-byte offset: offset = (first >> 2) | (second << 6); litCount = first & 0x03.
        (int Offset, int LiteralCount) ReadTwoByteOffset()
        {
            int first = ReadByte();
            int second = ReadByte();
            return ((first >> 2) | (second << 6), first & 0x03);
        }

        if (input.Length == 0)
        {
            return [];
        }

        // Initial literal length — a first byte with the high nibble set means zero literals
        // and the byte is the first opcode instead.
        CopyLiteral(ReadLiteralLengthOrPeekOpcode());

        while (position < input.Length)
        {
            int op1 = ReadByte();

            int compBytes;
            int compOffset;
            int literalCount;

            switch (op1)
            {
                case <= 0x0F:
                    throw new DwgException($"LZ77 invalid opcode 0x{op1:X2} at input offset {position - 1}.");
                case 0x11:
                    return output.ToArray(); // terminator
                case 0x10:
                {
                    compBytes = ReadLongCompressionOffset() + 9;
                    (var off, literalCount) = ReadTwoByteOffset();
                    compOffset = off + 0x4000;
                    break;
                }
                case <= 0x1F:
                {
                    compBytes = ReadCompressedBytesExtended(op1, 0x07);
                    var off = (op1 & 0x08) << 11;
                    (var extra, literalCount) = ReadTwoByteOffset();
                    compOffset = off + extra + 0x4000;
                    break;
                }
                case 0x20:
                {
                    compBytes = ReadLongCompressionOffset() + 0x21;
                    (var off, literalCount) = ReadTwoByteOffset();
                    compOffset = off + 1;
                    break;
                }
                case <= 0x3F:
                {
                    compBytes = ReadCompressedBytesExtended(op1, 0x1F);
                    (var off, literalCount) = ReadTwoByteOffset();
                    compOffset = off + 1;
                    break;
                }
                default:
                {
                    // 0x40..=0xFF: compact form — 2 offset-high bits and a 0..3 literal count
                    // packed into the opcode itself.
                    compBytes = ((op1 & 0xF0) >> 4) - 1;
                    int op2 = ReadByte();
                    compOffset = ((op2 << 2) | ((op1 & 0x0C) >> 2)) + 1;
                    literalCount = op1 & 0x03;
                    break;
                }
            }

            if (literalCount == 0)
            {
                literalCount = ReadLiteralLengthOrPeekOpcode();
            }

            if (compOffset == 0)
            {
                throw new DwgException("LZ77 back-reference offset of 0.");
            }

            if (compBytes > limits.MaxBackrefLength)
            {
                throw new DwgException($"LZ77 back-reference of {compBytes} bytes exceeds limit.");
            }

            EnsureOutput(compBytes);
            int start = (int)output.Length - compOffset;
            if (start < 0)
            {
                throw new DwgException($"LZ77 back-reference offset {compOffset} points before output start.");
            }

            // Byte-at-a-time: the source may overlap the destination (RLE-style runs).
            // The buffer is fetched per byte because WriteByte may replace it when growing.
            for (int i = 0; i < compBytes; i++)
            {
                output.WriteByte(output.GetBuffer()[start + i]);
            }

            CopyLiteral(literalCount);
        }

        return output.ToArray();
    }
}
