# Redistributable test drawings (#33)

This folder holds small DWG files whose licence allows redistribution, one per supported release
(AC1018, AC1024, AC1027, AC1032 — then AC1012 to AC1015 as the readers for those land). The fixture tests
run over every drawing in the fixture folders, so a drawing committed here turns its skipped tests into
real runs in CI on all three systems.

## How a drawing qualifies

Before committing a file, record here, next to the file name:

1. **Origin** — where the drawing came from, and who produced it.
2. **Licence** — why it may be redistributed in this MIT-licensed repository (own drawing, written
   permission of the author, a licence that allows redistribution, or a file created from scratch).
3. **Contents** — one line on what the drawing holds (entities, layers), so tests can be written against it.

When the licence is in doubt, the file stays out. The private-folder mechanism (`DWGIO_TEST_FILES`,
see `docs/test-fixtures.md`) remains the place for larger local test sets.

## The fixture search

`TestFiles` looks for drawings in the folder named by `DWGIO_TEST_FILES`, in a `test_files` folder and in
this `test-files/public` folder; one level of subfolders is searched too. Nothing else about the search
changes: unsupported versions are skipped and counted, and a missing drawing never fails a test.
