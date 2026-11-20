using DwgIO.Model;

namespace DwgIO.Internal.Parsing;

/// <summary>
/// Walks the decompressed <c>AcDb:AppInfoHistory</c> payload as a sequence of AppInfo records, one per
/// earlier save of the drawing. The layout of the section is not documented anywhere; each record uses the
/// same shape as <c>AcDb:AppInfo</c> (variant marker, name, entries), and the walk stops at the first
/// offset that does not decode into a record, reporting that in the diagnostic.
/// </summary>
internal static class AppInfoHistoryParser
{
    /// <summary>Maximum number of records accepted; a malformed stream cannot loop forever.</summary>
    private const int MaxRecords = 4096;

    public static IReadOnlyList<DwgAppInfo> Parse(byte[] data, int codepage, Action<string> diagnostic)
    {
        var records = new List<DwgAppInfo>();
        int pos = 0;
        while (pos < data.Length && records.Count < MaxRecords)
        {
            int remaining = data.Length - pos;
            if (remaining < 4)
            {
                diagnostic($"AcDb:AppInfoHistory: {remaining} trailing byte(s) after {records.Count} records are too small for a record");
                break;
            }

            DwgAppInfo record;
            int consumed;
            try
            {
                record = AppInfoParser.ParseOne(data[pos..], codepage, out consumed);
            }
            catch (DwgException ex)
            {
                diagnostic($"AcDb:AppInfoHistory: record {records.Count} at offset {pos} does not decode ({ex.Message}); the rest of the section is skipped");
                break;
            }

            if (consumed <= 0 || consumed > remaining)
            {
                diagnostic($"AcDb:AppInfoHistory: record {records.Count} at offset {pos} claims {consumed} of {remaining} bytes; the rest of the section is skipped");
                break;
            }

            if (record.Name.Length == 0)
            {
                // Padding or an erased record: the walk stops without noise.
                break;
            }

            records.Add(record);
            pos += consumed;
        }

        return records;
    }
}
