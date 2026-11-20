using DwgIO.Output;

namespace DwgIO.Json;

public static partial class DwgJsonExporter
{
    /// <summary>Writes <paramref name="document"/> as JSON to a new file at <paramref name="path"/>.</summary>
    public static void WriteFile(DwgDocument document, string path, DwgJsonExportOptions options)
    {
        AtomicOutput.Write(path, stream => Write(document, stream, options));
    }
}
