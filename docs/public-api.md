---
title: Public API
---

# Public API

The `DwgIO` package is the supported surface listed here. Everything else (the container reader, the cipher, the
checksums, the decompressor, the bit reader and the section parsers under `DwgIO.Internal`) is `internal`. It can
change in any release and is visible only to the test project through `InternalsVisibleTo`.

## Reading a file (`DwgIO`)

| Type | Use |
|---|---|
| `DwgDocument` | Opens a drawing (`Open`) and gives its version, file and container headers, section catalog, metadata, handle map, object inventory, entities and `Diagnostics`. `ReadSection(name)` returns the decompressed bytes of one named section. |
| `DwgReaderOptions` | What `Open` reads: `ReadSectionMap`, `ReadSummaryInfo`, `ReadMetadata`, `ReadObjects`, `ReadEntities` (all on by default) and `VerifyChecksums` (off by default). |
| `DwgException` | Thrown for a damaged or truncated file, and by `ReadSection` for an unknown section name, a document opened without its section map, or a checksum mismatch when `VerifyChecksums` is on. |
| `DwgUnsupportedVersionException` | Thrown when the version is not recognised or its container is not implemented yet (R13 to R2000, R2007). Derives from `DwgException`. |

`DwgDocument.Open` throws `FileNotFoundException` when the file does not exist.

## Model (`DwgIO.Model`)

| Type | Use |
|---|---|
| `DwgVersionInfo`, `DwgContainerFamily` | The release identified from the version magic (`Magic`, `Release`, `Year`, `Family`). |
| `DwgFileHeader` | The 128-byte prefix shared by every version: `Version`, `MaintenanceVersion`, `PreviewOffset`, `Codepage`, `CodepageName`. |
| `DwgContainerHeader` | The decrypted header of AutoCAD 2004 and later files: page and section map locations, `PageCount`, `GapCount`, `StoredCrc32` and `HeaderCrcValid`. |
| `DwgSectionInfo` | One named section: `Name`, `Size`, `PageCount`, `MaxPageSize`, `Compression`, `Encryption`, `SectionId`. |
| `DwgSummaryInfo` | Drawing properties (title, author, keywords, ...), custom properties and the created and modified dates. |
| `DwgAppInfo` | The application that saved the drawing. |
| `DwgFileDepList`, `DwgFileDependency` | External files the drawing refers to (fonts, images, references). |
| `DwgPreview`, `DwgPreviewImageKind` | The embedded thumbnail and its format (BMP, PNG or WMF). |
| `DwgHandleMap`, `DwgHandleMapEntry` | The handle to offset index of the object stream; `Find(handle)` looks up one object. |
| `DwgObjectInfo`, `DwgHandle` | One record of the object stream: offset, size, type code and handle. |
| `DwgEntity`, `DwgEntityKind` | A decoded entity: handle, type, colour, linetype scale and, for LINE, CIRCLE, ARC and POINT, its `Geometry`. |

## JSON export (`DwgIO.Json`)

| Type | Use |
|---|---|
| `DwgJsonExporter` | `Write(document, stream, options)` writes an opened `DwgDocument` to a stream; `WriteFile(document, path, options)` writes it to a new file. |
| `DwgJsonExportOptions` | `Indented` (default true), `IncludeSectionContents` (embeds the decompressed bytes of sections as base64; default false) and `SectionsToInclude` (names of the sections to embed; empty means every readable section). `Default` holds the default values. |

```csharp
using DwgIO;
using DwgIO.Json;

var doc = DwgDocument.Open("drawing.dwg");
DwgJsonExporter.WriteFile(doc, "drawing.json", DwgJsonExportOptions.Default);
```

The export has the objects `file`, `version`, `containerHeader`, `summaryInfo`, `appInfo`, `preview`, `fileDepList`
and `objects`, the numbers `pageCount` and `entityCount`, the `entities` array and the `sections` array. An object whose data the drawing does not hold is left
out.

## Diagnostics

The library never writes to the console. Parts of a drawing that could not be decoded are listed in
`DwgDocument.Diagnostics` (section name and message) and the rest of the drawing is still read.

`dwgio inspect` prints the first three of them.
