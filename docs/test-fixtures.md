# Private test fixtures

Most tests build their input in the test itself (cipher, decompression, checksums, bit reading, version detection,
handle map, object stream and metadata parsing) and run everywhere. The fixture tests in `DwgFixtureTests` need real
drawings, which cannot be published. The repository ships no drawings, and `test_files/` is ignored by git.

## Where the drawings are looked for

The fixture tests find their input through one helper, `TestFiles` in the test project. Drawings are taken from all
of these folders together:

- the folder named by the environment variable `DWGIO_TEST_FILES` (ignored when the folder does not exist);
- a folder `test_files` in the repository root (or in any parent of the test binary folder).

Every `*.dwg` file directly in such a folder or in one level of subfolders is used, whatever its name. Drawings of the
supported releases (AC1018, AC1024, AC1027, AC1032) are the useful ones.

## Without drawings

When no drawing is found, the fixture tests are reported as **skipped with a reason** that names `DWGIO_TEST_FILES`.
They never pass silently and never fail for lack of a file. The attributes `[SkippableFact]` and `[SkippableTheory]`
(`SkippableFact.cs`) report a test that throws `SkipException` as skipped, because xunit 2.9 has no runtime skip of its
own; `TestFiles` throws it when no drawing is available.

No test uses a fixed absolute path.

## Running with drawings

```sh
DWGIO_TEST_FILES=<folder> dotnet test
```

In PowerShell: `$env:DWGIO_TEST_FILES = '<folder>'; dotnet test`.

Do not commit drawings, and do not put file names, object counts or other details of private drawings into tests,
comments or documentation.
