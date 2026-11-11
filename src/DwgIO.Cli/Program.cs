using DwgIO;
using DwgIO.Json;
using DwgIO.Model;

namespace DwgIO.Cli;

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitUsage = 1;
    private const int ExitFileNotFound = 2;
    private const int ExitUnsupportedVersion = 3;
    private const int ExitParseError = 4;

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
            Console.Error.WriteLine($"unexpected error: {ex}");
            return ExitParseError;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            dwgio — read Autodesk DWG files without AutoCAD

            Usage:
              dwgio inspect <file.dwg>                 Print version + section table
              dwgio <file.dwg> <output.json>           Export metadata + section catalog to JSON
              dwgio extract <file.dwg> <section> <out> Write a named section's decompressed bytes
              dwgio preview <file.dwg> <out>           Write the embedded thumbnail image

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
        Console.WriteLine($"{doc.Path}  ({doc.FileSize:N0} bytes)");
        Console.WriteLine($"Version: {doc.Version}  codepage={doc.FileHeader.CodepageName}  pages={doc.PageCount}  headerCrc={(doc.ContainerHeader?.HeaderCrcValid == true ? "ok" : doc.ContainerHeader is null ? "n/a" : "MISMATCH")}");
        if (doc.SummaryInfo is { } summary)
        {
            var title = string.IsNullOrWhiteSpace(summary.Title) ? "(no title)" : summary.Title;
            Console.WriteLine($"Summary: \"{title}\"  author={summary.Author}  savedBy={summary.LastSavedBy}  modified={summary.ModifiedUtc:yyyy-MM-dd HH:mm} UTC  revision={summary.RevisionNumber}");
            foreach (var property in summary.Properties)
            {
                Console.WriteLine($"  custom: {property.Key} = {property.Value}");
            }
        }

        if (doc.AppInfo is { } appInfo)
        {
            Console.WriteLine($"App: {appInfo.Name}  version={appInfo.Version}  product={appInfo.Product}");
            if (!string.IsNullOrEmpty(appInfo.Comment))
            {
                Console.WriteLine($"  comment: {Truncate(appInfo.Comment, 120)}");
            }
        }

        if (doc.Preview is { } preview)
        {
            Console.WriteLine($"Preview: {preview.ImageKind} {preview.ImageBytes.Length:N0} bytes");
        }

        if (doc.FileDepList is { } deps && deps.Files.Count > 0)
        {
            Console.WriteLine($"File dependencies: {deps.Files.Count}");
            foreach (var dep in deps.Files.Take(5))
            {
                Console.WriteLine($"  {dep.Filename}  ({dep.FileSize:N0} bytes)");
            }
        }

        if (doc.Objects.Count > 0)
        {
            Console.WriteLine($"Objects: {doc.Objects.Count:N0}  (handle map: {doc.HandleMap?.Count ?? 0:N0})");
            foreach (var group in doc.Objects.GroupBy(o => o.TypeCode).OrderByDescending(g => g.Count()).Take(8))
            {
                Console.WriteLine($"  type 0x{group.Key:X3}: {group.Count():N0}");
            }
        }

        if (doc.Entities.Count > 0)
        {
            Console.WriteLine($"Entities decoded: {doc.Entities.Count:N0}");
            foreach (var group in doc.Entities.GroupBy(e => e.Kind).OrderByDescending(g => g.Count()))
            {
                Console.WriteLine($"  {group.Key}: {group.Count():N0}");
            }

            foreach (var entity in doc.Entities.Where(e => e.Kind != DwgEntityKind.Unknown).Take(5))
            {
                Console.WriteLine($"  {entity}");
            }

            foreach (var diagnostic in doc.Diagnostics.Take(3))
            {
                Console.WriteLine($"  diagnostics: {diagnostic}");
            }
        }

        Console.WriteLine();

        if (doc.Sections.Count == 0)
        {
            Console.WriteLine("(section map not read)");
            return ExitOk;
        }

        Console.WriteLine($"{"Name",-28} {"Size",12} {"Pages",6} {"Compr",6} {"Enc",4} Id");
        foreach (var s in doc.Sections)
        {
            Console.WriteLine($"{s.Name,-28} {s.Size,12:N0} {s.PageCount,6} {s.Compression,6} {s.Encryption,4} {s.SectionId}");
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
        DwgJsonExporter.WriteFile(doc, args[1], DwgJsonExportOptions.Default);
        Console.WriteLine($"{doc.Version}  sections={doc.Sections.Count}  ->  {args[1]}");
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
        Console.WriteLine($"{args[2]}: {bytes.Length:N0} bytes ->  {args[3]}");
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
        var preview = doc.Preview;
        if (preview is null || !preview.HasImage)
        {
            Console.Error.WriteLine("error: this drawing has no thumbnail preview.");
            return ExitParseError;
        }

        var extension = preview.ImageKind switch
        {
            DwgPreviewImageKind.Png => ".png",
            DwgPreviewImageKind.Wmf => ".wmf",
            _ => ".bmp",
        };
        var target = args[2];
        if (!Path.HasExtension(target) && target.EndsWith(Path.DirectorySeparatorChar) is false && target.EndsWith('/') is false)
        {
            target += extension;
        }

        File.WriteAllBytes(target, preview.ImageBytes);
        Console.WriteLine($"Preview: {preview.ImageKind} {preview.ImageBytes.Length:N0} bytes ->  {target}");
        return ExitOk;
    }
}
