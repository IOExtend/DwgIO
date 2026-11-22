namespace DwgIO.Tests;

/// <summary>Resolves required public DWG fixtures from the test output and finds optional inputs from
/// DWGIO_TEST_FILES and fixture folders in ancestors of the test binary.</summary>
internal static class TestFiles
{
    public const string EnvironmentVariable = "DWGIO_TEST_FILES";

    /// <summary>Folders searched for fixtures: the one named by the environment variable first, then the
    /// committed redistributable drawings of test-files/public, then a test_files folder in the repository
    /// root (or in an ancestor of the test binary).</summary>
    public static IEnumerable<string> Roots()
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } configured && Directory.Exists(configured))
            yield return configured;
        for (var directory = AppContext.BaseDirectory; directory != null; directory = Path.GetDirectoryName(directory))
        {
            string candidate = Path.Combine(directory, "test-files", "public");
            if (Directory.Exists(candidate)) yield return candidate;
            candidate = Path.Combine(directory, "test_files");
            if (Directory.Exists(candidate)) yield return candidate;
        }
    }

    /// <summary>Resolve the build's fixture copy, never a private file with the same name.</summary>
    public static string PublicDrawing(string fileName)
    {
        // Keep these copies outside the fixture roots searched by DwgFiles.
        string path = Path.Combine(AppContext.BaseDirectory, "PublicFixtures", fileName);
        if (File.Exists(path)) return path;
        throw new FileNotFoundException($"Required public DWG fixture '{fileName}' is missing from the test output.", path);
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
