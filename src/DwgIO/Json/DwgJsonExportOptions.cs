namespace DwgIO.Json;

/// <summary>Options for the JSON export.</summary>
public sealed class DwgJsonExportOptions
{
    /// <summary>Options with every default value.</summary>
    public static DwgJsonExportOptions Default { get; } = new();

    /// <summary>Indent the output (default: true).</summary>
    public bool Indented { get; set; } = true;

    /// <summary>Embed decompressed section contents in the JSON (base64; large!). Default: false.</summary>
    public bool IncludeSectionContents { get; set; }

    /// <summary>Explicit section names to embed when <see cref="IncludeSectionContents"/> is true;
    /// empty means every readable section.</summary>
    public HashSet<string> SectionsToInclude { get; } = new(StringComparer.OrdinalIgnoreCase);
}
