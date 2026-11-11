using System.Buffers.Binary;
using System.Text;
using DwgIO.Model;

#pragma warning disable CA1866 // byte pattern literals below are compared through LINQ for clarity

namespace DwgIO.Internal.Parsing;

/// <summary>
/// Parses the decompressed <c>AcDb:SummaryInfo</c> payload.
/// String encoding is auto-detected: AC1018 (R2004) files store 8-bit strings
/// (u16 byte length + bytes + NUL), AC1021+ (R2007+) store UTF-16LE (u16 char count,
/// count × 2 bytes where the last code unit is the NUL terminator).
/// </summary>
internal static class SummaryInfoParser
{
    /// <summary>Parses a SummaryInfo payload, trying the UTF-16 layout before the 8-bit one.</summary>
    public static DwgSummaryInfo Parse(byte[] data)
    {
        // Try the Unicode layout first (R2007 and later files); fall back to the 8-bit
        // layout for R2004-era files.
        var summary = TryParse(data, unicode: true) ?? TryParse(data, unicode: false)
            ?? throw new DwgException("AcDb:SummaryInfo payload does not match any known string layout.");

        long? created = ToDateTimeUtc(summary.CreateDay, summary.CreateMs);
        long? modified = ToDateTimeUtc(summary.ModifiedDay, summary.ModifiedMs);

        return new DwgSummaryInfo
        {
            Title = summary.Title,
            Subject = summary.Subject,
            Author = summary.Author,
            Keywords = summary.Keywords,
            Comments = summary.Comments,
            LastSavedBy = summary.LastSavedBy,
            RevisionNumber = summary.RevisionNumber,
            HyperlinkBase = summary.HyperlinkBase,
            CreatedUtc = created is null ? null : new DateTime(created.Value, DateTimeKind.Utc),
            ModifiedUtc = modified is null ? null : new DateTime(modified.Value, DateTimeKind.Utc),
            Properties = summary.Properties,
        };
    }

    private static SummaryRaw? TryParse(byte[] data, bool unicode)
    {
        try
        {
            int pos = 0;
            var readString = () => unicode ? ReadUnicodeString(data, ref pos) : ReadAnsiString(data, ref pos);

            var title = readString();
            var subject = readString();
            var author = readString();
            var keywords = readString();
            var comments = readString();
            var lastSavedBy = readString();
            var revisionNumber = readString();
            var hyperlinkBase = readString();

            pos += 8; // total editing time (two i32s; often zero)
            uint createDay = ReadU32(data, ref pos);
            uint createMs = ReadU32(data, ref pos);
            uint modifiedDay = ReadU32(data, ref pos);
            uint modifiedMs = ReadU32(data, ref pos);

            if (pos + 2 > data.Length)
            {
                return null;
            }

            int propertyCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos));
            pos += 2;
            if (propertyCount > 4096)
            {
                return null;
            }

            var properties = new List<KeyValuePair<string, string>>(propertyCount);
            for (int i = 0; i < propertyCount; i++)
            {
                var key = readString();
                var value = readString();
                properties.Add(new KeyValuePair<string, string>(key, value));
            }

            // A correct parse must reach the end of the payload; R2018-era writers append
            // an 8-byte zero reserve after the property list, so a small zero tail is allowed.
            if (pos > data.Length || data.Length - pos > 16 || data.Skip(pos).Any(b => b != 0))
            {
                return null;
            }

            return new SummaryRaw
            {
                Title = title,
                Subject = subject,
                Author = author,
                Keywords = keywords,
                Comments = comments,
                LastSavedBy = lastSavedBy,
                RevisionNumber = revisionNumber,
                HyperlinkBase = hyperlinkBase,
                CreateDay = createDay,
                CreateMs = createMs,
                ModifiedDay = modifiedDay,
                ModifiedMs = modifiedMs,
                Properties = properties,
            };
        }
        catch (DwgException)
        {
            return null;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>AC1021+ layout: u16 char count, then count × 2 bytes where the last code unit is NUL.</summary>
    private static string ReadUnicodeString(byte[] data, ref int pos)
    {
        if (pos + 2 > data.Length)
        {
            throw new DwgException("SummaryInfo string header truncated.");
        }

        int charCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos));
        pos += 2;
        if (charCount == 0 || pos + charCount * 2 > data.Length)
        {
            throw new DwgException("SummaryInfo unicode string truncated.");
        }

        var units = new char[charCount];
        for (int i = 0; i < charCount; i++)
        {
            units[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos + i * 2));
        }

        pos += charCount * 2;
        if (units[^1] != '\0')
        {
            throw new DwgException("SummaryInfo unicode string is not NUL-terminated.");
        }

        return new string(units, 0, charCount - 1);
    }

    /// <summary>AC1018 layout: u16 byte length, then that many bytes, then a NUL byte.</summary>
    private static string ReadAnsiString(byte[] data, ref int pos)
    {
        if (pos + 2 > data.Length)
        {
            throw new DwgException("SummaryInfo string header truncated.");
        }

        int length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos));
        pos += 2;
        if (pos + length + 1 > data.Length)
        {
            throw new DwgException("SummaryInfo ansi string truncated.");
        }

        var value = Encoding.UTF8.GetString(data, pos, length);
        pos += length + 1; // NUL terminator
        return value;
    }

    private static uint ReadU32(byte[] data, ref int pos)
    {
        if (pos + 4 > data.Length)
        {
            throw new DwgException("SummaryInfo date field truncated.");
        }

        var value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));
        pos += 4;
        return value;
    }

    /// <summary>
    /// Converts a DWG Julian date (4-byte Julian Day Number + 4-byte milliseconds into the day)
    /// to .NET UTC ticks; returns null when the day count is implausible.
    /// </summary>
    internal static long? ToDateTimeUtc(uint day, uint ms)
    {
        if (day is < 1_000_000 or > 10_000_000)
        {
            return null;
        }

        long l = day + 68569;
        long n = 4 * l / 146097;
        l -= (146097 * n + 3) / 4;
        long i = 4000 * (l + 1) / 1461001;
        l = l - 1461 * i / 4 + 31;
        long j = 80 * l / 2447;
        long d = l - 2447 * j / 80;
        l = j / 11;
        long month = j + 2 - 12 * l;
        long year = 100 * (n - 49) + i + l;

        if (year is < 1 or > 9999 || month is < 1 or > 12 || d is < 1 or > 31)
        {
            return null;
        }

        var baseDate = new DateTime((int)year, (int)month, (int)d, 0, 0, 0, DateTimeKind.Utc);
        return baseDate.AddMilliseconds(ms).Ticks;
    }

    private sealed class SummaryRaw
    {
        public string Title { get; init; } = string.Empty;
        public string Subject { get; init; } = string.Empty;
        public string Author { get; init; } = string.Empty;
        public string Keywords { get; init; } = string.Empty;
        public string Comments { get; init; } = string.Empty;
        public string LastSavedBy { get; init; } = string.Empty;
        public string RevisionNumber { get; init; } = string.Empty;
        public string HyperlinkBase { get; init; } = string.Empty;
        public uint CreateDay { get; init; }
        public uint CreateMs { get; init; }
        public uint ModifiedDay { get; init; }
        public uint ModifiedMs { get; init; }
        public IReadOnlyList<KeyValuePair<string, string>> Properties { get; init; } = [];
    }
}
