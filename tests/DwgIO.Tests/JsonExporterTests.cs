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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteFile_Overwrite_PreservesExistingAccessRules(bool denyAclChanges)
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
            if (denyAclChanges)
            {
                acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    System.Security.Principal.WindowsIdentity.GetCurrent().User!,
                    System.Security.AccessControl.FileSystemRights.ChangePermissions,
                    System.Security.AccessControl.AccessControlType.Deny));
                // OWNER RIGHTS suppresses the owner's implicit permission to change the ACL.
                acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    new System.Security.Principal.SecurityIdentifier("S-1-3-4"),
                    System.Security.AccessControl.FileSystemRights.ReadPermissions,
                    System.Security.AccessControl.AccessControlType.Allow));
            }

            new FileInfo(target).SetAccessControl(acl);
            var expectedAcl = new FileInfo(target).GetAccessControl().GetSecurityDescriptorSddlForm(
                System.Security.AccessControl.AccessControlSections.Access);

            DwgJsonExporter.WriteFile(document, target, DwgJsonExportOptions.Default);

            Assert.Equal(expectedAcl, new FileInfo(target).GetAccessControl().GetSecurityDescriptorSddlForm(
                System.Security.AccessControl.AccessControlSections.Access));
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
    public void WriteFile_UnreadableAcl_AbortsWithoutReplacing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var document = CreateDocument(CreateEntity([1.0, 2.0, 3.0, 4.0, 5.0, 6.0]));
        var directory = Path.Combine(Path.GetTempPath(), "dwgio-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "out.json");
        File.WriteAllText(target, "KEEP ORIGINAL");
        var fileInfo = new FileInfo(target);

        var deny = new System.Security.AccessControl.FileSecurity();
        deny.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        deny.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            user,
            System.Security.AccessControl.FileSystemRights.ReadPermissions,
            System.Security.AccessControl.AccessControlType.Deny));
        deny.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            user,
            System.Security.AccessControl.FileSystemRights.FullControl,
            System.Security.AccessControl.AccessControlType.Allow));
        deny.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            new System.Security.Principal.SecurityIdentifier("S-1-3-4"),
            System.Security.AccessControl.FileSystemRights.ChangePermissions,
            System.Security.AccessControl.AccessControlType.Allow));
        fileInfo.SetAccessControl(deny);

        try
        {
            bool readDenied;
            try
            {
                fileInfo.GetAccessControl();
                readDenied = false;
            }
            catch (UnauthorizedAccessException)
            {
                readDenied = true;
            }

            Assert.True(readDenied, "probe invalid: reading the ACL was not denied");

            var exception = Record.Exception(() => DwgJsonExporter.WriteFile(document, target, DwgJsonExportOptions.Default));

            Assert.NotNull(exception);
            Assert.True(exception is UnauthorizedAccessException or IOException, exception.ToString());
            // READ_CONTROL is denied, so contents cannot be opened for reading
            // (GENERIC_READ includes READ_CONTROL); the directory-reported length
            // still proves the original 13-byte file was not replaced.
            Assert.Equal(13, new FileInfo(target).Length);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            // The deny ACL blocks READ_CONTROL, so it cannot be restored in-process;
            // deleting only needs DELETE, which FullControl still allows.
            try
            {
                File.Delete(target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

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
