# Changelog

All notable changes are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project follows [Semantic Versioning](https://semver.org/); versions before 1.0 may change the
API between minor releases.

## [Unreleased]

### Added
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
