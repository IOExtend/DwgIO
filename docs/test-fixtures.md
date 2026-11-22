# DWG test fixtures

Most tests build their input in the test itself (cipher, decompression, checksums, bit reading, version detection,
handle map, object stream and metadata parsing) and run everywhere. The fixture tests in `DwgFixtureTests` need real
drawings. The committed [public primitives](https://github.com/IOExtend/DwgIO/blob/main/test-files/public/README.md) cover R2010, R2013 and R2018
in CI on Windows, Linux and macOS. Private fixtures still belong in the ignored test folder or outside the
repository.

## Where the drawings are looked for

The required `PublicDrawing_DecodesAuthoredPrimitives` theory reads only the three committed drawings, copied
to a dedicated subfolder of the test output at build time. This works even when `--artifacts-path` places the
assembly outside the checkout.

The optional fixture checks take drawings from all of these folders together:

- the folder named by the environment variable `DWGIO_TEST_FILES` (ignored when the folder does not exist);
- the committed redistributable drawings of [test-files/public](https://github.com/IOExtend/DwgIO/blob/main/test-files/public/README.md), with their
  origin and licence recorded next to each file;
- a folder `test_files` in the repository root (or in any parent of the test binary folder).

Every `*.dwg` file directly in such a folder or in one level of subfolders is used, whatever its name. Drawings of the
supported releases (AC1018, AC1024, AC1027, AC1032) are the useful ones. AC1018 still needs a public fixture.
Drawings of an unsupported version
(AC1012 to AC1015, AC1021) are ignored by the fixture tests and counted in the skip reason, so one file of a
planned release does not fail every test.

## Without drawings

Optional fixture checks are reported as **skipped with a reason** when no eligible drawing is available.
If no drawing is found, the reason names `DWGIO_TEST_FILES`. The attributes `[SkippableFact]` and
`[SkippableTheory]` (`SkippableFact.cs`) report `SkipException` as a skip, because xunit 2.9 has no runtime skip
of its own. The public geometry theory requires all three committed drawings and fails if any is missing
from the test output.

No test uses a fixed absolute path.

## Running with drawings

```sh
DWGIO_TEST_FILES=<folder> dotnet test
```

In PowerShell: `$env:DWGIO_TEST_FILES = '<folder>'; dotnet test`.

Do not commit private drawings or put their file names, object counts or other details into tests,
comments or documentation.
