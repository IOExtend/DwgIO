using System.Globalization;
using DwgIO;
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

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return ExitUsage;
        }

        try
        {
            return args[0] switch
            {
                "inspect" => Inspect(args),
                "extract" => Extract(args),
                "preview" => Preview(args),
                "scene" => Scene(args),
                "svg" => Svg(args),
                _ => Export(args),
            };
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitFileNotFound;
        }
        catch (DwgUnsupportedVersionException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitUnsupportedVersion;
        }
        catch (DwgException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitParseError;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"unexpected error: {ex.Message}");
            return ExitParseError;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
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

            Sections are named as stored, e.g. AcDb:Header, AcDb:Classes, AcDb:SummaryInfo.
            """);
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";

    private static int Inspect(string[] args)
    {
        if (args.Length != 2)
        {
            PrintUsage();
            return ExitUsage;
        }

        var doc = DwgDocument.Open(args[1]);
        return InspectDocument(doc, Console.Out);
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
            foreach (var group in doc.Objects.GroupBy(o => o.TypeCode).OrderByDescending(g => g.Count()).Take(8))
            {
                output.WriteLine($"  type 0x{group.Key:X3}: {Count(group.Count())}");
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

    private static int Export(string[] args)
    {
        if (args.Length != 2)
        {
            PrintUsage();
            return ExitUsage;
        }

        var doc = DwgDocument.Open(args[0]);
        string output = args[1];
        if (Path.GetExtension(output).Equals(".obj", StringComparison.OrdinalIgnoreCase))
        {
            var objResult = DwgSceneExport.SaveObj(doc, output);
            Console.WriteLine($"{doc.Version}  elements={objResult.ElementCount} ({objResult.ElementsWithGeometry} with geometry)  ->  {output}");
        }
        else if (Path.GetExtension(output).Equals(".glb", StringComparison.OrdinalIgnoreCase))
        {
            var glbResult = DwgSceneExport.SaveGlb(doc, output);
            Console.WriteLine($"{doc.Version}  elements={glbResult.ElementCount} ({glbResult.ElementsWithGeometry} with geometry)  ->  {output}");
        }
        else if (Path.GetExtension(output).Equals(".html", StringComparison.OrdinalIgnoreCase))
        {
            var htmlResult = DwgViewerExport.SaveViewer(doc, output);
            Console.WriteLine($"{doc.Version}  elements={htmlResult.ElementCount} ({htmlResult.ElementsWithGeometry} with geometry)  ->  {output}");
        }
        else
        {
            DwgJsonExporter.WriteFile(doc, output, DwgJsonExportOptions.Default);
            Console.WriteLine($"{doc.Version}  sections={doc.Sections.Count}  ->  {output}");
        }

        return ExitOk;
    }

    private static int Extract(string[] args)
    {
        if (args.Length != 4)
        {
            PrintUsage();
            return ExitUsage;
        }

        var doc = DwgDocument.Open(args[1]);
        var bytes = doc.ReadSection(args[2]);
        File.WriteAllBytes(args[3], bytes);
        Console.WriteLine($"{args[2]}: {Count(bytes.Length)} bytes ->  {args[3]}");
        return ExitOk;
    }

    private static int Svg(string[] args)
    {
        double scale = 1.0;
        var positional = new List<string>(2);
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--scale")
            {
                if (i + 1 >= args.Length || !double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out scale) || scale <= 0)
                {
                    Console.Error.WriteLine("error: --scale needs a positive number of pixels per drawing unit.");
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
            PrintUsage();
            return ExitUsage;
        }

        var doc = DwgDocument.Open(positional[0]);
        DwgSvgExporter.WriteFile(doc, positional[1], new DwgSvgOptions { Scale = scale });
        Console.WriteLine($"{doc.Version}  entities={doc.Entities.Count}  ->  {positional[1]}");
        return ExitOk;
    }

    private static int Scene(string[] args)
    {
        string? unit = null;
        var positional = new List<string>(3);
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--unit")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("error: --unit needs a value (mm, cm, m, ft or in).");
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
            PrintUsage();
            return ExitUsage;
        }

        var options = new DwgSceneOptions();
        if (unit is not null)
        {
            if (!DwgSceneReader.IsSupportedUnit(unit))
            {
                Console.Error.WriteLine($"error: unknown length unit '{unit}'. Use mm, cm, m, ft or in.");
                return ExitUsage;
            }

            options.LengthUnit = unit;
        }

        var result = DwgSceneReader.Read(positional[0], options);
        SceneJson.Save(result.Document, positional[1]);
        Console.WriteLine(
            $"{result.Document.Title}: {result.ElementCount} elements ({result.ElementsWithGeometry} with geometry)  ->  {positional[1]}");
        foreach (var diagnostic in result.Diagnostics.Take(5))
        {
            Console.WriteLine($"  {diagnostic.Severity.ToString().ToLowerInvariant()}: {diagnostic.Message}");
        }

        if (result.Diagnostics.Count > 5)
        {
            Console.WriteLine($"  ... and {result.Diagnostics.Count - 5} more");
        }

        return ExitOk;
    }

    private static int Preview(string[] args)
    {
        if (args.Length != 3)
        {
            PrintUsage();
            return ExitUsage;
        }

        var doc = DwgDocument.Open(args[1]);
        return PreviewDocument(doc, args[2], Console.Out, Console.Error);
    }

    /// <summary>Writes the thumbnail of an opened document. Internal for the CLI tests.</summary>
    internal static int PreviewDocument(DwgDocument doc, string requested, TextWriter output, TextWriter error)
    {
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
}
