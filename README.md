<p align="center">
  <img src="docs/images/dwgio-banner.png" alt="DwgIO" width="640">
</p>

DwgIO is a .NET library and command line tool that reads Autodesk AutoCAD drawings (`.dwg`) directly from disk, without AutoCAD or the AutoCAD API, and exports their metadata and contents to JSON.

## What it does

- Detects the DWG version of a file, from AC1012 (R13) to AC1032 (R2018).
- Opens the container of AutoCAD 2004 and later drawings (AC1018, AC1024, AC1027, AC1032): file header, page map, section map and compressed data pages, with optional checksum verification.
- Reads named sections (`AcDb:Header`, `AcDb:AcDbObjects`, ...) as decompressed bytes.
- Parses drawing metadata: summary information with custom properties and dates, application information, file dependencies and the embedded thumbnail preview.
- Lists the objects of a drawing through its handle map and decodes the geometry of LINE, CIRCLE, ARC and POINT entities.
- Writes a JSON export of the metadata, the section catalog and the decoded entities.
- Converts a drawing into a neutral [IOScene](https://www.nuget.org/packages/IOScene) scene document (`DwgIO.Scene` package): geometry in metres, ACI entity colours, one element per entity with its handle.
- Exports the scene to Wavefront OBJ (with an MTL colour library) and binary glTF (GLB).
- Draws model space to SVG (`dwgio svg`): LINE, CIRCLE, ARC and POINT in the top view, in their ACI colours.
- Writes a standalone interactive 3D HTML viewer (`.html`): the WebGL viewer of [IOViewer](https://github.com/IOExtend/IOViewer) embedded with the scene, no server or external scripts.

## Install

Install the command line tool:

```sh
dotnet tool install --global DwgIO.Cli
```

and add the libraries to a .NET project:

```sh
dotnet add package DwgIO
dotnet add package DwgIO.Scene
```

The libraries target .NET 8 and .NET 10. They use managed code without native dependencies or Windows-only APIs and support Windows, Linux and macOS. Tests run on Windows, Linux and macOS.

## Command line

Print the version, metadata, object and entity summary and section table of a drawing:

```sh
dwgio inspect drawing.dwg
```

Export metadata, the section catalog and the decoded entities to JSON:

```sh
dwgio drawing.dwg drawing.json
```

Write the decompressed bytes of one named section:

```sh
dwgio extract drawing.dwg AcDb:Header header.bin
```

Save the embedded thumbnail preview. Without an extension, `.png`, `.wmf` or `.bmp` is added to match the image; a mismatching extension is reported on standard error, and a drawing without a thumbnail exits with code 5:

```sh
dwgio preview drawing.dwg thumbnail
```

Export a neutral IOScene scene document (geometry in metres, ACI colours, one element per decoded entity). `--unit` names the drawing unit the coordinates are scaled from — one of `mm`, `cm`, `m` (default), `ft`, `in`:

```sh
dwgio scene drawing.dwg drawing.scene.json --unit mm
```

The plain export command writes OBJ and GLB by output extension, through the same scene:

```sh
dwgio drawing.dwg drawing.obj   # Wavefront OBJ + drawing.mtl with the ACI colours
dwgio drawing.dwg drawing.glb   # binary glTF for viewers and web scenes
```

Draw model space to SVG (X to the right, Y up, ACI colours; `--scale` sets pixels per drawing unit):

```sh
dwgio svg drawing.dwg drawing.svg --scale 2
```

Open the drawing in a standalone interactive 3D HTML viewer:

```sh
dwgio drawing.dwg drawing.html
```

List the decoded entities, one line each, optionally filtered by type and as JSON:

```sh
dwgio entities drawing.dwg --type LINE,CIRCLE
dwgio entities drawing.dwg --json
```

The JSON export also writes to standard output when the target is `-`:

```sh
dwgio drawing.dwg - | jq .summaryInfo
```

`dwgio --version` prints the package version, `dwgio --help` the usage. Errors go to standard error.

### Exit codes

| Code | Meaning |
| --- | --- |
| 0 | The command succeeded. |
| 1 | The command line is wrong. |
| 2 | The input file does not exist. |
| 3 | The DWG version is not supported yet (R13 to R2000, R2007) or not recognised. |
| 4 | The file could not be parsed, or an unexpected error occurred. |
| 5 | The drawing has no thumbnail preview (`preview`). |

## Library

Open a drawing (from a path or from a stream — a network handle, an archive entry, an already-open file; the stream is read to memory once and `Open(Stream)` gives the same results as `Open(path)`), list its sections and read one of them:

```csharp
using System;
using DwgIO;

var doc = DwgDocument.Open("drawing.dwg");

Console.WriteLine($"{doc.Version.Release} ({doc.Version.Magic})");

// or from a stream:
// var doc = DwgDocument.Open(httpResponse.Content.ReadAsStream(), "remote.dwg");

foreach (var section in doc.Sections)
{
    Console.WriteLine($"{section.Name,-24} {section.Size,12} bytes  pages={section.PageCount}");
}

byte[] header = doc.ReadSection("AcDb:Header");

foreach (var entity in doc.Entities)
{
    Console.WriteLine(entity);
}
```

`DwgDocument.Open` throws `FileNotFoundException` for a missing file, `DwgUnsupportedVersionException` for a version whose container is not implemented yet, and `DwgException` for a damaged or truncated file. The library writes nothing to the console: parts of a drawing that could not be decoded are listed in `DwgDocument.Diagnostics`. `DwgReaderOptions` selects what is read and turns on checksum verification (`VerifyChecksums`).

The JSON export is `DwgJsonExporter` in the `DwgIO.Json` namespace. See [Getting started](docs/getting-started.md) and [Public API](docs/public-api.md) for the supported types.

The scene export is `DwgSceneReader` in the `DwgIO.Scene` package. It converts a drawing into an `IOScene.SceneDocument` — the neutral scene model of the IOExtend tools, shared with RvtIO and NwcIO:

```csharp
using DwgIO.Scene;

var scene = DwgSceneReader.Read("drawing.dwg", new DwgSceneOptions { LengthUnit = "mm" });
IOScene.SceneJson.Save(scene.Document, "drawing.scene.json");
```

Every coordinate is scaled to metres by `LengthUnit` (a DWG stores plain numbers, so the unit is a caller decision and `UnitAssumed` records it), each entity becomes an element with its handle as `SourceId`, and LINE, CIRCLE, ARC and POINT become line and point geometries with their ACI colour.

`DwgSceneExport` writes that scene out in the exchange formats: `SaveObj(document, "drawing.obj", options)` produces an OBJ with an adjacent MTL colour library (with rollback if replacement fails), `SaveGlb(document, "drawing.glb", options)` produces binary glTF. Both return the same `DwgSceneResult` report.

The documentation site with the API reference is at <https://ioextend.github.io/DwgIO/>. [![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/IOExtend/DwgIO) offers an automatically generated overview of the repository.

## Status

| Area | Status |
| --- | --- |
| Version detection (AC1012 to AC1032) | done |
| AutoCAD 2004 and later container (AC1018, AC1024, AC1027, AC1032): file header, page map, section map, data pages | done |
| Decompression of section data | done |
| Checksum verification (file header CRC-32, data page checksums) | done |
| `AcDb:SummaryInfo` parsing (metadata, custom properties, dates) | done |
| Reading named sections (`AcDb:Header`, `AcDb:AcDbObjects`, ...) | done |
| `AcDb:AppInfo`, `AcDb:FileDepList` and `AcDb:Preview` parsing | done |
| Handle map and object stream inventory | done |
| Entity decoding: LINE, CIRCLE, ARC and POINT geometry | done (partial) |
| IOScene scene export (`DwgIO.Scene`): entities, geometry, ACI colours | done (partial) |
| OBJ and GLB export through the scene | done (partial) |
| SVG export of model space (`dwgio svg`) | done (partial) |
| Standalone HTML viewer export through IOViewer | done (partial) |
| AutoCAD 2007 container (AC1021) | planned |
| R13 to R2000 container (AC1012 to AC1015) | planned |

## Supported versions

| Magic | Release | Container | Status |
| --- | --- | --- | --- |
| AC1012 | R13 | flat section locators | planned |
| AC1014 | R14 | flat section locators | planned |
| AC1015 | R2000 | flat section locators | planned |
| AC1018 | R2004 | paged, compressed sections | verified locally on real drawings |
| AC1021 | R2007 | Reed-Solomon coded pages | planned |
| AC1024 | R2010 | paged, compressed sections | done |
| AC1027 | R2013 | paged, compressed sections | done |
| AC1032 | R2018 | paged, compressed sections | done |

## Limitations

- Version 0.4.0 is a preview. The API can change before 1.0.
- Drawings saved as R13 to R2000 or R2007 are refused (`DwgUnsupportedVersionException`, exit code 3).
- Entity geometry is decoded for LINE, CIRCLE, ARC and POINT only. Other entities are listed as objects without geometry, and entity bodies that could not be decoded are recorded in `DwgDocument.Diagnostics`.
- There is no support for writing or modifying DWG files.

## Build from source and tests

Use the .NET SDK selected by `global.json`, which specifies 10.0.401 with `latestFeature` roll-forward.

From the repository root:

```sh
dotnet build DwgIO.slnx -c Release
dotnet test
```

Tests requiring real DWG files are skipped with a message unless `DWGIO_TEST_FILES` or a `test_files` folder provides private drawings. The repository includes no DWG drawings. See [test fixtures](docs/test-fixtures.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md). Release changes are recorded in [CHANGELOG.md](CHANGELOG.md).

## Security

Report vulnerabilities through GitHub private vulnerability reporting in the repository's Security tab. Do not open public issues for security vulnerabilities. See [SECURITY.md](SECURITY.md).

## License and trademarks

DwgIO is licensed under the [MIT License](LICENSE).

Autodesk, AutoCAD and DWG are registered trademarks or trademarks of Autodesk, Inc. DwgIO is not affiliated with, endorsed by or sponsored by Autodesk.

The file format is read by analysing files for interoperability. No Autodesk code or binaries are used or included.
