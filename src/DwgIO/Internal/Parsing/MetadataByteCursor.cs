using System.Buffers.Binary;
using System.Text;

namespace DwgIO.Internal.Parsing;

/// <summary>
/// Byte-aligned cursor for the metadata sections (SummaryInfo / AppInfo / FileDepList / Preview),
/// carrying the DWG codepage for legacy 8-bit strings.
/// </summary>
internal sealed class MetadataByteCursor(byte[] bytes, int codepage)
{
    private static Encoding? _codePages;

    private int _pos;

    public int Position => _pos;

    public int Remaining => bytes.Length - _pos;

    private static Encoding GetEncoding(int codepage)
    {
        // AutoCAD's own codepage enumeration → Windows codepage numbers.
        var windowsCodepage = codepage switch
        {
            28 => 1250,
            29 => 1251,
            32 => 1253,
            33 => 1254,
            36 => 1257,
            38 => 932,
            39 => 936,
            40 => 949,
            41 => 950,
            _ => 1252, // 30 and unknown values: Western
        };

        try
        {
            return Encoding.GetEncoding(windowsCodepage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            if (_codePages is null)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            }

            _codePages = Encoding.GetEncoding(windowsCodepage);
            return _codePages;
        }
    }

    public void Skip(int count)
    {
        if (_pos + count > bytes.Length)
        {
            throw new DwgException("Metadata section truncated.");
        }

        _pos += count;
    }

    public byte ReadU8()
    {
        if (_pos + 1 > bytes.Length)
        {
            throw new DwgException("Metadata section truncated.");
        }

        return bytes[_pos++];
    }

    public ushort ReadU16()
    {
        if (_pos + 2 > bytes.Length)
        {
            throw new DwgException("Metadata section truncated.");
        }

        var value = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(_pos));
        _pos += 2;
        return value;
    }

    public uint ReadU32()
    {
        if (_pos + 4 > bytes.Length)
        {
            throw new DwgException("Metadata section truncated.");
        }

        var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(_pos));
        _pos += 4;
        return value;
    }

    public byte[] ReadBytes(int count)
    {
        if (_pos + count > bytes.Length)
        {
            throw new DwgException("Metadata section truncated.");
        }

        var slice = new byte[count];
        Buffer.BlockCopy(bytes, _pos, slice, 0, count);
        _pos += count;
        return slice;
    }

    /// <summary>8-bit string: u16 byte length including the trailing NUL, decoded with the file codepage.</summary>
    public string ReadAnsiString16()
    {
        int len = ReadU16();
        if (len == 0)
        {
            return string.Empty;
        }

        var raw = ReadBytes(len);
        int textLength = raw[^1] == 0 ? len - 1 : len;
        return GetEncoding(codepage).GetString(raw, 0, textLength);
    }

    /// <summary>Unicode string where the counted content includes the NUL (AC1021+ AppInfo layout).</summary>
    public string ReadUnicodeStringWithNul()
    {
        int charCount = ReadU16();
        if (charCount == 0)
        {
            return string.Empty;
        }

        var raw = ReadBytes(charCount * 2);
        return DecodeUtf16(raw);
    }

    /// <summary>8-bit string with a u32 byte length and no NUL (FileDepList).</summary>
    public string ReadAnsiString32()
    {
        uint len = ReadU32();
        if (len == 0 || len == 0xFFFFFFFF)
        {
            return string.Empty;
        }

        if (len > Remaining)
        {
            throw new DwgException("Metadata string overruns the section.");
        }

        var raw = ReadBytes((int)len);
        return GetEncoding(codepage).GetString(raw);
    }

    private static string DecodeUtf16(ReadOnlySpan<byte> raw)
    {
        var units = new char[raw.Length / 2];
        for (int i = 0; i < units.Length; i++)
        {
            units[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(i * 2, 2));
        }

        int end = units.Length;
        while (end > 0 && units[end - 1] == '\0')
        {
            end--;
        }

        return new string(units, 0, end);
    }
}
