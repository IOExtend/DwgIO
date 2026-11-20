using IOScene;

namespace DwgIO.Scene;

internal static class ViewerMeshGeometry
{
    // shortcut: IOViewer 0.3.1 draws triangles only; use thin display meshes until it supports lines and points.
    public static SceneDocument Adapt(SceneDocument scene)
    {
        if (!scene.Geometries.Any(g => g.Topology is SceneTopology.Lines or SceneTopology.Points))
        {
            return scene;
        }

        var bounds = scene.ComputeBounds();
        double extent = bounds.IsEmpty ? 0 : MaxComponent(bounds.Size);
        double halfWidth = extent > 0 && double.IsFinite(extent) ? extent / 1500 : 0.001;

        return new SceneDocument
        {
            Title = scene.Title,
            Models = scene.Models,
            Materials = scene.Materials,
            Textures = scene.Textures,
            PropertySets = scene.PropertySets,
            Levels = scene.Levels,
            Elements = scene.Elements,
            Instances = scene.Instances,
            Grids = scene.Grids,
            Viewpoints = scene.Viewpoints,
            Schedules = scene.Schedules,
            ClashTests = scene.ClashTests,
            Geometries = scene.Geometries.ConvertAll(g => ToTriangles(g, halfWidth)),
        };
    }

    private static SceneGeometry ToTriangles(SceneGeometry geometry, double halfWidth)
    {
        if (geometry.Topology is not (SceneTopology.Lines or SceneTopology.Points))
        {
            return geometry;
        }

        bool isLine = geometry.Topology == SceneTopology.Lines;
        int count = geometry.Indices.Count == 0 ? geometry.Vertices.Count : geometry.Indices.Count;
        int stride = isLine ? 2 : 1;
        if (count % stride != 0)
        {
            throw new InvalidDataException($"Geometry {geometry.Id} has an incomplete line segment.");
        }

        var vertices = new List<Vector3d>();
        var indices = new List<int>();
        for (int i = 0; i < count; i += stride)
        {
            Vector3d a = Vertex(i);
            Vector3d b = isLine ? Vertex(i + 1) : a;
            Vector3d delta = b - a;
            double scale = MaxComponent(delta);
            double radius = Math.Max(halfWidth, Math.Max(MaxComponent(a), MaxComponent(b)) * 1e-15);
            if (scale == 0)
            {
                AddPoint(a, radius);
                continue;
            }

            Vector3d axis = (delta / scale).Normalized();
            Vector3d side = Vector3d.Cross(axis, Math.Abs(axis.Z) < 0.9 ? Vector3d.UnitZ : Vector3d.UnitX).Normalized();
            AddRibbon(a, b, side * radius);
            AddRibbon(a, b, Vector3d.Cross(axis, side) * radius);
        }

        return new SceneGeometry { Id = geometry.Id, Vertices = vertices, Indices = indices };

        Vector3d Vertex(int i)
        {
            int index = geometry.Indices.Count == 0 ? i : geometry.Indices[i];
            if ((uint)index >= (uint)geometry.Vertices.Count)
            {
                throw new InvalidDataException($"Geometry {geometry.Id} references missing vertex {index}.");
            }

            var vertex = geometry.Vertices[index];
            if (!double.IsFinite(MaxComponent(vertex)))
            {
                throw new InvalidDataException($"Geometry {geometry.Id} has a non-finite coordinate.");
            }

            return vertex;
        }

        void AddRibbon(Vector3d a, Vector3d b, Vector3d side)
        {
            int first = vertices.Count;
            vertices.AddRange([a - side, b - side, b + side, a + side]);
            indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
        }

        void AddPoint(Vector3d p, double radius)
        {
            AddRibbon(p - Vector3d.UnitX * radius, p + Vector3d.UnitX * radius, Vector3d.UnitY * radius);
            AddRibbon(p - Vector3d.UnitX * radius, p + Vector3d.UnitX * radius, Vector3d.UnitZ * radius);
            AddRibbon(p - Vector3d.UnitY * radius, p + Vector3d.UnitY * radius, Vector3d.UnitZ * radius);
        }
    }

    private static double MaxComponent(Vector3d vector) =>
        Math.Max(Math.Abs(vector.X), Math.Max(Math.Abs(vector.Y), Math.Abs(vector.Z)));
}
