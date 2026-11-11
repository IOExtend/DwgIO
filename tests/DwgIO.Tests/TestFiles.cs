namespace DwgIO.Tests;

/// <summary>Where the tests find private DWG files that are not part of the repository. The folder is named by the environment
/// variable DWGIO_TEST_FILES; when it is not set, the folder test_files in the repository root (or in an ancestor of the test
/// binary) is used. Files may lie directly in the folder or in one level of subfolders. A test that needs a file which is not
/// there is reported as skipped with the reason, never passed.</summary>
internal static class TestFiles
{
    public const string EnvironmentVariable = "DWGIO_TEST_FILES";

    /// <summary>Folders searched for fixtures, the one named by the environment variable first.</summary>
    public static IEnumerable<string> Roots()
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } configured && Directory.Exists(configured))
            yield return configured;
        for (var directory = AppContext.BaseDirectory; directory != null; directory = Path.GetDirectoryName(directory))
        {
            string candidate = Path.Combine(directory, "test_files");
            if (Directory.Exists(candidate)) yield return candidate;
        }
    }

    /// <summary>Every *.dwg in the fixture folders (root plus one level of subfolders, so a mistyped variable cannot sweep a
    /// whole tree), sorted ordinally, or a <see cref="SkipException"/> when there is none.</summary>
    public static List<string> DwgFiles()
    {
        var files = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string root in Roots())
        {
            foreach (string file in Directory.EnumerateFiles(root, "*.dwg")) files.Add(file);
            foreach (string folder in Directory.EnumerateDirectories(root))
                foreach (string file in Directory.EnumerateFiles(folder, "*.dwg")) files.Add(file);
        }
        if (files.Count == 0) throw Missing("*.dwg files");
        return [.. files];
    }

    public static SkipException Missing(string what) =>
        new($"Private fixture '{what.Replace('\\', '/')}' is not available. Set {EnvironmentVariable} to the folder that holds the private test files.");
}
