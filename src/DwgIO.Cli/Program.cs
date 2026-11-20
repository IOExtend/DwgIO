using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DwgIO;
using DwgIO.Output;
using DwgIO.Json;
using DwgIO.Model;
using DwgIO.Scene;
using IOScene;

namespace DwgIO.Cli;

internal static class Program
{
    /// <summary>Formats a count or byte size with digit grouping, invariantly, so the output does not depend
    /// on the machine's locale.</summary>
    internal static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Formats a bare number, invariantly.</summary>
    internal static string Plain(double value) => value.ToString(CultureInfo.InvariantCulture);

    private const int ExitOk = 0;
    private const int ExitUsage = 1;
    private const int ExitFileNotFound = 2;
    private const int ExitUnsupportedVersion = 3;
    private const int ExitParseError = 4;
    private const int ExitNoPreview = 5;

    private static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>Runs one command. Internal for the CLI tests.</summary>
    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0)
        {
            PrintUsage(output);
            return ExitUsage;
        }

        try
        {
            return args[0] switch
            {
                "--version" or "-v" => PrintVersion(output),
                "--help" or "-h" => PrintHelp(output),
                "inspect" => Inspect(args, output, error),
                "extract" => Extract(args, output, error),
                "preview" => Preview(args, output, error),
                "scene" => Scene(args, output, error),
                "svg" => Svg(args, output, error),
                "entities" => Entities(args, output, error),
                string option when option.StartsWith('-') => UnknownOption(option, output, error),
                _ => Export(args, output, error),
            };
        }
        catch (FileNotFoundException ex)
        {
            error.WriteLine($"error: {ex.Message}");
            return ExitFileNotFound;
        }
        catch (DwgUnsupportedVersionException ex)
        {
            error.WriteLine($"error: {ex.Message}");
            return ExitUnsupportedVersion;
        }
        catch (DwgException ex)
        {
            error.WriteLine($"error: {ex.Message}");
            return ExitParseError;
        }
        catch (Exception ex)
        {
            error.WriteLine($"unexpected error: {ex.Message}");
            return ExitParseError;
        }
    }

    private static int PrintVersion(TextWriter output)
    {
        string version = typeof(Program).Assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0] ?? "unknown";
        output.WriteLine(version);
        return ExitOk;
    }

    private static int PrintHelp(TextWriter output)
    {
        PrintUsage(output);
        return ExitOk;
    }

    private static int UnknownOption(string option, TextWriter output, TextWriter error)
    {
        error.WriteLine($"error: unknown option {option}.");
        PrintUsage(output);
        return ExitUsage;
    }

    private static void PrintUsage(TextWriter output)
    {
        output.WriteLine("""
            dwgio — read Autodesk DWG files without AutoCAD

            Usage:
              dwgio inspect <file.dwg>                 Print version + section table
              dwgio <file.dwg> <output>                Export metadata, section catalog and entities to JSON,
                                                       or the scene to OBJ (.obj), GLB (.glb) or a standalone
                                                       interactive 3D HTML viewer (.html)
              dwgio extract <file.dwg> <section> <out> Write a named section's decompressed bytes
              dwgio preview <file.dwg> <out>           Write the embedded thumbnail image
              dwgio scene <file.dwg> <out.json> [--unit u]  Export a neutral IOScene scene document
                                                       (u: mm, cm, m (default), ft or in)
              dwgio svg <file.dwg> <out.svg> [--scale s]    Draw model space to SVG (s: px per unit, default 1)
              dwgio entities <file.dwg> [--type LINE,CIRCLE] [--json]
                                                       List decoded entities, one line each or as JSON
              dwgio --version                          Print the version
              dwgio --help                             Print this usage

            Sections are named as stored, e.g. AcDb:Header, AcDb:Classes, AcDb:SummaryInfo.
            """);
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";

    private static int Inspect(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 2)
        {
            PrintUsage(output);
            return ExitUsage;
        }

        var doc = DwgDocument.Open(args[1]);
        return InspectDocument(doc, output);
    }

    /// <summary>Writes the inspect report of an opened document. Internal for the CLI tests.</summary>
    internal static int InspectDocument(DwgDocument doc, TextWriter output)
    {
        output.WriteLine($"{doc.Path}  ({Count(doc.FileSize)} bytes)");
        output.WriteLine($"Version: {doc.Version}  codepage={doc.FileHeader.CodepageName}  pages={doc.PageCount}  headerCrc={(doc.ContainerHeader?.HeaderCrcValid == true ? "ok" : doc.ContainerHeader is null ? "n/a" : "MISMATCH")}");
        if (doc.SummaryInfo is { } summary)
        {
            var title = string.IsNullOrWhiteSpace(summary.Title) ? "(no title)" : summary.Title;
            output.WriteLine($"Summary: \"{title}\"  author={summary.Author}  savedBy={summary.LastSavedBy}  modified={summary.ModifiedUtc:yyyy-MM-dd HH:mm} UTC  revision={summary.RevisionNumber}");
            foreach (var property in summary.Properties)
            {
                output.WriteLine($"  custom: {property.Key} = {property.Value}");
            }
        }

        if (doc.AppInfo is { } appInfo)
        {
            output.WriteLine($"App: {appInfo.Name}  version={appInfo.Version}  product={appInfo.Product}");
            if (!string.IsNullOrEmpty(appInfo.Comment))
            {
                output.WriteLine($"  comment: {Truncate(appInfo.Comment, 120)}");
            }
        }

        if (!doc.AppInfoHistory.IsEmpty)
        {
            output.WriteLine($"App history: {Count(doc.AppInfoHistory.Length)} bytes (raw)");
        }

        if (doc.RevHistory is { } revHistory)
        {
            output.WriteLine($"Rev history: layout {revHistory.ClassVersion}.{revHistory.ClassMinor}  saves={Count(revHistory.Histories.Count)}");
        }

        if (doc.AuxHeader is { } auxHeader)
        {
            output.WriteLine($"Aux header: saves={Count(auxHeader.Numsaves)}  created={auxHeader.CreatedUtc:yyyy-MM-dd HH:mm} UTC  updated={auxHeader.ModifiedUtc:yyyy-MM-dd HH:mm} UTC  handseed={auxHeader.Handseed:X}");
        }

        if (doc.Template is { } template)
        {
            string units = template.IsMetric ? "metric" : "english";
            output.WriteLine($"Template: \"{Truncate(template.Description, 60)}\"  units={units}");
        }

        if (doc.Preview is { } preview)
        {
            output.WriteLine($"Preview: {preview.ImageKind} {Count(preview.ImageBytes.Length)} bytes");
        }

        if (doc.FileDepList is { } deps && deps.Files.Count > 0)
        {
            output.WriteLine($"File dependencies: {deps.Files.Count}");
            foreach (var dep in deps.Files.Take(5))
            {
                output.WriteLine($"  {dep.Filename}  ({Count(dep.FileSize)} bytes)");
            }
        }

        if (doc.Objects.Count > 0)
        {
            output.WriteLine($"Objects: {Count(doc.Objects.Count)}  (handle map: {Count(doc.HandleMap?.Count ?? 0)})");
            foreach (var group in doc.Objects.GroupBy(o => o.TypeName).OrderByDescending(g => g.Count()).Take(8))
            {
                output.WriteLine($"  {group.Key}: {Count(group.Count())}");
            }
        }

        if (doc.Entities.Count > 0)
        {
            output.WriteLine($"Entities decoded: {Count(doc.Entities.Count)}");
            foreach (var group in doc.Entities.GroupBy(e => e.Kind).OrderByDescending(g => g.Count()))
            {
                output.WriteLine($"  {group.Key}: {Count(group.Count())}");
            }

            foreach (var entity in doc.Entities.Where(e => e.Kind != DwgEntityKind.Unknown).Take(5))
            {
                output.WriteLine($"  {entity}");
            }
        }

        if (doc.Diagnostics.Count > 0)
        {
            // Always printed, whether or not any entity was decoded: a failed section shows up here alone.
            output.WriteLine($"Diagnostics: {Count(doc.Diagnostics.Count)}");
            foreach (var diagnostic in doc.Diagnostics.Take(3))
            {
                output.WriteLine($"  {diagnostic}");
            }
        }

        output.WriteLine();

        if (doc.Sections.Count == 0)
        {
            output.WriteLine("(section map not read)");
            return ExitOk;
        }

        output.WriteLine($"{"Name",-28} {"Size",12} {"Pages",6} {"Compr",6} {"Enc",4} Id");
        foreach (var s in doc.Sections)
        {
            string name = s.Encryption == 1 ? $"{s.Name} [encrypted]" : s.Name;
            output.WriteLine($"{name,-28} {Count(s.Size),12} {s.PageCount,6} {s.Compression,6} {s.Encryption,4} {s.SectionId}");
        }

        return ExitOk;
    }

    private static int Export(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 2)
        {
            PrintUsage(output);
            return ExitUsage;
        }

        var doc = DwgDocument.Open(args[0]);
        return ExportDocument(doc, args[1], output, error);
    }

    /// <summary>Exports an opened document to a target path, where <c>-</c> means standard output. Internal for the CLI tests.</summary>
    internal static int ExportDocument(DwgDocument doc, string target, TextWriter output, TextWriter error)
    {
        if (target == "-")
        {
            // The JSON export to standard output: no summary line may pollute the stream.
            using var json = new MemoryStream();
            DwgJsonExporter.Write(doc, json, DwgJsonExportOptions.Default);
            output.Write(Encoding.UTF8.GetString(json.ToArray()));
            return ExitOk;
        }

        if (Path.GetExtension(target).Equals(".obj", StringComparison.OrdinalIgnoreCase))
        {
            var objResult = DwgSceneExport.SaveObj(doc, target);
            output.WriteLine($"{doc.Version}  elements={objResult.ElementCount} ({objResult.ElementsWithGeometry} with geometry)  ->  {target}");
        }
        else if (Path.GetExtension(target).Equals(".glb", StringComparison.OrdinalIgnoreCase))
        {
            var glbResult = DwgSceneExport.SaveGlb(doc, target);
            output.WriteLine($"{doc.Version}  elements={glbResult.ElementCount} ({glbResult.ElementsWithGeometry} with geometry)  ->  {target}");
        }
        else if (Path.GetExtension(target).Equals(".html", StringComparison.OrdinalIgnoreCase))
        {
            var htmlResult = DwgViewerExport.SaveViewer(doc, target);
            output.WriteLine($"{doc.Version}  elements={htmlResult.ElementCount} ({htmlResult.ElementsWithGeometry} with geometry)  ->  {target}");
        }
        else
        {
            DwgJsonExporter.WriteFile(doc, target, DwgJsonExportOptions.Default);
            output.WriteLine($"{doc.Version}  sections={doc.Sections.Count}  ->  {target}");
        }

        return ExitOk;
    }

    private static int Extract(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 4)
        {
            PrintUsage(output);
            return ExitUsage;
        }

        var doc = DwgDocument.Open(args[1]);
        var bytes = doc.ReadSection(args[2]);
        File.WriteAllBytes(args[3], bytes);
        output.WriteLine($"{args[2]}: {Count(bytes.Length)} bytes ->  {args[3]}");
        return ExitOk;
    }

    private static int Svg(string[] args, TextWriter output, TextWriter error)
    {
        double scale = 1.0;
        var positional = new List<string>(2);
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--scale")
            {
                if (i + 1 >= args.Length || !double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out scale) || !double.IsFinite(scale) || scale <= 0)
                {
                    error.WriteLine("error: --scale needs a positive number of pixels per drawing unit.");
                    return ExitUsage;
                }

                i++;
            }
            else
            {
                positional.Add(args[i]);
            }
        }

        if (positional.Count != 2)
        {
            PrintUsage(output);
            return ExitUsage;
        }

        var doc = DwgDocument.Open(positional[0]);
        DwgSvgExporter.WriteFile(doc, positional[1], new DwgSvgOptions { Scale = scale });
        output.WriteLine($"{doc.Version}  entities={Count(doc.Entities.Count)}  ->  {positional[1]}");
        return ExitOk;
    }

    private static int Scene(string[] args, TextWriter output, TextWriter error)
    {
        string? unit = null;
        var positional = new List<string>(3);
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--unit")
            {
                if (i + 1 >= args.Length)
                {
                    error.WriteLine("error: --unit needs a value (mm, cm, m, ft or in).");
                    return ExitUsage;
                }

                unit = args[++i];
            }
            else
            {
                positional.Add(args[i]);
            }
        }

        if (positional.Count != 2)
        {
            PrintUsage(output);
            return ExitUsage;
        }

        var options = new DwgSceneOptions();
        if (unit is not null)
        {
            if (!DwgSceneReader.IsSupportedUnit(unit))
            {
                error.WriteLine($"error: unknown length unit '{unit}'. Use mm, cm, m, ft or in.");
                return ExitUsage;
            }

            options.LengthUnit = unit;
        }

        var result = DwgSceneReader.Read(positional[0], options);
        AtomicOutput.Write(positional[1], stream => SceneJson.Write(result.Document, stream));
        output.WriteLine(
            $"{result.Document.Title}: {Count(result.ElementCount)} elements ({Count(result.ElementsWithGeometry)} with geometry)  ->  {positional[1]}");
        foreach (var diagnostic in result.Diagnostics.Take(5))
        {
            output.WriteLine($"  {diagnostic.Severity.ToString().ToLowerInvariant()}: {diagnostic.Message}");
        }

        if (result.Diagnostics.Count > 5)
        {
            output.WriteLine($"  ... and {Count(result.Diagnostics.Count - 5)} more");
        }

        return ExitOk;
    }

    private static int Preview(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 3)
        {
            PrintUsage(output);
            return ExitUsage;
        }

        var doc = DwgDocument.Open(args[1]);
        return PreviewDocument(doc, args[2], output, error);
    }

    /// <summary>Writes the thumbnail of an opened document. Internal for the CLI tests.</summary>
    internal static int PreviewDocument(DwgDocument doc, string requested, TextWriter output, TextWriter error)
    {
        if (doc.PreviewReadError is not null)
        {
            error.WriteLine($"error: {doc.PreviewReadError}");
            return ExitParseError;
        }

        var preview = doc.Preview;
        if (preview is null || !preview.HasImage)
        {
            error.WriteLine("error: this drawing has no thumbnail preview.");
            return ExitNoPreview;
        }

        var (target, warning) = ResolvePreviewTarget(preview.ImageKind, requested);
        if (warning is not null)
        {
            error.WriteLine(warning);
        }

        File.WriteAllBytes(target, preview.ImageBytes);
        output.WriteLine($"Preview: {preview.ImageKind} {Count(preview.ImageBytes.Length)} bytes ->  {target}");
        return ExitOk;
    }

    /// <summary>Chooses the output path for a preview: the kind's extension is added when the path has none, and
    /// a mismatching extension is reported instead of being changed. Internal for the CLI tests.</summary>
    internal static (string Target, string? Warning) ResolvePreviewTarget(DwgPreviewImageKind kind, string requested)
    {
        string expected = kind switch
        {
            DwgPreviewImageKind.Png => ".png",
            DwgPreviewImageKind.Wmf => ".wmf",
            _ => ".bmp",
        };
        bool isDirectory = requested.EndsWith(Path.DirectorySeparatorChar) || requested.EndsWith('/');
        if (!Path.HasExtension(requested) || isDirectory)
        {
            return (requested + expected, null);
        }

        string actual = Path.GetExtension(requested);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            return (requested, $"warning: the image is {kind} (expected {expected}); the requested path keeps {actual}");
        }

        return (requested, null);
    }

    /// <summary>Lists decoded entities, one line each, or as a JSON array with --json (#19).</summary>
    private static int Entities(string[] args, TextWriter output, TextWriter error)
    {
        string? file = null;
        string? types = null;
        bool json = false;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--type":
                    if (i + 1 >= args.Length)
                    {
                        error.WriteLine("error: --type needs a comma-separated list of type names (e.g. LINE,CIRCLE).");
                        return ExitUsage;
                    }

                    types = args[++i];
                    break;
                case "--json":
                    json = true;
                    break;
                case string option when option.StartsWith('-'):
                    return UnknownOption(option, output, error);
                default:
                    if (file is not null)
                    {
                        error.WriteLine($"error: unexpected argument '{args[i]}'.");
                        return ExitUsage;
                    }

                    file = args[i];
                    break;
            }
        }

        HashSet<string>? filter = null;
        if (types is not null)
        {
            filter = [];
            foreach (string name in types.Split(','))
            {
                string trimmed = name.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (!DwgObjectTypes.KnownNames.Contains(trimmed))
                {
                    error.WriteLine($"error: unknown type '{trimmed}'. Known types include LINE, CIRCLE, ARC, POINT, TEXT and MTEXT.");
                    return ExitUsage;
                }

                filter.Add(trimmed);
            }

            if (filter.Count == 0)
            {
                error.WriteLine("error: --type needs at least one type name.");
                return ExitUsage;
            }
        }

        if (file is null)
        {
            PrintUsage(output);
            return ExitUsage;
        }

        var doc = DwgDocument.Open(file);
        return EntitiesDocument(doc, json, filter, output);
    }

    /// <summary>Writes the entities of an opened document, filtered by type; internal for the CLI tests.</summary>
    internal static int EntitiesDocument(DwgDocument document, bool json, HashSet<string>? filter, TextWriter output)
    {
        IEnumerable<DwgEntity> entities = document.Entities;
        if (filter is not null)
        {
            entities = entities.Where(e => filter.Contains(e.TypeName));
        }

        if (json)
        {
            WriteEntitiesJson(output, entities);
        }
        else
        {
            foreach (var entity in entities)
            {
                output.WriteLine(entity.ToString());
            }
        }

        return ExitOk;
    }

    private static void WriteEntitiesJson(TextWriter output, IEnumerable<DwgEntity> entities)
    {
        using var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartArray();
            foreach (var entity in entities)
            {
                writer.WriteStartObject();
                writer.WriteString("type", entity.TypeName);
                writer.WriteString("handle", entity.Handle.ToString("X", CultureInfo.InvariantCulture));
                writer.WriteString("kind", entity.Kind.ToString());
                if (entity.Geometry is { } geometry)
                {
                    writer.WriteStartArray("geometry");
                    foreach (var value in geometry)
                    {
                        if (double.IsFinite(value))
                        {
                            writer.WriteNumberValue(Math.Round(value, 6));
                        }
                        else
                        {
                            writer.WriteNullValue();
                        }
                    }

                    writer.WriteEndArray();
                }

                writer.WriteNumber("colorIndex", entity.ColorIndex);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        output.Write(Encoding.UTF8.GetString(json.ToArray()));
    }
}
