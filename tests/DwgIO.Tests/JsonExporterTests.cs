using System.Text.Json;
using DwgIO.Json;
using DwgIO.Model;
using Xunit;

namespace DwgIO.Tests;

public class JsonExporterTests
{
    private static DwgDocument CreateDocument(params DwgEntity[] entities) =>
        new(
            "synthetic.dwg",
            new DwgFileHeader
            {
                Version = new DwgVersionInfo
                {
                    Magic = "AC1024",
                    Release = "R2010",
                    Family = DwgContainerFamily.R2004Family,
                    Year = 2010,
                },
                MaintenanceVersion = 0,
                PreviewOffset = 0,
                Codepage = 30,
            },
            entities);

    private static DwgEntity CreateEntity(double[] geometry) =>
        new()
        {
            Handle = 0x2A,
            TypeCode = 0x04,
            TypeName = "LINE",
            Kind = DwgEntityKind.Line,
            Geometry = geometry,
        };

    [Fact]
    public void Write_NonFiniteGeometry_WritesNullAndRecordsDiagnostic()
    {
        var document = CreateDocument(CreateEntity(
            [double.NaN, double.PositiveInfinity, 1.5, double.NegativeInfinity, -2.25, 0.0]));

        using var stream = new MemoryStream();
        DwgJsonExporter.Write(document, stream, DwgJsonExportOptions.Default);

        using var json = JsonDocument.Parse(stream.ToArray());
        var geometry = json.RootElement.GetProperty("entities")[0].GetProperty("geometry");
        Assert.Equal(JsonValueKind.Null, geometry[0].ValueKind);
        Assert.Equal(JsonValueKind.Null, geometry[1].ValueKind);
        Assert.Equal(1.5, geometry[2].GetDouble());
        Assert.Equal(JsonValueKind.Null, geometry[3].ValueKind);
        Assert.Equal(-2.25, geometry[4].GetDouble());
        Assert.Equal(0.0, geometry[5].GetDouble());
        Assert.Contains(document.Diagnostics, d => d.StartsWith("entity 2A:", StringComparison.Ordinal));
    }

    [Fact]
    public void Write_FiniteGeometry_RoundsWithoutDiagnostics()
    {
        var document = CreateDocument(CreateEntity([1.12345678, 2.0, 3.0, 4.0, 5.0, 6.0]));

        using var stream = new MemoryStream();
        DwgJsonExporter.Write(document, stream, DwgJsonExportOptions.Default);

        using var json = JsonDocument.Parse(stream.ToArray());
        var geometry = json.RootElement.GetProperty("entities")[0].GetProperty("geometry");
        Assert.Equal(1.123457, geometry[0].GetDouble());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void WriteFile_WritesFileAndLeavesNoTemporaryFile()
    {
        var document = CreateDocument(CreateEntity([1.0, 2.0, 3.0, 4.0, 5.0, 6.0]));
        var directory = Path.Combine(Path.GetTempPath(), "dwgio-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var target = Path.Combine(directory, "out.json");

            DwgJsonExporter.WriteFile(document, target, DwgJsonExportOptions.Default);

            Assert.True(File.Exists(target));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            using var json = JsonDocument.Parse(File.ReadAllBytes(target));
            Assert.Equal(1, json.RootElement.GetProperty("entityCount").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteFile_Overwrite_PreservesExistingAccessRules()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var document = CreateDocument(CreateEntity([1.0, 2.0, 3.0, 4.0, 5.0, 6.0]));
        var directory = Path.Combine(Path.GetTempPath(), "dwgio-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var target = Path.Combine(directory, "out.json");
            DwgJsonExporter.WriteFile(document, target, DwgJsonExportOptions.Default);

            var acl = new FileInfo(target).GetAccessControl();
            acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                System.Security.Principal.WindowsIdentity.GetCurrent().User!,
                System.Security.AccessControl.FileSystemRights.FullControl,
                System.Security.AccessControl.AccessControlType.Allow));
            new FileInfo(target).SetAccessControl(acl);
            Assert.True(new FileInfo(target).GetAccessControl().AreAccessRulesProtected);

            DwgJsonExporter.WriteFile(document, target, DwgJsonExportOptions.Default);

            Assert.True(new FileInfo(target).GetAccessControl().AreAccessRulesProtected);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            using var json = JsonDocument.Parse(File.ReadAllBytes(target));
            Assert.Equal(1, json.RootElement.GetProperty("entityCount").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteFile_FailedMove_LeavesNoTargetFileOrTemporaryFile()
    {
        var document = CreateDocument(CreateEntity([1.0, 2.0, 3.0, 4.0, 5.0, 6.0]));
        var directory = Path.Combine(Path.GetTempPath(), "dwgio-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var target = Path.Combine(directory, "out.json");
            // A directory at the target path makes the final move fail after the temporary file was written.
            Directory.CreateDirectory(target);

            var exception = Record.Exception(() => DwgJsonExporter.WriteFile(document, target, DwgJsonExportOptions.Default));

            Assert.NotNull(exception);
            Assert.True(exception is IOException or UnauthorizedAccessException, exception.ToString());
            Assert.False(File.Exists(target));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
