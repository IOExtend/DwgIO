using DwgIO.Model;
using DwgIO.Scene;
using IOScene;
using Xunit;
using Vector3d = IOScene.Vector3d;

namespace DwgIO.Tests;

public class DwgSceneReaderTests
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

    private static DwgEntity Entity(ulong handle, DwgEntityKind kind, string typeName, double[] geometry, int colorIndex = 256) =>
        new()
        {
            Handle = handle,
            TypeCode = 0x13,
            TypeName = typeName,
            Kind = kind,
            Geometry = geometry,
            ColorIndex = colorIndex,
        };

    [Fact]
    public void Read_ConvertsLineAndPointToSceneGeometries()
    {
        var document = CreateDocument(
            Entity(0x2A, DwgEntityKind.Line, "LINE", [1.0, 2.0, 3.0, 4.0, 5.0, 6.0]),
            Entity(0x2B, DwgEntityKind.Point, "POINT", [7.0, 8.0, 9.0]));

        var result = DwgSceneReader.Read(document, new DwgSceneOptions { ModelKey = "synthetic" });

        var scene = result.Document;
        var model = Assert.Single(scene.Models);
        Assert.Equal("synthetic", model.Key);
        Assert.Equal("dwg", model.SourceFormat);
        Assert.Equal("m", model.SourceLengthUnit);
        Assert.Equal(2, result.ElementCount);
        Assert.Equal(2, result.ElementsWithGeometry);

        var line = scene.Elements[0];
        Assert.Equal("2A", line.SourceId);
        Assert.Equal("LINE 2A", line.Name);
        Assert.Equal("LINE", line.Category);
        Assert.Equal(SceneNodeKind.Element, line.Kind);

        var lineInstance = Assert.Single(scene.Instances, i => i.ElementId == line.Id);
        var lineGeometry = scene.Geometries.Single(g => g.Id == lineInstance.GeometryId);
        Assert.Equal(SceneTopology.Lines, lineGeometry.Topology);
        Assert.Equal(2, lineGeometry.Vertices.Count);
        Assert.Equal(new Vector3d(1.0, 2.0, 3.0), lineGeometry.Vertices[0]);
        Assert.Equal(new Vector3d(4.0, 5.0, 6.0), lineGeometry.Vertices[1]);

        var point = scene.Elements[1];
        var pointInstance = Assert.Single(scene.Instances, i => i.ElementId == point.Id);
        var pointGeometry = scene.Geometries.Single(g => g.Id == pointInstance.GeometryId);
        Assert.Equal(SceneTopology.Points, pointGeometry.Topology);
        Assert.Equal(new Vector3d(7.0, 8.0, 9.0), Assert.Single(pointGeometry.Vertices));
    }

    [Fact]
    public void Read_TessellatesCircleAndArc()
    {
        var document = CreateDocument(
            Entity(0x31, DwgEntityKind.Circle, "CIRCLE", [10.0, 20.0, 0.0, 5.0]),
            Entity(0x32, DwgEntityKind.Arc, "ARC", [10.0, 20.0, 0.0, 5.0, 0.0, Math.PI]));

        var result = DwgSceneReader.Read(document, new DwgSceneOptions { ModelKey = "synthetic" });

        var circleGeometry = result.Document.Geometries[0];
        Assert.Equal(SceneTopology.Lines, circleGeometry.Topology);
        Assert.Equal(64, circleGeometry.Vertices.Count);
        // Vertex 0 sits at angle 0, so one radius to the right of the centre.
        Assert.Equal(15.0, circleGeometry.Vertices[0].X, 9);
        Assert.Equal(20.0, circleGeometry.Vertices[0].Y, 9);

        var arcGeometry = result.Document.Geometries[1];
        Assert.True(arcGeometry.Vertices.Count >= 9);
        Assert.Equal(15.0, arcGeometry.Vertices[0].X, 9);
        var last = arcGeometry.Vertices[^1];
        Assert.Equal(5.0, last.X, 9); // centre - radius at PI
        Assert.Equal(20.0, last.Y, 9);
    }

    [Fact]
    public void Read_MapsAciColourOntoInstances()
    {
        var document = CreateDocument(
            Entity(0x2A, DwgEntityKind.Line, "LINE", [0, 0, 0, 1, 0, 0], colorIndex: 1),
            Entity(0x2B, DwgEntityKind.Line, "LINE", [0, 0, 0, 1, 0, 0], colorIndex: 256),
            Entity(0x2C, DwgEntityKind.Line, "LINE", [0, 0, 0, 1, 0, 0], colorIndex: 0));

        var result = DwgSceneReader.Read(document, new DwgSceneOptions { ModelKey = "synthetic" });

        Assert.Equal(new RgbaColor(255, 0, 0), result.Document.Instances[0].Color);
        // BYLAYER (256) and BYBLOCK (0) have no colour of their own until layers are decoded.
        Assert.Null(result.Document.Instances[1].Color);
        Assert.Null(result.Document.Instances[2].Color);
    }

    [Fact]
    public void Read_UnitOptionScalesCoordinates()
    {
        var document = CreateDocument(Entity(0x2A, DwgEntityKind.Line, "LINE", [1000.0, 0.0, 0.0, 2000.0, 0.0, 0.0]));

        var result = DwgSceneReader.Read(document, new DwgSceneOptions { ModelKey = "synthetic", LengthUnit = "mm" });

        Assert.Equal("mm", result.LengthUnit);
        Assert.Equal(0.001, result.MetresPerUnit);
        Assert.Equal(1.0, result.Document.Geometries[0].Vertices[0].X, 9);
        Assert.Equal(2.0, result.Document.Geometries[0].Vertices[1].X, 9);
    }

    [Fact]
    public void Read_UnknownUnit_Throws()
    {
        var document = CreateDocument();

        Assert.Throws<ArgumentException>(() => DwgSceneReader.Read(document, new DwgSceneOptions { ModelKey = "synthetic", LengthUnit = "parsec" }));
        Assert.False(DwgSceneReader.IsSupportedUnit("parsec"));
    }

    [Fact]
    public void Read_MemoryDocumentWithoutModelKey_Throws()
    {
        var document = CreateDocument();

        Assert.Throws<ArgumentException>(() => DwgSceneReader.Read(document, new DwgSceneOptions()));
    }

    [Fact]
    public void Read_ForwardsDocumentDiagnostics()
    {
        var document = CreateDocument();
        document.ReportDiagnostic("AcDb:Handles: unexpected end");

        var result = DwgSceneReader.Read(document, new DwgSceneOptions { ModelKey = "synthetic" });

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(SceneSeverity.Warning, diagnostic.Severity);
        Assert.Equal("document", diagnostic.Code);
        Assert.Contains("AcDb:Handles", diagnostic.Message);
    }

    [Fact]
    public void Read_EntityWithoutGeometry_CountsAndDiagnoses()
    {
        var document = CreateDocument(
            Entity(0x2A, DwgEntityKind.Unknown, "TEXT", []),
            Entity(0x2B, DwgEntityKind.Unknown, "TEXT", []),
            Entity(0x2C, DwgEntityKind.Unknown, "INSERT", []));

        var result = DwgSceneReader.Read(document, new DwgSceneOptions { ModelKey = "synthetic" });

        Assert.Equal(3, result.ElementCount);
        Assert.Equal(0, result.ElementsWithGeometry);
        Assert.Equal(3, result.EntitiesWithoutGeometry);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "entities-without-geometry");
        Assert.Contains("TEXT 2", diagnostic.Message);
    }

    [Fact]
    public void Read_NonFiniteGeometry_DropsGeometryAndReports()
    {
        var document = CreateDocument(Entity(0x2A, DwgEntityKind.Line, "LINE", [0.0, 0.0, 0.0, double.NaN, 0.0, 0.0]));

        var result = DwgSceneReader.Read(document, new DwgSceneOptions { ModelKey = "synthetic" });

        Assert.Empty(result.Document.Geometries);
        Assert.Empty(result.Document.Instances);
        Assert.Equal(1, result.ElementCount);
        Assert.Equal(0, result.ElementsWithGeometry);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "non-finite-geometry");
        Assert.Equal(SceneSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void Read_ScenePassesTheIosceneContract()
    {
        var document = CreateDocument(
            Entity(0x2A, DwgEntityKind.Line, "LINE", [1, 2, 3, 4, 5, 6], colorIndex: 1),
            Entity(0x2B, DwgEntityKind.Arc, "ARC", [0, 0, 0, 2, 0.5, 1.5], colorIndex: 40),
            Entity(0x2C, DwgEntityKind.Point, "POINT", [0, 0, 0]));

        var result = DwgSceneReader.Read(document, new DwgSceneOptions { ModelKey = "synthetic" });

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == SceneSeverity.Error);
        Assert.DoesNotContain(result.Document.Validate(), d => d.Severity == SceneSeverity.Error);
    }

    [Fact]
    public void Read_SerializedScene_IsAnIosceneDocument()
    {
        var document = CreateDocument(Entity(0x2A, DwgEntityKind.Line, "LINE", [1, 2, 3, 4, 5, 6]));

        var result = DwgSceneReader.Read(document, new DwgSceneOptions { ModelKey = "synthetic" });
        string json = SceneJson.Write(result.Document);

        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("ioscene", parsed.RootElement.GetProperty("Kind").GetString());
        Assert.Equal(1, parsed.RootElement.GetProperty("Format").GetInt32());

        var roundTrip = SceneJson.Read(json);
        Assert.Equal(result.Document.Models[0].Key, roundTrip.Models[0].Key);
        Assert.Equal(result.Document.Instances.Count, roundTrip.Instances.Count);
    }
}
