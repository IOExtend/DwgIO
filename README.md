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

## Install

Install the command line tool:

```sh
dotnet tool install --global DwgIO.Cli
```

and add the library to a .NET project:

```sh
dotnet add package DwgIO
```

The library targets .NET 8 and .NET 10. It uses managed code without native dependencies or Windows-only APIs and supports Windows, Linux and macOS. Tests currently run on Windows.

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

Save the embedded thumbnail preview. Without an extension, `.png`, `.wmf` or `.bmp` is added to match the image:

```sh
dwgio preview drawing.dwg thumbnail
```

Errors go to standard error.

### Exit codes

| Code | Meaning |
| --- | --- |
| 0 | The command succeeded. |
| 1 | The command line is wrong. |
| 2 | The input file does not exist. |
| 3 | The DWG version is not supported yet (R13 to R2000, R2007) or not recognised. |
| 4 | The file could not be parsed, the drawing has no thumbnail preview (`preview`), or an unexpected error occurred. |

## Library

Open a drawing, list its sections and read one of them:

```csharp
using System;
using DwgIO;

var doc = DwgDocument.Open("drawing.dwg");

Console.WriteLine($"{doc.Version.Release} ({doc.Version.Magic})");

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
| AutoCAD 2007 container (AC1021) | planned |
| R13 to R2000 container (AC1012 to AC1015) | planned |

## Supported versions

| Magic | Release | Container | Status |
| --- | --- | --- | --- |
| AC1012 | R13 | flat section locators | planned |
| AC1014 | R14 | flat section locators | planned |
| AC1015 | R2000 | flat section locators | planned |
| AC1018 | R2004 | paged, compressed sections | done |
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
