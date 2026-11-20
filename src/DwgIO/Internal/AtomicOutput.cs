using System.Security.AccessControl;

namespace DwgIO.Internal;

internal static class AtomicOutput
{
    public static void Write(string path, Action<Stream> write)
    {
        string fullPath = Path.GetFullPath(path);
        string temporary = CreateTemporary(fullPath);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Truncate, FileAccess.Write, FileShare.None))
            {
                write(stream);
            }

            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    public static string CreateTemporary(string fullPath)
    {
        string directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        string tempPath = Path.Combine(directory, $".dwgio-{Guid.NewGuid():N}.tmp");

        // Preserve the destination's access rights across the atomic replace: a fresh
        // temporary file inherits the directory ACL, which may be more permissive than
        // the file being overwritten. Copy the existing rights onto the temporary file
        // before any JSON bytes are written; the same-directory move preserves them.
        // Reading or applying the rights is mandatory: failures abort the export
        // before the destination is touched, instead of silently widening access.
        FileSecurity? savedWindowsAcl = null;
        UnixFileMode? savedUnixMode = null;
        bool destinationExists = File.Exists(fullPath);
        if (destinationExists)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    savedWindowsAcl = new FileInfo(fullPath).GetAccessControl();
                }
                catch (PlatformNotSupportedException)
                {
                    savedWindowsAcl = null;
                }
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                try
                {
                    savedUnixMode = File.GetUnixFileMode(fullPath);
                }
                catch (PlatformNotSupportedException)
                {
                    savedUnixMode = null;
                }
            }
        }

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }

            ApplyTempSecurity(tempPath, savedWindowsAcl, savedUnixMode, destinationExists);
            return tempPath;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void ApplyTempSecurity(
        string filePath,
        FileSecurity? windowsAcl,
        UnixFileMode? unixMode,
        bool destinationExists)
    {
        if (!destinationExists)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            if (windowsAcl is null)
            {
                return;
            }

            // Copy only the DACL and its protection flag. Persisting the whole
            // FileSecurity (owner/group/SACL) requires SeSecurityPrivilege and,
            // when unmodified, persists nothing — the temporary file would keep
            // the directory's inherited rules.
            new FileInfo(filePath).SetAccessControl(CloneDacl(windowsAcl));
            return;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            if (unixMode.HasValue)
            {
                File.SetUnixFileMode(filePath, unixMode.Value);
            }
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static FileSecurity CloneDacl(FileSecurity source)
    {
        var clone = new FileSecurity();
        clone.SetAccessRuleProtection(source.AreAccessRulesProtected, preserveInheritance: false);
        foreach (FileSystemAccessRule rule in source.GetAccessRules(includeExplicit: true, includeInherited: false, targetType: typeof(System.Security.Principal.SecurityIdentifier)))
        {
            clone.AddAccessRule(rule);
        }

        return clone;
    }

    public static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup: the original export failure is the error worth reporting.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup: the original export failure is the error worth reporting.
        }
    }
}
