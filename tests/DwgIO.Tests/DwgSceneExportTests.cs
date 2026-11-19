using DwgIO.Model;
using DwgIO.Scene;
using IOScene.Formats;
using Xunit;
using Vector3d = IOScene.Vector3d;

namespace DwgIO.Tests;

public class DwgSceneExportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dwgio-tests-" + Guid.NewGuid().ToString("N"));

    public DwgSceneExportTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

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

    private static DwgDocument SampleDrawing() => CreateDocument(
        new DwgEntity
        {
            Handle = 0x2A,
            TypeCode = 0x13,
            TypeName = "LINE",
            Kind = DwgEntityKind.Line,
            Geometry = [1.0, 2.0, 0.0, 11.0, 2.0, 0.0],
            ColorIndex = 1,
        },
        new DwgEntity
        {
            Handle = 0x2B,
            TypeCode = 0x12,
            TypeName = "CIRCLE",
            Kind = DwgEntityKind.Circle,
            Geometry = [6.0, 2.0, 0.0, 5.0],
            ColorIndex = 40,
        },
        new DwgEntity
        {
            Handle = 0x2C,
            TypeCode = 0x1B,
            TypeName = "POINT",
            Kind = DwgEntityKind.Point,
            Geometry = [20.0, 2.0, 0.0],
        });

    [Fact]
    public void SaveObj_WritesObjWithMtlLibrary()
    {
        string objPath = Path.Combine(_directory, "drawing.obj");

        var result = DwgSceneExport.SaveObj(SampleDrawing(), objPath);

        Assert.Equal(3, result.ElementCount);
        Assert.True(File.Exists(objPath));
        Assert.True(File.Exists(Path.Combine(_directory, "drawing.mtl")));
        string obj = File.ReadAllText(objPath);
        Assert.Contains("mtllib drawing.mtl", obj, StringComparison.Ordinal);
        Assert.Contains("usemtl material_1", obj, StringComparison.Ordinal);
        Assert.Contains("v 1 2 0", obj, StringComparison.Ordinal);
        Assert.Contains("l ", obj, StringComparison.Ordinal);
        Assert.Contains("p ", obj, StringComparison.Ordinal);
        string mtl = File.ReadAllText(Path.Combine(_directory, "drawing.mtl"));
        Assert.Contains("newmtl material_1", mtl, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveObj_WritesLineAndPointRecords()
    {
        string objPath = Path.Combine(_directory, "drawing.obj");

        DwgSceneExport.SaveObj(SampleDrawing(), objPath);

        // The OBJ preview reader of IOScene.Formats reads faces only, so the line and point records are asserted
        // on the text: one segment for the LINE, 64 for the tessellated CIRCLE, one point for the POINT.
        string[] objLines = File.ReadAllLines(objPath);
        Assert.Equal(65, objLines.Count(l => l.StartsWith("l ", StringComparison.Ordinal)));
        Assert.Equal(1, objLines.Count(l => l.StartsWith("p ", StringComparison.Ordinal)));
        Assert.Contains(objLines, l => l == "v 1 2 0");
        Assert.Contains(objLines, l => l == "v 20 2 0");
    }

    [Fact]
    public void SaveObj_ReplacesExistingFiles()
    {
        string objPath = Path.Combine(_directory, "drawing.obj");
        File.WriteAllText(objPath, "stale");
        string mtlPath = Path.Combine(_directory, "drawing.mtl");
        File.WriteAllText(mtlPath, "stale");

        DwgSceneExport.SaveObj(SampleDrawing(), objPath);

        Assert.DoesNotContain("stale", File.ReadAllText(objPath), StringComparison.Ordinal);
        Assert.DoesNotContain("stale", File.ReadAllText(mtlPath), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(_directory, ".dwgio-*"));
    }

    [Fact]
    public void SaveGlb_RoundTripsThroughGlbSceneReader()
    {
        string glbPath = Path.Combine(_directory, "drawing.glb");

        var result = DwgSceneExport.SaveGlb(SampleDrawing(), glbPath);

        Assert.Equal(3, result.ElementCount);
        var loaded = GlbSceneReader.Load(glbPath);
        Assert.Equal(3, loaded.Elements.Count);
        Assert.Equal(3, loaded.Instances.Count);
        // The GLB reader turns instance colours into materials: red for the LINE (ACI 1), gold for the CIRCLE (ACI 40).
        Assert.Contains(loaded.Materials, m => m.Color.R == 255 && m.Color.G == 0 && m.Color.B == 0);
        Assert.Contains(loaded.Materials, m => m.Color.R == 255 && m.Color.G == 191 && m.Color.B == 0);
        var lineInstance = Assert.Single(loaded.Instances, i => loaded.Geometries.Single(g => g.Id == i.GeometryId).Topology == IOScene.SceneTopology.Lines && loaded.Geometries.Single(g => g.Id == i.GeometryId).Vertices.Count == 2);
        Assert.NotNull(lineInstance);
    }

    [Fact]
    public void SaveGlb_ScalesByLengthUnit()
    {
        string glbPath = Path.Combine(_directory, "drawing.glb");
        var document = CreateDocument(new DwgEntity
        {
            Handle = 0x2A,
            TypeCode = 0x13,
            TypeName = "LINE",
            Kind = DwgEntityKind.Line,
            Geometry = [1000.0, 0.0, 0.0, 2000.0, 0.0, 0.0],
        });

        DwgSceneExport.SaveGlb(document, glbPath, new DwgSceneOptions { LengthUnit = "mm" });

        var loaded = GlbSceneReader.Load(glbPath);
        var geometry = Assert.Single(loaded.Geometries);
        Assert.Equal(new Vector3d(1.0, 0.0, 0.0), geometry.Vertices[0]);
    }
}
