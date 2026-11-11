using System.Buffers.Binary;
using System.Text;
using DwgIO.Internal.Cipher;
using DwgIO.Internal.Container;

namespace DwgIO.Model;

/// <summary>
/// The file header of an R2004-family drawing: the 0x6C-byte block at file offset 0x80
/// after decryption, plus the plaintext fields of the first 0x80 bytes.
/// </summary>
public sealed class DwgContainerHeader
{
    /// <summary>The 128-byte prefix shared with other DWG releases.</summary>
    public required DwgFileHeader FileHeader { get; init; }

    /// <summary>Bytes 0x18-0x1B of the plaintext header.</summary>
    public required uint SecurityFlags { get; init; }

    /// <summary>Bytes 0x20-0x23: file offset of the summary info section.</summary>
    public required uint SummaryInfoOffset { get; init; }

    /// <summary>Bytes 0x24-0x27: file offset of the VBA project (0 if absent).</summary>
    public required uint VbaProjectOffset { get; init; }

    /// <summary>Decrypted 0x00: the "AcFssFcAJMB" file-id string.</summary>
    public required string FileId { get; init; }

    /// <summary>Decrypted 0x28: number of the last page.</summary>
    public required uint LastPageNumber { get; init; }

    /// <summary>Decrypted 0x2C: file offset where the last page ends.</summary>
    public required ulong LastPageEndOffset { get; init; }

    /// <summary>Decrypted 0x34: offset of the backup copy of the header at the end of the file.</summary>
    public required ulong BackupHeaderOffset { get; init; }

    /// <summary>Decrypted 0x3C: number of gaps in the page sequence.</summary>
    public required uint GapCount { get; init; }

    /// <summary>Decrypted 0x40: number of pages.</summary>
    public required uint PageCount { get; init; }

    /// <summary>Decrypted 0x50: page number of the page map.</summary>
    public required uint PageMapPageNumber { get; init; }

    /// <summary>Decrypted 0x54: page map offset (add 0x100 for the file offset).</summary>
    public required ulong PageMapOffset { get; init; }

    /// <summary>Decrypted 0x5C: page number of the section map.</summary>
    public required uint SectionMapPageNumber { get; init; }

    /// <summary>Decrypted 0x60: length of the page table.</summary>
    public required uint PageTableLength { get; init; }

    /// <summary>Decrypted 0x64: length of the gap table.</summary>
    public required uint GapTableLength { get; init; }

    /// <summary>Decrypted 0x68: CRC-32 stored over the block with its own 4 bytes zeroed.</summary>
    public required uint StoredCrc32 { get; init; }

    /// <summary>Whether the decrypted header block matches its stored CRC-32.</summary>
    public required bool HeaderCrcValid { get; init; }

    /// <summary>Parses the first 0xEC bytes of an R2004-family DWG file.</summary>
    internal static DwgContainerHeader Parse(byte[] bytes)
    {
        if (bytes.Length < 0xEC)
        {
            throw new DwgException($"File too short for an R2004-family header: {bytes.Length} bytes.");
        }

        var fileHeader = DwgFileHeader.Parse(bytes);
        if (fileHeader.Version.Family != DwgContainerFamily.R2004Family)
        {
            throw new DwgUnsupportedVersionException(
                $"{fileHeader.Version.Release} ({fileHeader.Version.Magic}) uses a container this reader does not implement yet.");
        }

        // Decrypt the 0x6C block and read its fields.
        var block = new byte[DwgCipher.HeaderBlockLength];
        Buffer.BlockCopy(bytes, 0x80, block, 0, DwgCipher.HeaderBlockLength);
        DwgCipher.XorHeaderBlock(block);

        var fileId = Encoding.ASCII.GetString(block, 0, 11);
        if (fileId != "AcFssFcAJMB")
        {
            throw new DwgException(
                $"Header decryption failed: expected file id \"AcFssFcAJMB\", got \"{fileId}\". File may be corrupt.");
        }

        // The header CRC-32 covers the decrypted block with the 4 CRC bytes themselves zeroed.
        uint storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(0x68));
        Span<byte> crcBlock = stackalloc byte[DwgCipher.HeaderBlockLength];
        block.AsSpan(0, DwgCipher.HeaderBlockLength).CopyTo(crcBlock);
        crcBlock[0x68..0x6C].Clear();
        bool crcValid = DwgCrc.Crc32(0, crcBlock.ToArray()) == storedCrc;

        return new DwgContainerHeader
        {
            FileHeader = fileHeader,
            SecurityFlags = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x18)),
            SummaryInfoOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x20)),
            VbaProjectOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x24)),
            FileId = fileId,
            LastPageNumber = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(0x28)),
            LastPageEndOffset = BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(0x2C)),
            BackupHeaderOffset = BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(0x34)),
            GapCount = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(0x3C)),
            PageCount = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(0x40)),
            PageMapPageNumber = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(0x50)),
            PageMapOffset = BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(0x54)),
            SectionMapPageNumber = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(0x5C)),
            PageTableLength = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(0x60)),
            GapTableLength = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(0x64)),
            StoredCrc32 = storedCrc,
            HeaderCrcValid = crcValid,
        };
    }
}
