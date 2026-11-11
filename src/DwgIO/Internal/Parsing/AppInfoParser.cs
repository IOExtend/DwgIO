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
    /// <c>u32 marker, [u16] name, u32, 16B checksum, [u16] comment, 16B checksum,
    /// [u16] product XML, trailing reserve</c>. The XML blob's build_version
    /// attribute doubles as the application version.
    /// </summary>
    private static DwgAppInfo? TryParseUnicode(byte[] data, int codepage)
    {
        try
        {
            var cursor = new MetadataByteCursor(data, codepage);
            cursor.ReadU32(); // variant marker
            var name = cursor.ReadUnicodeStringWithNul();
            cursor.ReadU32();
            cursor.Skip(16); // data checksum
            var comment = cursor.ReadUnicodeStringWithNul();
            cursor.Skip(16); // data checksum
            var productXml = cursor.ReadUnicodeStringWithNul();
            var version = string.IsNullOrEmpty(productXml)
                ? string.Empty
                : BuildVersionRegex().Match(productXml) is { Success: true } match
                    ? match.Groups[1].Value
                    : string.Empty;

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
