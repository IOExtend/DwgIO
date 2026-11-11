using System.Buffers.Binary;

namespace DwgIO.Internal.Cipher;

/// <summary>
/// XOR ciphers of the R2004-family container (file header and data page headers).
/// </summary>
internal static class DwgCipher
{
    /// <summary>Length of the XOR'd header block at file offset 0x80.</summary>
    public const int HeaderBlockLength = 0x6C;

    /// <summary>
    /// Generates the 108-byte XOR key sequence for the file header:
    /// a Microsoft-flavor LCG seeded with 1, emitting the high byte of bits 16..23 —
    /// the output bytes of the Visual C++ <c>rand()</c> implementation.
    /// </summary>
    public static byte[] HeaderKey()
    {
        var output = new byte[HeaderBlockLength];
        uint seed = 1;
        for (int i = 0; i < HeaderBlockLength; i++)
        {
            seed = (seed * 0x343FD) + 0x269EC3;
            output[i] = (byte)((seed >> 0x10) & 0xFF);
        }

        return output;
    }

    /// <summary>
    /// The 0x14 bytes that follow the encrypted block at 0xEC: the same generator
    /// run on to 0x100 bytes, keeping the last 0x14.
    /// </summary>
    public static byte[] HeaderPadding()
    {
        const int total = 0x100;
        const int tail = 0x14;
        var output = new byte[tail];
        uint seed = 1;
        for (int i = 0; i < total; i++)
        {
            seed = (seed * 0x343FD) + 0x269EC3;
            if (i >= total - tail)
            {
                output[i - (total - tail)] = (byte)((seed >> 0x10) & 0xFF);
            }
        }

        return output;
    }

    /// <summary>XOR-encrypts or decrypts (same operation) a block against the header key.</summary>
    public static void XorHeaderBlock(byte[] block)
    {
        if (block.Length != HeaderBlockLength)
        {
            throw new ArgumentException($"Header block must be {HeaderBlockLength} bytes.", nameof(block));
        }

        var key = HeaderKey();
        for (int i = 0; i < HeaderBlockLength; i++)
        {
            block[i] ^= key[i];
        }
    }

    /// <summary>
    /// The R2018+ data-page-header mask: <c>0x4164536B XOR offset</c>,
    /// where <c>offset</c> is the file position of the 32-byte page header.
    /// </summary>
    public static uint PageHeaderMask(uint offset) => 0x4164_536B ^ offset;

    /// <summary>Decrypts the 8 masked words of a data page header in place.</summary>
    public static void UnmaskPageHeader(Span<byte> header, uint fileOffset)
    {
        uint mask = PageHeaderMask(fileOffset);
        for (int i = 0; i + 4 <= header.Length; i += 4)
        {
            uint word = BinaryPrimitives.ReadUInt32LittleEndian(header[i..]);
            BinaryPrimitives.WriteUInt32LittleEndian(header[i..], word ^ mask);
        }
    }
}
