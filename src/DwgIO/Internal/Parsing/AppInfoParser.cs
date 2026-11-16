using System.Buffers.Binary;
using System.Text.RegularExpressions;
using DwgIO.Model;

namespace DwgIO.Internal.Parsing;

/// <summary>
/// Parses the decompressed <c>AcDb:AppInfo</c> payload.
/// AC1018 (R2004) files store 8-bit strings; AC1021+ (R2007+) files store UTF-16LE strings
/// whose counted length includes the NUL. Layout is auto-detected from the leading u32
/// ("2"/"3" marks the UTF-16 layout).
/// </summary>
internal static partial class AppInfoParser
{
    private static readonly byte[] VersionEntryId =
        [0x1B, 0xD8, 0x48, 0xF3, 0xCC, 0x0A, 0x3E, 0x4D, 0xBA, 0xB1, 0xCF, 0x81, 0xF7, 0xB4, 0x50, 0xB3];

    private static readonly byte[] CommentEntryId =
        [0xB8, 0xD0, 0xF0, 0x25, 0xA1, 0xD7, 0x93, 0x49, 0xB2, 0xFA, 0x9B, 0xF9, 0x28, 0x6F, 0xA1, 0xFD];

    private static readonly byte[] ProductXmlEntryId =
        [0xE8, 0xE0, 0x96, 0x51, 0xC5, 0xCE, 0xB2, 0x44, 0xA8, 0xBF, 0xF6, 0xE8, 0x3B, 0x85, 0x9D, 0x44];

    [GeneratedRegex("build_version=\\\\?\"([^\"\\\\]+)")]
    private static partial Regex BuildVersionRegex();

    /// <summary>Parses an AppInfo payload; <paramref name="codepage"/> decodes 8-bit strings.</summary>
    public static DwgAppInfo Parse(byte[] data, int codepage)
    {
        if (data.Length >= 4)
        {
            uint marker = BinaryPrimitives.ReadUInt32LittleEndian(data);
            if (marker is 2 or 3)
            {
                var unicodeInfo = TryParseUnicode(data, codepage);
                if (unicodeInfo is not null)
                {
                    return unicodeInfo;
                }
            }
        }

        return ParseAnsi(data, codepage);
    }

    private static DwgAppInfo ParseAnsi(byte[] data, int codepage)
    {
        var cursor = new MetadataByteCursor(data, codepage);
        var name = cursor.ReadAnsiString16();
        cursor.ReadU32();
        cursor.ReadAnsiString16();
        var productXml = cursor.ReadAnsiString16();
        var version = TryRead(cursor, c => c.ReadAnsiString16());
        return new DwgAppInfo
        {
            Name = name,
            ProductXml = productXml,
            Version = version ?? string.Empty,
        };
    }

    /// <summary>
    /// UTF-16 layout written by AC1021+ (R2007+) files:
    /// <c>u32 marker, [u16] name, u32 entry count</c>, then <c>count</c> entries of
    /// <c>16B identifier, [u16] string</c>. Identifiers are stable across files, but
    /// entry order and count vary; unknown entries are skipped. The version entry
    /// holds the application version, and the product XML's build_version
    /// attribute is the fallback when the version entry is absent.
    /// </summary>
    private static DwgAppInfo? TryParseUnicode(byte[] data, int codepage)
    {
        try
        {
            var cursor = new MetadataByteCursor(data, codepage);
            cursor.ReadU32(); // variant marker
            var name = cursor.ReadUnicodeStringWithNul();
            uint entryCount = cursor.ReadU32();
            var version = string.Empty;
            var comment = string.Empty;
            var productXml = string.Empty;

            for (uint i = 0; i < entryCount; i++)
            {
                var id = cursor.ReadBytes(16);
                var text = cursor.ReadUnicodeStringWithNul();
                if (id.AsSpan().SequenceEqual(VersionEntryId))
                {
                    version = text;
                }
                else if (id.AsSpan().SequenceEqual(CommentEntryId))
                {
                    comment = text;
                }
                else if (id.AsSpan().SequenceEqual(ProductXmlEntryId))
                {
                    productXml = text;
                }
            }

            if (version.Length == 0 && productXml.Length > 0
                && BuildVersionRegex().Match(productXml) is { Success: true } match)
            {
                version = match.Groups[1].Value;
            }

            return new DwgAppInfo
            {
                Name = name,
                Version = version,
                Comment = comment,
                ProductXml = productXml,
            };
        }
        catch (DwgException)
        {
            return null;
        }
    }

    private static string? TryRead(MetadataByteCursor cursor, Func<MetadataByteCursor, string> read)
    {
        try
        {
            return read(cursor);
        }
        catch (DwgException)
        {
            return null;
        }
    }
}
