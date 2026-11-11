using System.Buffers.Binary;
using DwgIO.Model;

namespace DwgIO.Internal.Parsing;

/// <summary>
/// Parses the decompressed <c>AcDb:FileDepList</c> payload:
/// feature names followed by file dependency records
/// (u32-length 8-bit strings, no NUL).
/// </summary>
internal static class FileDepListParser
{
    /// <summary>Parses a FileDepList payload; <paramref name="codepage"/> decodes the strings.</summary>
    public static DwgFileDepList Parse(byte[] data, int codepage)
    {
        var cursor = new MetadataByteCursor(data, codepage);

        int featureCount = checked((int)cursor.ReadU32());
        if (featureCount > 4096)
        {
            throw new DwgException($"FileDepList declares {featureCount} features; file is malformed.");
        }

        var features = new List<string>(featureCount);
        for (int i = 0; i < featureCount; i++)
        {
            features.Add(cursor.ReadAnsiString32());
        }

        int fileCount = checked((int)cursor.ReadU32());
        if (fileCount > 65536)
        {
            throw new DwgException($"FileDepList declares {fileCount} files; file is malformed.");
        }

        var files = new List<DwgFileDependency>(fileCount);
        for (int i = 0; i < fileCount; i++)
        {
            var filename = cursor.ReadAnsiString32();
            var foundPath = cursor.ReadAnsiString32();
            var fingerprintGuid = cursor.ReadAnsiString32();
            var versionGuid = cursor.ReadAnsiString32();
            uint featureIndex = cursor.ReadU32();
            uint timestamp = cursor.ReadU32();
            uint fileSize = cursor.ReadU32();
            bool affectsGraphics = cursor.ReadU16() != 0;
            uint referenceCount = cursor.ReadU32();
            files.Add(new DwgFileDependency
            {
                Filename = filename,
                FoundPath = foundPath,
                FingerprintGuid = fingerprintGuid,
                VersionGuid = versionGuid,
                FeatureIndex = unchecked((int)featureIndex),
                Timestamp = unchecked((int)timestamp),
                FileSize = unchecked((int)fileSize),
                AffectsGraphics = affectsGraphics,
                ReferenceCount = unchecked((int)referenceCount),
            });
        }

        return new DwgFileDepList
        {
            Features = features,
            Files = files,
        };
    }

    /// <summary>Helper for callers that want the raw timestamp interpreted (Unix seconds).</summary>
    public static DateTimeOffset? AsUnixTime(int timestamp)
    {
        if (timestamp <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(timestamp);
    }
}
