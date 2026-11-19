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
    /// (<c>&lt;objPath&gt;</c> with <c>.mtl</c> instead of its extension).</summary>
    /// <param name="scene">The scene to export.</param>
    /// <param name="objPath">Path to the OBJ file.</param>
    public static void WriteObj(SceneDocument scene, string objPath)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrEmpty(objPath);
        string fullPath = Path.GetFullPath(objPath);
        string directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        string mtlName = Path.ChangeExtension(Path.GetFileName(fullPath), ".mtl");
        string mtlPath = Path.Combine(directory, mtlName);
        string objTemp = Path.Combine(directory, $".dwgio-{Guid.NewGuid():N}.obj");
        string mtlTemp = Path.Combine(directory, $".dwgio-{Guid.NewGuid():N}.mtl");
        try
        {
            // OBJ and MTL are written as a pair: both temporary files are written first, then moved in, so a
            // failure leaves the previous pair in place.
            using (var objStream = new FileStream(objTemp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var objWriter = new StreamWriter(objStream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            using (var mtlStream = new FileStream(mtlTemp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var mtlWriter = new StreamWriter(mtlStream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                ObjSceneWriter.Write(scene, objWriter, mtlWriter, mtlName);
            }

            File.Move(mtlTemp, mtlPath, overwrite: true);
            File.Move(objTemp, fullPath, overwrite: true);
        }
        catch
        {
            TryDelete(objTemp);
            TryDelete(mtlTemp);
            throw;
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
        GlbSceneWriter.Save(result.Document, glbPath);
        return result;
    }

    /// <summary>Writes a scene to a binary glTF (GLB) file.</summary>
    /// <param name="scene">The scene to export.</param>
    /// <param name="glbPath">Path to the GLB file.</param>
    public static void WriteGlb(SceneDocument scene, string glbPath)
    {
        ArgumentNullException.ThrowIfNull(scene);
        GlbSceneWriter.Save(scene, glbPath);
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

    private static void TryDelete(string path)
    {
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
