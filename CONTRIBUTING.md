# Contributing to DwgIO

## Reporting bugs

Open a [GitHub issue](https://github.com/IOExtend/DwgIO/issues) with:

- The AutoCAD version that saved the drawing (or the version magic, such as `AC1032`, that `dwgio inspect` prints).
- The dwgio version.
- The command line or API call used, with confidential values removed.
- A minimal description of how to reproduce the problem.
- Expected and actual results, including relevant error messages.

Do not attach drawings if they contain confidential data.
A minimal description is better than sharing a confidential drawing.

Report security issues through GitHub private vulnerability reporting in
the Security tab. Do not open public issues for security vulnerabilities.

## Proposing changes

Open an issue first for larger changes so the scope and approach can be
discussed before implementation. Keep changes focused on the agreed problem.

## Original work only

Contributions must be your own original work. Do not copy code or text from
other DWG implementations or other projects. Describe format
facts as they are observed in DWG files, in your own words, in code comments,
documentation and commit messages.

## Development setup

Install the .NET SDK specified by `global.json`. It currently specifies
10.0.401, allows roll-forward to the latest feature band, and excludes
prerelease SDKs.

From the repository root, run:

```sh
dotnet build DwgIO.slnx -c Release
dotnet test
```

The library is in `src/DwgIO`, the CLI in `src/DwgIO.Cli`, and tests in
`tests/DwgIO.Tests`.

Tests that need real drawings are skipped with a message when no drawing is
available. Set `DWGIO_TEST_FILES` to the folder containing your private
drawings, or place them in a `test_files` folder, to run them. The repository
ships no drawings. See [test fixtures](docs/test-fixtures.md).

## Code conventions

- Follow the surrounding code and repository formatting settings.
- Keep nullable reference types enabled.
- Warnings are treated as errors.
- Write short English comments that explain why.
- Document public APIs with XML documentation comments.
- Do not write console output from the library. Collect diagnostics in
  `DwgDocument.Diagnostics`. The library must remain silent.

## Validating output

The JSON export and `dwgio inspect` output must not change unintentionally.

Before changing parsing behaviour, export the same drawings with the existing
code and keep the results outside the repository. Export those drawings again
after the change and compare the before and after output, investigate
differences, and explain intentional changes in the pull request.

## Pull requests

Include:

- A description of the problem and the change.
- Tests for behaviour changes, including relevant errors and edge cases.
- The build and test commands run, their results, and any skipped checks.
- Before and after output comparison results when the output is affected.

Do not add drawings, customer data, vendor code or binaries to the
repository. Keep private test drawings outside version control.

Write commit messages in the imperative mood, for example,
"Fix handle map offset for truncated blocks".

## Releases

Push a tag matching the package version, such as `v0.4.0`, to run `release.yml`.
The workflow checks that the tag matches the version, builds and tests the solution, and attaches the
binary archives, NuGet packages, symbols, a source archive and checksums to a GitHub release, then publishes the
NuGet packages to nuget.org through trusted publishing. The release notes are the section of `CHANGELOG.md` for the
version, so add that section before the tag.

## Licensing contributions

Contributions are licensed under the MIT license. By submitting a
contribution, you confirm that you have the right to contribute it under
that license.

## Questions

Ask questions in GitHub Discussions or repository issues.
