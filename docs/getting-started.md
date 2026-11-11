---
title: Getting started
---

# Getting started

## Install

Publishing to nuget.org is not enabled yet. Until it is, build from source or take the packages attached to a tagged GitHub release. Once the packages are published:

```sh
dotnet tool install --global DwgIO.Cli
dotnet add package DwgIO
```

## Command line tool

```sh
dwgio inspect drawing.dwg                      # version, metadata, entities and section table
dwgio drawing.dwg drawing.json                 # metadata, section catalog and entities as JSON
dwgio extract drawing.dwg AcDb:Header out.bin  # decompressed bytes of one named section
dwgio preview drawing.dwg thumbnail            # embedded thumbnail; .png, .wmf or .bmp is added
```

Sections are named as stored in the drawing, for example `AcDb:Header`, `AcDb:Classes` or `AcDb:SummaryInfo`. Errors go to standard error.

| Exit code | Meaning |
| --- | --- |
| 0 | The command succeeded. |
| 1 | The command line is wrong. |
| 2 | The input file does not exist. |
| 3 | The DWG version is not supported yet or not recognised. |
| 4 | The file could not be parsed, the drawing has no thumbnail preview (`preview`), or an unexpected error occurred. |

## Library

```csharp
using System;
using DwgIO;

var doc = DwgDocument.Open("drawing.dwg");
Console.WriteLine(doc.Version);

foreach (var section in doc.Sections)
{
    Console.WriteLine(section.Name);
}

byte[] header = doc.ReadSection("AcDb:Header");

if (doc.SummaryInfo is { } summary)
{
    Console.WriteLine($"{summary.Title} by {summary.Author}");
}

foreach (var entity in doc.Entities)
{
    Console.WriteLine(entity);
}

foreach (var diagnostic in doc.Diagnostics)
{
    Console.WriteLine(diagnostic);
}
```

The supported surface is listed in [Public API](public-api.md); every type is in the [API reference](api/DwgIO.yml).

## Status

DwgIO is a preview and the API can change before 1.0.
