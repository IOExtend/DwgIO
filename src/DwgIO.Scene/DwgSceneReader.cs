using System.Globalization;
using System.Reflection;
using DwgIO.Model;
using IOScene;
using Vector3d = IOScene.Vector3d;
using RgbaColor = IOScene.RgbaColor;

namespace DwgIO.Scene;

/// <summary>Converts an AutoCAD drawing, or a <see cref="DwgDocument"/> read by DwgIO, into a <see cref="SceneDocument"/>.</summary>
/// <remarks>
/// The scene has one <see cref="SceneModel"/>: <c>Key</c> and <c>Name</c> are <see cref="DwgSceneOptions.ModelKey"/>,
/// else the file name; <c>SourceFormat</c> is <c>dwg</c>; <c>Application</c> is the producer from
/// <c>AcDb:AppInfo</c>; <c>CreatedAt</c> is the summary-info creation time in UTC. The drawing's entities become
/// flat scene elements with the handle as <see cref="SceneElement.SourceId"/>; LINE, CIRCLE, ARC and POINT become
/// line and point geometries, tessellated for the round kinds. Every coordinate is scaled to metres by
/// <see cref="DwgSceneOptions.LengthUnit"/>; the drawing itself names no unit, so the scene records the choice.
/// </remarks>
public static class DwgSceneReader
{
    private static readonly string Producer = "DwgIO.Scene "
        + typeof(DwgSceneReader).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    // A full circle tessellated into 64 segments (5.6 degrees); an arc gets one segment per 5 degrees, at least 8.
    private const int CircleSegments = 64;
    private const double ArcRadiansPerSegment = 5.0 * Math.PI / 180.0;
    private const int MinArcSegments = 8;

    /// <summary>Reads the drawing at <paramref name="path"/> with <see cref="DwgDocument.Open(string, DwgReaderOptions?)"/> and converts it.</summary>
    /// <param name="path">The drawing file.</param>
    /// <param name="options">Conversion options; null uses the defaults.</param>
    /// <returns>The scene with a report of the conversion.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    public static DwgSceneResult Read(string path, DwgSceneOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Convert(DwgDocument.Open(path), options?.ModelKey ?? Path.GetFileName(path), path, options ?? new DwgSceneOptions());
    }

    /// <summary>
    /// Converts <paramref name="document"/>, which has no file name, so <see cref="DwgSceneOptions.ModelKey"/> is
    /// required. Give a document opened with the default <see cref="DwgReaderOptions"/>; a document read with
    /// <see cref="DwgReaderOptions.ReadEntities"/> off has no entities, and the scene holds the drawing without geometry.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="options">Conversion options with <see cref="DwgSceneOptions.ModelKey"/>.</param>
    /// <returns>The scene with a report of the conversion.</returns>
    /// <exception cref="ArgumentException"><see cref="DwgSceneOptions.ModelKey"/> is null or empty.</exception>
    public static DwgSceneResult Read(DwgDocument document, DwgSceneOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.ModelKey))
        {
            throw new ArgumentException("DwgSceneOptions.ModelKey must not be empty; a document converted from memory has no file name.", nameof(options));
        }

        return Convert(document, options.ModelKey, null, options);
    }

    private static DwgSceneResult Convert(DwgDocument document, string key, string? path, DwgSceneOptions options)
    {
        double metresPerUnit = MetresPerUnit(options.LengthUnit);
        var scene = new SceneDocument { Title = key };
        var diagnostics = new List<SceneDiagnostic>();

        scene.Models.Add(new SceneModel
        {
            Id = 1,
            Key = key,
            Name = key,
            Path = path,
            SourceFormat = "dwg",
            Producer = Producer,
            SourceLengthUnit = options.LengthUnit,
            Application = document.AppInfo is { Name.Length: > 0 } appInfo ? appInfo.Name : null,
            CreatedAt = document.SummaryInfo?.CreatedUtc?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        });

        foreach (string message in document.Diagnostics)
        {
            // Findings from the read: the scene is usable but something in the drawing was skipped.
            diagnostics.Add(new SceneDiagnostic(SceneSeverity.Warning, "document", $"{key}: {message}"));
        }

        int elementCount = 0;
        int elementsWithGeometry = 0;
        var withoutGeometry = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int nonFinite = 0;
        int geometryId = 0;

        foreach (var entity in document.Entities)
        {
            elementCount++;
            var element = new SceneElement
            {
                Id = elementCount,
                ModelId = 1,
                SourceId = entity.Handle.ToString("X", CultureInfo.InvariantCulture),
                Name = $"{entity.TypeName} {entity.Handle:X}",
                Category = entity.TypeName,
                TypeName = entity.TypeName,
            };
            scene.Elements.Add(element);

            Vector3d[]? vertices = entity.Kind switch
            {
                DwgEntityKind.Line when entity.Geometry is { Length: 6 } g => LineVertices(g, metresPerUnit),
                DwgEntityKind.Circle when entity.Geometry is { Length: 4 } g => CircleVertices(g, metresPerUnit),
                DwgEntityKind.Arc when entity.Geometry is { Length: 6 } g => ArcVertices(g, metresPerUnit),
                DwgEntityKind.Point when entity.Geometry is { Length: 3 } g => PointVertices(g, metresPerUnit),
                _ => null,
            };
            if (vertices is null)
            {
                if (entity.Kind == DwgEntityKind.Unknown || entity.Geometry is null)
                {
                    withoutGeometry[entity.TypeName] = withoutGeometry.TryGetValue(entity.TypeName, out int n) ? n + 1 : 1;
                }

                continue;
            }

            if (!vertices.All(IsFinite))
            {
                // The IOScene contract forbids non-finite coordinates; the core JSON export writes them as null,
                // the scene drops the geometry and reports it.
                nonFinite++;
                continue;
            }

            elementsWithGeometry++;
            geometryId++;
            bool isPoint = entity.Kind == DwgEntityKind.Point;
            // The contract wants whole pairs for non-indexed lines; polylines share their joints through indices.
            IReadOnlyList<int> indices = entity.Kind switch
            {
                DwgEntityKind.Circle => SharedIndices(CircleSegments, closed: true),
                DwgEntityKind.Arc => SharedIndices(vertices!.Length, closed: false),
                _ => [],
            };
            var geometry = new SceneGeometry
            {
                Id = geometryId,
                Topology = isPoint ? SceneTopology.Points : SceneTopology.Lines,
                Vertices = vertices!,
                Indices = indices,
            };
            scene.Geometries.Add(geometry);
            scene.Instances.Add(new SceneInstance
            {
                ElementId = element.Id,
                GeometryId = geometry.Id,
                Color = ColourOf(entity.ColorIndex),
            });
        }

        if (withoutGeometry.Count > 0)
        {
            string kinds = string.Join(", ", withoutGeometry.Select(kvp => $"{kvp.Key} {kvp.Value}"));
            diagnostics.Add(new SceneDiagnostic(SceneSeverity.Info, "entities-without-geometry",
                $"{withoutGeometry.Values.Sum()} entities have no geometry the scene places yet ({kinds})."));
        }

        if (nonFinite > 0)
        {
            diagnostics.Add(new SceneDiagnostic(SceneSeverity.Warning, "non-finite-geometry",
                $"{nonFinite} entities had non-finite coordinates and are placed without geometry."));
        }

        return new DwgSceneResult
        {
            Document = scene,
            Diagnostics = diagnostics,
            ElementCount = elementCount,
            ElementsWithGeometry = elementsWithGeometry,
            EntitiesWithoutGeometry = withoutGeometry.Values.Sum(),
            LengthUnit = options.LengthUnit,
            MetresPerUnit = metresPerUnit,
            UnitAssumed = true,
        };
    }

    /// <summary>Tells whether <see cref="DwgSceneOptions.LengthUnit"/> accepts <paramref name="unit"/>.</summary>
    public static bool IsSupportedUnit(string unit) => unit is "mm" or "cm" or "m" or "ft" or "in";

    private static double MetresPerUnit(string unit) => unit switch
    {
        "mm" => 0.001,
        "cm" => 0.01,
        "m" => 1.0,
        "ft" => 0.3048,
        "in" => 0.0254,
        _ => throw new ArgumentException($"DwgSceneOptions.LengthUnit '{unit}' is not supported; use mm, cm, m, ft or in.", nameof(unit)),
    };

    private static RgbaColor? ColourOf(int colorIndex)
    {
        if (colorIndex is <= 0 or >= 256)
        {
            // BYBLOCK (0) and BYLAYER (256) have no colour of their own; the scene leaves the choice to the
            // consumer until layers and blocks are decoded.
            return null;
        }

        (byte r, byte g, byte b) = DwgAciPalette.RgbOf(colorIndex);
        return new RgbaColor(r, g, b);
    }

    private static Vector3d[] LineVertices(double[] g, double scale) =>
    [
        new(g[0] * scale, g[1] * scale, g[2] * scale),
        new(g[3] * scale, g[4] * scale, g[5] * scale),
    ];

    private static Vector3d[] CircleVertices(double[] g, double scale)
    {
        double cx = g[0] * scale, cy = g[1] * scale, cz = g[2] * scale;
        double radius = g[3] * scale;
        var vertices = new Vector3d[CircleSegments];
        for (int i = 0; i < CircleSegments; i++)
        {
            double angle = 2.0 * Math.PI * i / CircleSegments;
            vertices[i] = new Vector3d(cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle), cz);
        }

        return vertices;
    }

    private static Vector3d[] ArcVertices(double[] g, double scale)
    {
        double cx = g[0] * scale, cy = g[1] * scale, cz = g[2] * scale;
        double radius = g[3] * scale;
        double start = g[4];
        double span = g[5] - g[4];
        while (span <= 0)
        {
            span += 2.0 * Math.PI;
        }

        int segments = Math.Clamp((int)Math.Ceiling(span / ArcRadiansPerSegment), MinArcSegments, CircleSegments * 4);
        var vertices = new Vector3d[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            double angle = start + span * i / segments;
            vertices[i] = new Vector3d(cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle), cz);
        }

        return vertices;
    }

    private static Vector3d[] PointVertices(double[] g, double scale) => [new(g[0] * scale, g[1] * scale, g[2] * scale)];

    /// <summary>Line indices of a tessellated circle (closed ring) or arc (open chain), sharing the joints.</summary>
    private static List<int> SharedIndices(int vertexCount, bool closed)
    {
        var indices = new List<int>((vertexCount - (closed ? 0 : 1)) * 2);
        for (int i = 0; i < vertexCount - 1; i++)
        {
            indices.Add(i);
            indices.Add(i + 1);
        }

        if (closed)
        {
            indices.Add(vertexCount - 1);
            indices.Add(0);
        }

        return indices;
    }

    private static bool IsFinite(Vector3d v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
}
