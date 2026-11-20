using System.Security.Cryptography;
using System.Text;
using DwgIO.Output;
using IOScene;
using IOScene.Formats;

namespace DwgIO.Scene;

/// <summary>
/// Exports a drawing to the mesh and viewer formats the IOScene toolchain writes: Wavefront OBJ with an
/// adjacent MTL library and binary glTF (GLB). Both go through the IOScene scene document, so colors, line
/// and point topologies and instancing behave exactly as in the scene JSON export.
/// </summary>
public static class DwgSceneExport
{
    /// <summary>Converts the drawing and writes it to a Wavefront OBJ file with an adjacent MTL library
    /// (<c>&lt;objPath&gt;</c> with <c>.mtl</c> instead of its extension).</summary>
    /// <param name="document">The drawing, for example from <see cref="DwgDocument.Open(string, DwgReaderOptions?)"/>.</param>
    /// <param name="objPath">Path to the OBJ file; the MTL library is written beside it.</param>
    /// <param name="options">Conversion options; null uses the defaults.</param>
    /// <returns>The conversion report. <see cref="DwgSceneResult.Document"/> is the intermediate scene.</returns>
    public static DwgSceneResult SaveObj(DwgDocument document, string objPath, DwgSceneOptions? options = null)
    {
        var result = DwgSceneReader.Read(document, OptionsFor(document, objPath, options));
        WriteObj(result.Document, objPath);
        return result;
    }

    /// <summary>Writes a scene to a Wavefront OBJ file with an adjacent MTL library
    /// (<c>&lt;objPath&gt;</c> with <c>.mtl</c> instead of its extension). The pair is replaced so that a
    /// failure at any step leaves the previous pair untouched: the old MTL is first renamed aside, the new
    /// MTL moves in, and the OBJ — which names that MTL in its <c>mtllib</c> — moves in last, after which
    /// the old MTL backup is dropped.</summary>
    /// <param name="scene">The scene to export.</param>
    /// <param name="objPath">Path to the OBJ file.</param>
    public static void WriteObj(SceneDocument scene, string objPath)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrEmpty(objPath);
        string fullPath = Path.GetFullPath(objPath);
        string directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        if (Path.GetExtension(fullPath).Equals(".mtl", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The OBJ destination must differ from its material file.", nameof(objPath));
        }

        string objName = Path.GetFileName(fullPath);
        string mtlName = Path.ChangeExtension(objName, ".mtl");
        if (mtlName.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is ':' or '#' or '\\'))
        {
            mtlName = $"materials-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(objName)))}.mtl";
        }

        string mtlPath = Path.Combine(directory, mtlName);
        string? objTemp = null;
        string? mtlTemp = null;
        string? mtlBackup = null;
        bool mtlReplaced = false;
        try
        {
            objTemp = AtomicOutput.CreateTemporary(fullPath);
            mtlTemp = AtomicOutput.CreateTemporary(mtlPath);
            // Both temporary files are written first; the OBJ names the MTL by its stable name.
            using (var objStream = new FileStream(objTemp, FileMode.Truncate, FileAccess.Write, FileShare.None))
            using (var objWriter = new StreamWriter(objStream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            using (var mtlStream = new FileStream(mtlTemp, FileMode.Truncate, FileAccess.Write, FileShare.None))
            using (var mtlWriter = new StreamWriter(mtlStream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                ObjSceneWriter.Write(scene, objWriter, mtlWriter, mtlName);
            }

            // The old MTL steps aside first, so it can step back in when either move fails.
            if (File.Exists(mtlPath))
            {
                mtlBackup = Path.Combine(directory, $".dwgio-{Guid.NewGuid():N}.bak");
                File.Move(mtlPath, mtlBackup);
            }

            File.Move(mtlTemp, mtlPath, overwrite: true);
            mtlReplaced = true;
            File.Move(objTemp, fullPath, overwrite: true);
            if (mtlBackup is not null)
            {
                AtomicOutput.TryDelete(mtlBackup);
            }
        }
        catch
        {
            // Restore the previous pair: the old MTL comes back, and a fresh export leaves no MTL behind.
            if (mtlBackup is not null && File.Exists(mtlBackup))
            {
                File.Move(mtlBackup, mtlPath, overwrite: true);
            }
            else if (mtlReplaced && File.Exists(mtlPath))
            {
                File.Delete(mtlPath);
            }

            throw;
        }
        finally
        {
            AtomicOutput.TryDelete(objTemp);
            AtomicOutput.TryDelete(mtlTemp);
        }
    }

    /// <summary>Converts the drawing and writes it to a binary glTF (GLB) file.</summary>
    /// <param name="document">The drawing, for example from <see cref="DwgDocument.Open(string, DwgReaderOptions?)"/>.</param>
    /// <param name="glbPath">Path to the GLB file.</param>
    /// <param name="options">Conversion options; null uses the defaults.</param>
    /// <returns>The conversion report. <see cref="DwgSceneResult.Document"/> is the intermediate scene.</returns>
    public static DwgSceneResult SaveGlb(DwgDocument document, string glbPath, DwgSceneOptions? options = null)
    {
        var result = DwgSceneReader.Read(document, OptionsFor(document, glbPath, options));
        WriteGlb(result.Document, glbPath);
        return result;
    }

    /// <summary>Writes a scene to a binary glTF (GLB) file.</summary>
    /// <param name="scene">The scene to export.</param>
    /// <param name="glbPath">Path to the GLB file.</param>
    public static void WriteGlb(SceneDocument scene, string glbPath)
    {
        ArgumentNullException.ThrowIfNull(scene);
        AtomicOutput.Write(glbPath, stream => GlbSceneWriter.Write(scene, stream));
    }

    private static DwgSceneOptions OptionsFor(DwgDocument document, string path, DwgSceneOptions? options)
    {
        if (options?.ModelKey is { Length: > 0 })
        {
            return options;
        }

        // The file name doubles as the model key; a document from memory falls back to the export file name.
        return new DwgSceneOptions
        {
            LengthUnit = options?.LengthUnit ?? "m",
            ModelKey = Path.GetFileName(path),
        };
    }
}
