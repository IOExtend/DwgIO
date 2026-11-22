# Redistributable test drawings (#33)

These small DWG drawings exercise R2010, R2013 and R2018 in CI on Windows, Linux and macOS.

| Drawing | DWG release | SHA-256 of the committed file |
| --- | --- | --- |
| `primitives-ac1024.dwg` | AC1024 (2010) | `e28711b8b875d7d8203e0eef784304e39c95d12e44d28b236fa0765c700c372c` |
| `primitives-ac1027.dwg` | AC1027 (2013) | `1f04c46e0af453898f941b3242843bf07576a55f9066ec52db5830b331dc28c3` |
| `primitives-ac1032.dwg` | AC1032 (2018) | `73443dc0d95e9081aeb16ddb9c7f223ccb96541368c1f08ef4c5318aba3bd112` |

**Origin and licence:** Each drawing was created from scratch for DwgIO #33 and is distributed
under [DwgIO's MIT licence](../../LICENSE). The independent writer is
[ACadSharp 3.8.35](https://www.nuget.org/packages/ACadSharp/3.8.35) (MIT).
No vendor or third-party drawing, writer implementation, or assembly is included.

**Contents:** Model space, default layer `0`: one line from (1, 2, 3) to (4, 6, 8), one arc
centred at (-3, 7, 2) with radius 5 from π/6 to π/2 radians, one circle centred at (7, -2, 1)
with radius 4, and one point at (-5, -6, 9). These values are authored inputs, not values
captured from DwgIO. The xUnit theory checks the decoded values and version for every file.

**Remaining coverage:** AC1018 (R2004) still needs a compatible public fixture. An ACadSharp 3.8.35
R2004 candidate reopened in that writer's reader, but DwgIO did not decode the four authored entities
and failed its AppInfo and handle-map fixture checks. It is not committed.

### Regenerating equivalent drawings

Outside this repository, create a disposable .NET 10 console project and add the pinned writer:

```sh
dotnet new console --framework net10.0 -o DwgFixtureGenerator
cd DwgFixtureGenerator
dotnet add package ACadSharp --version 3.8.35
```

Replace `Program.cs` with this original fixture recipe:

```csharp
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;

foreach (var version in new[] { ACadVersion.AC1024, ACadVersion.AC1027, ACadVersion.AC1032 })
{
    var drawing = new CadDocument(version);
    drawing.Entities.Add(new Line(new XYZ(1, 2, 3), new XYZ(4, 6, 8)));
    drawing.Entities.Add(new Arc(new XYZ(-3, 7, 2), 5, Math.PI / 6, Math.PI / 2));
    drawing.Entities.Add(new Circle(new XYZ(7, -2, 1), 4));
    drawing.Entities.Add(new Point(new XYZ(-5, -6, 9)));
    var name = $"primitives-{version.ToString().ToLowerInvariant()}.dwg";
    DwgWriter.Write(name, drawing);
}
```

Run `dotnet run -c Release`. Writer metadata such as timestamps can change the bytes, so the hashes
above identify the committed drawings, not all equivalent regenerations. Validate the version and
geometry with DwgIO before replacing a file, then update the recorded hash. These fixtures exercise
the independent writer's output, not every DWG writer or entity kind.

## How a drawing qualifies

Before committing a file, record here, next to the file name:

1. **Origin** — where the drawing came from, and who produced it.
2. **Licence** — why it may be redistributed in this MIT-licensed repository (own drawing, written
   permission of the author, a licence that allows redistribution, or a file created from scratch).
3. **Contents** — one line on what the drawing holds (entities, layers), so tests can be written against it.

When the licence is in doubt, the file stays out. The private-folder mechanism (`DWGIO_TEST_FILES`,
see `docs/test-fixtures.md`) remains the place for larger local test sets.

## The fixture search

For optional fixture checks, `TestFiles` looks in `DWGIO_TEST_FILES`, ancestor `test_files` and
`test-files/public` folders, and one level of subfolders. Unsupported versions are skipped and counted;
checks with no eligible drawing are skipped. The required `PublicDrawing_DecodesAuthoredPrimitives` theory
instead reads the three committed files copied into the test output at build time. A missing copy fails
that theory even when no private drawings are configured.
