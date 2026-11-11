---
title: DwgIO
---

# DwgIO

DwgIO is a .NET library and command line tool that reads Autodesk AutoCAD drawings (`.dwg`) directly from disk, without AutoCAD or the AutoCAD API, and exports their metadata and contents to JSON.

- Detects the DWG version of a file, from AC1012 (R13) to AC1032 (R2018).
- Opens the container of AutoCAD 2004 and later drawings and reads named sections as decompressed bytes.
- Parses drawing metadata: summary information, application information, file dependencies and the thumbnail preview.
- Decodes the geometry of LINE, CIRCLE, ARC and POINT entities.
- Writes a JSON export of the metadata, the section catalog and the decoded entities.

The library targets .NET 8 and .NET 10, uses managed code only, and supports Windows, Linux and macOS.

## Where to go next

- [Getting started](getting-started.md): install the tool or the package and read a first drawing.
- [Public API](public-api.md): the supported surface of the library.
- [API reference](api/DwgIO.yml): every public type, generated from the XML documentation.
- [Source on GitHub](https://github.com/IOExtend/DwgIO) and [DeepWiki](https://deepwiki.com/IOExtend/DwgIO) for an automatically generated overview of the repository.

DwgIO is not affiliated with, endorsed by or sponsored by Autodesk. Autodesk and AutoCAD are trademarks of Autodesk, Inc.
