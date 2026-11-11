using System.Text.Json;

namespace DwgIO.Json;

/// <summary>Writes a parsed <see cref="DwgDocument"/> to JSON.</summary>
public static class DwgJsonExporter
{
    /// <summary>Writes <paramref name="document"/> as JSON to <paramref name="output"/>.</summary>
    public static void Write(DwgDocument document, Stream output, DwgJsonExportOptions options)
    {
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions
        {
            Indented = options.Indented,
        });

        writer.WriteStartObject();

        writer.WriteStartObject("file");
        writer.WriteString("path", document.Path);
        writer.WriteNumber("size", document.FileSize);
        writer.WriteEndObject();

        writer.WriteStartObject("version");
        writer.WriteString("magic", document.Version.Magic);
        writer.WriteString("release", document.Version.Release);
        writer.WriteNumber("year", document.Version.Year);
        writer.WriteString("family", document.Version.Family.ToString());
        writer.WriteEndObject();

        if (document.ContainerHeader is { } header)
        {
            writer.WriteStartObject("containerHeader");
            writer.WriteNumber("maintenanceVersion", header.FileHeader.MaintenanceVersion);
            writer.WriteString("codepage", header.FileHeader.CodepageName);
            writer.WriteNumber("securityFlags", header.SecurityFlags);
            writer.WriteNumber("summaryInfoOffset", header.SummaryInfoOffset);
            writer.WriteNumber("vbaProjectOffset", header.VbaProjectOffset);
            writer.WriteString("fileId", header.FileId);
            writer.WriteNumber("lastPageNumber", header.LastPageNumber);
            writer.WriteNumber("gapCount", header.GapCount);
            writer.WriteNumber("pageCount", header.PageCount);
            writer.WriteNumber("pageMapPageNumber", header.PageMapPageNumber);
            writer.WriteNumber("pageMapOffset", header.PageMapOffset);
            writer.WriteNumber("sectionMapPageNumber", header.SectionMapPageNumber);
            writer.WriteNumber("pageTableLength", header.PageTableLength);
            writer.WriteNumber("gapTableLength", header.GapTableLength);
            writer.WriteBoolean("crc32Valid", header.HeaderCrcValid);
            writer.WriteEndObject();
        }

        if (document.SummaryInfo is { } summary)
        {
            writer.WriteStartObject("summaryInfo");
            writer.WriteString("title", summary.Title);
            writer.WriteString("subject", summary.Subject);
            writer.WriteString("author", summary.Author);
            writer.WriteString("keywords", summary.Keywords);
            writer.WriteString("comments", summary.Comments);
            writer.WriteString("lastSavedBy", summary.LastSavedBy);
            writer.WriteString("revisionNumber", summary.RevisionNumber);
            writer.WriteString("hyperlinkBase", summary.HyperlinkBase);
            writer.WriteString("createdUtc", summary.CreatedUtc?.ToString("O"));
            writer.WriteString("modifiedUtc", summary.ModifiedUtc?.ToString("O"));
            writer.WriteStartArray("customProperties");
            foreach (var property in summary.Properties)
            {
                writer.WriteStartObject();
                writer.WriteString("key", property.Key);
                writer.WriteString("value", property.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteNumber("pageCount", document.PageCount ?? 0);

        if (document.AppInfo is { } appInfo)
        {
            writer.WriteStartObject("appInfo");
            writer.WriteString("name", appInfo.Name);
            writer.WriteString("version", appInfo.Version);
            writer.WriteString("comment", appInfo.Comment);
            writer.WriteString("product", appInfo.Product);
            writer.WriteString("productXml", appInfo.ProductXml);
            writer.WriteEndObject();
        }

        if (document.Preview is { } preview)
        {
            writer.WriteStartObject("preview");
            writer.WriteString("imageKind", preview.ImageKind.ToString());
            writer.WriteNumber("imageSize", preview.ImageBytes.Length);
            writer.WriteEndObject();
        }

        if (document.FileDepList is { } deps)
        {
            writer.WriteStartObject("fileDepList");
            writer.WriteStartArray("features");
            foreach (var feature in deps.Features)
            {
                writer.WriteStringValue(feature);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("files");
            foreach (var file in deps.Files)
            {
                writer.WriteStartObject();
                writer.WriteString("filename", file.Filename);
                writer.WriteString("foundPath", file.FoundPath);
                writer.WriteNumber("fileSize", file.FileSize);
                writer.WriteBoolean("affectsGraphics", file.AffectsGraphics);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteStartObject("objects");
        writer.WriteNumber("count", document.Objects.Count);
        writer.WriteNumber("handleMapEntries", document.HandleMap?.Count ?? 0);
        writer.WriteStartObject("byType");
        foreach (var group in document.Objects.GroupBy(o => o.TypeCode).OrderByDescending(g => g.Count()))
        {
            writer.WriteNumber(group.Key.ToString("X", System.Globalization.CultureInfo.InvariantCulture), group.Count());
        }

        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteNumber("entityCount", document.Entities.Count);
        writer.WriteStartArray("entities");
        foreach (var entity in document.Entities)
        {
            writer.WriteStartObject();
            writer.WriteString("type", entity.TypeName);
            writer.WriteString("handle", entity.Handle.ToString("X", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteString("kind", entity.Kind.ToString());
            if (entity.Geometry is { } geometry)
            {
                writer.WriteStartArray("geometry");
                foreach (var value in geometry)
                {
                    writer.WriteNumberValue(Math.Round(value, 6));
                }

                writer.WriteEndArray();
            }

            if (entity.ColorIndex != 0)
            {
                writer.WriteNumber("colorIndex", entity.ColorIndex);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("sections");
        foreach (var section in document.Sections)
        {
            writer.WriteStartObject();
            writer.WriteString("name", section.Name);
            writer.WriteNumber("size", section.Size);
            writer.WriteNumber("pageCount", section.PageCount);
            writer.WriteNumber("maxPageSize", section.MaxPageSize);
            writer.WriteNumber("compression", section.Compression);
            writer.WriteNumber("encryption", section.Encryption);
            writer.WriteNumber("sectionId", section.SectionId);
            if (options.IncludeSectionContents && IsIncluded(document, section.Name, options))
            {
                try
                {
                    var bytes = document.ReadSection(section.Name);
                    writer.WriteString("contentBase64", Convert.ToBase64String(bytes));
                }
                catch (DwgException ex)
                {
                    writer.WriteString("readError", ex.Message);
                }
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    /// <summary>Writes <paramref name="document"/> as JSON to a new file at <paramref name="path"/>.</summary>
    public static void WriteFile(DwgDocument document, string path, DwgJsonExportOptions options)
    {
        using var stream = File.Create(path);
        Write(document, stream, options);
    }

    private static bool IsIncluded(DwgDocument document, string name, DwgJsonExportOptions options) =>
        options.SectionsToInclude.Count == 0 || options.SectionsToInclude.Contains(name);
}
