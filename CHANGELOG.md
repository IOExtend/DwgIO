# Changelog

All notable changes are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project follows [Semantic Versioning](https://semver.org/); versions before 1.0 may change the
API between minor releases.

## [Unreleased]

### Added
- `DwgIO.Scene.Adapter` provides scene conversion with only DwgIO and IOScene as dependencies.
  The `DwgIO.Scene` package retains its export API and forwards the existing conversion types.
- JSON and SVG export options expose a `ReportDiagnostic` callback for export warnings.
  Document diagnostics cover DWG reads.
- CI builds the native engine and scene adapter with `BuildNativeCoreOnly` enabled.
- CI runs the tests on Linux and macOS as well as Windows (both target frameworks), replacing the Linux
  build-only job (#34).
- `test-files/public`: the folder for committed redistributable test drawings, with the origin and licence
  recorded next to each file; the fixture search includes it, so a committed drawing turns the skipped
  fixture tests into real runs in CI (#33, infrastructure — the drawings themselves follow).
- `DwgDocument.Open(Stream, documentName?, options?)`: a drawing can now come from a stream (a network
  handle, an archive entry, an already-open file). The stream is read to memory once, so the results are
  identical to `Open(path)` on the same bytes; the unsupported-version message names the source (#37, first
  step — reading data pages on demand remains open).
- `AcDb:RevHistory`, `AcDb:AuxHeader` and `AcDb:Template` parsing exposes the revision values, auxiliary
  header and template description on `DwgDocument`, in JSON and in `dwgio inspect`. The undecoded
  `AcDb:AppInfoHistory` section is exposed as decompressed bytes, with its size in JSON and CLI output (#36).
- Fixed object type names are available in `DwgObjectInfo.TypeName`, `dwgio inspect` and the JSON
  `objects.byTypeName` field. Existing `objects.byType` keys are preserved (#20, fixed types only).
- `dwgio --version` and `--help` (and `-v`/`-h`) print the version and the usage with exit code 0; an unknown
  option still exits 1 (#18).
- `dwgio entities <file.dwg> [--type LINE,CIRCLE] [--json]` lists decoded entities one line each or as a JSON
  array, with the type filter validated against the known type names (#19). The JSON export writes to standard
  output when the target is `-` (#19).
- `DwgIO.Scene` package: converts a drawing into a neutral [IOScene](https://www.nuget.org/packages/IOScene) scene
  document, the shared scene model of the IOExtend tools. One element per decoded entity with the handle as
  `SourceId`, line and point geometries scaled to metres (`DwgSceneOptions.LengthUnit`), ACI colours on the
  placements and a conversion report (`DwgSceneResult`). The scene passes the IOScene structural validation and
  round-trips through the IOScene JSON format.
- `dwgio scene <file.dwg> <out.json> [--unit mm|cm|m|ft|in]` writes the scene document.
- `DwgSceneExport.SaveObj`/`SaveGlb` write a drawing to Wavefront OBJ (with an adjacent MTL colour library,
  written as a pair) and binary glTF (GLB) through the IOScene writers; `dwgio drawing.dwg out.obj` and
  `dwgio drawing.dwg out.glb` dispatch by extension.
- `DwgSvgExporter` and `dwgio svg <file.dwg> <out.svg> [--scale n]` draw model space to SVG (#32): LINE,
  CIRCLE, ARC and POINT in the top view with the y axis up, in their ACI colours, inside a view box that
  contains every exported entity. Polylines, curves and text follow as the core decodes them.
- `DwgViewerExport.SaveViewer` and `dwgio drawing.dwg out.html` write a standalone interactive 3D HTML viewer
  (the IOViewer WebGL runtime embedded with the IOScene scene document; no server, no external scripts).
- `dwgio inspect` is written through a `TextWriter`, so the report is testable directly.

### Fixed
- `DwgAppInfo.Product` is filled from the product XML's `name` attribute ("AutoCAD", "AutoCAD LT",
  "Teigha(R)", ...), so `dwgio inspect` and the JSON export name the product instead of an empty string (#35).
- `dwgio preview` exits with code 5 when the drawing has no thumbnail, distinct from the parse-error code, and
  reports a mismatch between the requested output extension and the image kind on standard error instead of
  keeping both silent (#14).
- Encrypted sections (encryption flag 1) are reported in `Diagnostics` and by `dwgio inspect` (the section
  table marks them) instead of being skipped silently; decrypting them remains out of scope (#38).
- Object stream: after a gap (a zero run or a broken record) the object walk resynchronises at the next
  handle-map offset instead of stopping, and every early stop or skipped gap is reported in `Diagnostics`; the
  inventory-versus-handle-map difference is reported as well, and `DwgHandleMap.Parse` reports truncated blocks
  and drops negative offsets with a diagnostic (#13).
- Fixture tests skip drawings of an unsupported version and count them in the skip reason, so one AC1021 file
  in the fixture folder does not fail every test (#17).
- Entity colour: the DXF 62 index takes the low 9 bits of the stored colour value, so BYLAYER (256) is reported
  as 256 and not as 0 (which also means BYBLOCK) (#40). The JSON export writes `colorIndex` for BYBLOCK and
  BYLAYER so they are distinguishable.
- `dwgio` formats counts and byte sizes invariantly; the output no longer follows the machine's digit grouping
  (#15).
- `dwgio inspect` always prints the diagnostic total and the first diagnostics, whether or not any entity was
  decoded (#16).
- Packed 16-bit and 32-bit integers in their full-width form are read little-endian. They were read with their bytes
  reversed, so entity records that store a colour, extended data size or reactor count in that form were decoded from
  the wrong bit position and failed or gave NaN or absurd coordinates (#7).

## [0.4.0] - 2026-11-11

### Added
- Entity decoding: common entity data (extended data chain, graphics preview, entity mode, reactors, colour,
  linetype scale, visual styles, invisibility, lineweight) and geometry bodies for LINE, CIRCLE, ARC and POINT.
- `DwgDocument.Entities`: decoded entities with geometry; exposed in the JSON export
  and in `dwgio inspect` (entity-kind counts and sample coordinates).
- Bit-packed primitives: packed 32- and 64-bit integers, packed doubles, doubles stored as a patch of a default
  value, unpacked doubles and single bits; corrected the 2-bit tags of packed shorts (tag 11 = 256) and packed
  integers (tag 00 = full 32-bit value).
- Banner and logo redrawn.
- Contributing guide, security policy, code of conduct, issue and pull request templates, Dependabot configuration
  and a description of the private test fixtures (`docs/test-fixtures.md`).
- Documentation site built with docfx: getting started, public API and the generated API reference.

### Changed (breaking)
- Only the types listed in `docs/public-api.md` are public. The container reader, cipher, checksums, decompressor,
  bit reader and section parsers under `DwgIO.Internal` are now `internal`, and so are the `Parse` methods of the
  header and handle map types and `DwgVersionInfo.FromMagic`.
- The file header, container header, handle map and object types moved to `DwgIO.Model`, and the JSON exporter and
  its options to `DwgIO.Json`. `DwgDocument.CommonHeader` is now `FileHeader` and `DwgDocument.Header` is now
  `ContainerHeader`.
- Property names of the header, handle map and section types and the matching JSON export keys changed to clearer
  names; the current names are listed in `docs/public-api.md` and the generated API reference.
- The JSON `type` value and `DwgEntity.TypeName` use AutoCAD entity names for 3D faces, 3D solids, 2D and 3D
  polylines and vertices, and table names for block records, layers, text styles and linetypes.

### Changed
- The NuGet package icon lives in each project folder (`src/DwgIO/icon.png`, `src/DwgIO.Cli/icon.png`).
- Tests that need real drawings are reported as skipped, with a reason, when no drawing is available, instead of
  passing without running.
- `THIRD-PARTY-NOTICES.md` carries the Autodesk trademark notice and is included in the NuGet packages.
- README and changelog describe the file format in the project's own words.
- CLI help lists the `preview` command; error messages no longer cite external documents.
- Fixture tests look for drawings in `DWGIO_TEST_FILES` and in a `test_files` folder (each folder and one level of
  subfolders) and run on .NET 8 as well as .NET 10.
- Packages include Source Link.

### Fixed
- Crafted files can no longer cause huge allocations, integer overflow in bounds checks or crashes with exceptions
  other than `DwgException`: declared section sizes are checked against their page count, page size and the largest
  array before allocation; each page is limited to the section's page size; section buffers and decompressed pages of
  one document together are limited to 8 GiB; section-map entry and page counts are checked against the map before lists are sized; preview
  image ranges, page payload sizes and SummaryInfo dates are range-checked; pages past the section end are no longer
  decompressed; page checksums are computed only when `VerifyChecksums` is set.
- The decompressor no longer reads a stale buffer when the output grows during an overlapping back-reference, and it
  presizes its output only up to 1 MiB instead of trusting the size declared in the file.
- `DwgDocument.Open` and `ReadSection` report malformed data as `DwgException` (with the original exception as
  `InnerException`), and the best-effort section readers record such failures in `Diagnostics`.

### Known limitations
- Some entity bodies are not decoded yet; they are recorded in `DwgDocument.Diagnostics`.

## [0.3.0] - 2026-11-11

### Added
- Handle map parsing (`AcDb:Handles`): each block starts from an absolute handle/offset pair followed by
  variable-length deltas; a truncated tail is tolerated. `DwgDocument.HandleMap` and `Find(handle)`.
- Object stream inventory (`AcDb:AcDbObjects`): sequential walk of the object records
  (size, handle-stream bit count, type, handle, payload, CRC).
- Metadata parsers: `AcDb:AppInfo` (the AutoCAD 2007 and later layout: UTF-16 strings
  whose length includes the terminating NUL, build version from the ProductInformation XML), `AcDb:FileDepList`
  (features and file dependencies), `AcDb:Preview` (BMP, WMF and PNG extraction).
- AutoCAD codepage mapping (29 → 1251, 30 → 1252, 38 → 932, ...) for legacy 8-bit strings.
- `DwgDocument.Diagnostics`: best-effort parse failures are collected instead of
  silently swallowed.
- CLI: `dwgio preview <in> <out>` extracts the embedded thumbnail; `inspect` now shows
  summary info, app info, preview, file dependencies and the object-type histogram.

### Fixed
- The bit-level payload of an object record starts at the handle-stream bit count, after the size field;
  the sequential walk mis-read object types on R2010 and later files.

## [0.2.0] - 2026-11-11

### Added
- CRC verification: file-header CRC-32 validation (`R2004Header.HeaderCrcValid`)
  and data-page header and payload checksum verification via `DwgReaderOptions.VerifyChecksums`.
- `DwgSummaryInfo` parsing (`AcDb:SummaryInfo`): title, subject, author, keywords, comments,
  last saved by, revision, hyperlink base, custom properties, and Julian-date-based
  created/modified UTC timestamps. Surfaced in the JSON export and in `dwgio inspect`.
- Branding: pixel-art banner, logo and icon (red Dwg and sky-blue IO); `icon.png` is the NuGet package icon.

## [0.1.0] - 2026-11-11

### Added
- Version detection for DWG releases AC1012 (R13) through AC1032 (R2018).
- Container reader for the AutoCAD 2004 and later releases AC1018, AC1024, AC1027 and AC1032:
  XOR-masked file header (key stream from the Visual C++ `rand()` generator, seed 1), page map,
  section map, and data pages with XOR-masked headers.
- Decompression of section data with 1-based back-reference offsets, as AutoCAD-written files store them,
  overlapping back-references, and limits against decompression bombs.
- `DwgDocument` API: `Open`, `ReadSection`, version info, named-section catalog.
- CLI `dwgio`: `inspect` (version and section table), JSON export, section `extract`.
- Unit tests for the cipher, decompression and version detection, plus optional smoke tests that run on
  drawings in the folder named by `DWGIO_TEST_FILES`.
