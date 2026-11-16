using System.Security.AccessControl;
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
                int nonFiniteValues = 0;
                foreach (var value in geometry)
                {
                    if (double.IsFinite(value))
                    {
                        writer.WriteNumberValue(Math.Round(value, 6));
                    }
                    else
                    {
                        writer.WriteNullValue();
                        nonFiniteValues++;
                    }
                }

                writer.WriteEndArray();
                if (nonFiniteValues > 0)
                {
                    document.ReportDiagnostic(
                        $"entity {entity.Handle:X}: {nonFiniteValues} non-finite geometry value(s) written as null");
                }
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
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        var tempPath = Path.Combine(directory, $".dwgio-{Guid.NewGuid():N}.tmp");

        // Preserve the destination's access rights across the atomic replace: a fresh
        // temporary file inherits the directory ACL, which may be more permissive than
        // the file being overwritten. Copy the existing rights onto the temporary file
        // before any JSON bytes are written; the same-directory move preserves them.
        // Reading or applying the rights is mandatory: failures abort the export
        // before the destination is touched, instead of silently widening access.
        FileSecurity? savedWindowsAcl = null;
        UnixFileMode? savedUnixMode = null;
        bool destinationExists = File.Exists(fullPath);
        if (destinationExists)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    savedWindowsAcl = new FileInfo(fullPath).GetAccessControl();
                }
                catch (PlatformNotSupportedException)
                {
                    savedWindowsAcl = null;
                }
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                try
                {
                    savedUnixMode = File.GetUnixFileMode(fullPath);
                }
                catch (PlatformNotSupportedException)
                {
                    savedUnixMode = null;
                }
            }
        }

        try
        {
            // Create the temporary file empty first so its rights can be tightened
            // before any document content hits the disk.
            using (var _ = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }

            ApplyTempSecurity(tempPath, savedWindowsAcl, savedUnixMode, destinationExists);

            using (var stream = new FileStream(tempPath, FileMode.Truncate, FileAccess.Write, FileShare.None))
            {
                Write(document, stream, options);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void ApplyTempSecurity(
        string filePath,
        FileSecurity? windowsAcl,
        UnixFileMode? unixMode,
        bool destinationExists)
    {
        if (!destinationExists)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            if (windowsAcl is null)
            {
                return;
            }

            // Copy only the DACL and its protection flag. Persisting the whole
            // FileSecurity (owner/group/SACL) requires SeSecurityPrivilege and,
            // when unmodified, persists nothing — the temporary file would keep
            // the directory's inherited rules.
            new FileInfo(filePath).SetAccessControl(CloneDacl(windowsAcl));
            return;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            if (unixMode.HasValue)
            {
                File.SetUnixFileMode(filePath, unixMode.Value);
            }
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static FileSecurity CloneDacl(FileSecurity source)
    {
        var clone = new FileSecurity();
        clone.SetAccessRuleProtection(source.AreAccessRulesProtected, preserveInheritance: false);
        foreach (FileSystemAccessRule rule in source.GetAccessRules(includeExplicit: true, includeInherited: false, targetType: typeof(System.Security.Principal.SecurityIdentifier)))
        {
            clone.AddAccessRule(rule);
        }

        return clone;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup: the original export failure is the error worth reporting.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup: the original export failure is the error worth reporting.
        }
    }

    private static bool IsIncluded(DwgDocument document, string name, DwgJsonExportOptions options) =>
        options.SectionsToInclude.Count == 0 || options.SectionsToInclude.Contains(name);
}
