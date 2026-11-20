using DwgIO.Model;

namespace DwgIO.Internal.Parsing;

/// <summary>
/// Parses the decompressed <c>AcDb:Template</c> payload: a counted description string and the unit
/// system word (0 English, 1 metric), per libredwg's template.spec.
/// </summary>
internal static class TemplateParser
{
    public static DwgTemplate Parse(byte[] data, int codepage)
    {
        var cursor = new MetadataByteCursor(data, codepage);
        string description = cursor.ReadAnsiString16();
        ushort measurement = cursor.ReadU16();
        return new DwgTemplate
        {
            Description = description,
            Measurement = measurement,
        };
    }
}
