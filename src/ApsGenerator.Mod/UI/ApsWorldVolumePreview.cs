using ApsGenerator.Core.Export;
using ApsGenerator.Core.Models;
using BrilliantSkies.Ftd.Avatar.Build;
using UnityEngine;
using UnityEngine.Rendering;
using ApsGenerator.Mod.Generation;

namespace ApsGenerator.Mod.UI;

internal sealed class ApsWorldVolumePreview : IDisposable
{
    private const float MirrorPlaneExtension = 1f;
    private static readonly Color TetrisColor = new(0.12f, 0.95f, 0.38f, 0.88f);
    private static readonly Color BottomColor = new(1f, 0.52f, 0.10f, 0.84f);
    private static readonly Color CoolerColor = new(0.10f, 0.68f, 1f, 0.88f);
    private static readonly Color MirrorFillColor = new(0.78f, 0.24f, 1f, 0.11f);
    private static readonly Color MirrorWireColor = new(0.88f, 0.42f, 1f, 0.72f);

    private Material? tetrisMaterial;
    private Material? bottomMaterial;
    private Material? coolerMaterial;
    private Material? mirrorFillMaterial;
    private Material? mirrorWireMaterial;
    private readonly ApsPreviewHandles handles = new();
    private PreviewMeshSet? meshes;
    private PreviewKey? meshKey;

    internal bool IsPointerCaptured(ApsGenerationSettings settings, bool hasCurrentSolution)
    {
        if (!TryGetPlacementMatrix(out Matrix4x4 placementMatrix))
            return false;
        return handles.IsPointerOver(settings, placementMatrix, hasCurrentSolution);
    }

    internal void CancelInteraction() => handles.CancelInteraction();

    internal void Draw(
        ApsGenerationSettings settings,
        bool hasCurrentSolution,
        Action<Action> changeSettings)
    {
        if (!TryGetPlacementMatrix(out Matrix4x4 placementMatrix) || !EnsureMaterials())
            return;

        EnsureMeshes(settings);
        DrawLines(meshes!.Tetris, placementMatrix, tetrisMaterial!);
        DrawLines(meshes.Bottom, placementMatrix, bottomMaterial!);
        DrawLines(meshes.Cooler, placementMatrix, coolerMaterial!);
        DrawMirror(meshes.Mirror, placementMatrix);
        DrawLines(meshes.Rotation, placementMatrix, mirrorWireMaterial!);
        handles.Draw(settings, placementMatrix, hasCurrentSolution, changeSettings);
    }

    public void Dispose()
    {
        meshes?.Dispose();
        DestroyMaterial(tetrisMaterial);
        DestroyMaterial(bottomMaterial);
        DestroyMaterial(coolerMaterial);
        DestroyMaterial(mirrorFillMaterial);
        DestroyMaterial(mirrorWireMaterial);
        handles.Dispose();
    }

    private bool EnsureMaterials()
    {
        if (tetrisMaterial is not null)
            return true;

        Shader? shader = Shader.Find("Hidden/Internal-Colored");
        if (shader is null)
            return false;

        tetrisMaterial = CreateMaterial(shader, TetrisColor);
        bottomMaterial = CreateMaterial(shader, BottomColor);
        coolerMaterial = CreateMaterial(shader, CoolerColor);
        mirrorFillMaterial = CreateMaterial(shader, MirrorFillColor);
        mirrorWireMaterial = CreateMaterial(shader, MirrorWireColor);
        return true;
    }

    private void EnsureMeshes(ApsGenerationSettings settings)
    {
        var requestedKey = PreviewKey.From(settings);
        if (meshKey is PreviewKey currentKey && currentKey.Equals(requestedKey))
            return;

        meshes?.Dispose();
        ApsGenerationRequest request = settings.CreateRequest();
        Grid grid = request.Problem.Template.CreateGrid();
        ApsLayoutGeometry geometry = request.Geometry;
        int minimumX = geometry.MinimumX;
        bool hasBottom = geometry.HasBottomLayer;
        bool hasCooler = geometry.HasCoolerLayer;
        float tetrisBottom = geometry.TetrisBottom - 0.5f;
        float tetrisTop = geometry.TetrisTop - 0.5f;
        float previewBottom = -0.5f;
        float previewTop = geometry.IntendedHeight - 0.5f;

        float sectionHeight = geometry.SectionHeight;
        Mesh tetris = CreateVolumeEdges(
            grid,
            minimumX,
            tetrisBottom,
            tetrisTop,
            "APS volume edges",
            sectionHeight);
        Mesh? bottom = hasBottom
            ? CreateVolumeEdges(grid, minimumX, -0.5f, 0.5f, "APS bottom edges")
            : null;
        Mesh? cooler = hasCooler
            ? CreateVolumeEdges(grid, minimumX, tetrisTop, tetrisTop + 1f, "APS cooler edges")
            : null;
        Mesh? mirror = CreateMirrorPlanes(
            grid,
            minimumX,
            previewBottom,
            previewTop,
            settings.SymmetryType);
        Mesh? rotation = CreateRotationArrows(
            grid,
            minimumX,
            previewTop + 0.35f,
            settings.SymmetryType);

        meshes = new PreviewMeshSet(tetris, bottom, cooler, mirror, rotation);
        meshKey = requestedKey;
    }

    private static Mesh CreateVolumeEdges(
        Grid grid,
        int minimumX,
        float minimumY,
        float maximumY,
        string name,
        float sectionHeight = float.PositiveInfinity)
    {
        IReadOnlyList<BoundaryEdge> boundary = FindBoundary(grid, minimumX);
        var builder = new LineMeshBuilder();
        var directionsAtPoint = new Dictionary<GridPoint, List<GridPoint>>();

        foreach (BoundaryEdge edge in boundary)
        {
            for (float y = minimumY; y < maximumY; y += sectionHeight)
                builder.Add(ToVector(edge.Start, y), ToVector(edge.End, y));
            builder.Add(ToVector(edge.Start, maximumY), ToVector(edge.End, maximumY));
            AddDirection(directionsAtPoint, edge.Start, edge.End);
            AddDirection(directionsAtPoint, edge.End, edge.Start);
        }

        foreach (var pair in directionsAtPoint)
        {
            if (IsStraightBoundary(pair.Value))
                continue;

            builder.Add(ToVector(pair.Key, minimumY), ToVector(pair.Key, maximumY));
        }

        return builder.Create(name);
    }

    private static IReadOnlyList<BoundaryEdge> FindBoundary(Grid grid, int minimumX)
    {
        var edges = new List<BoundaryEdge>();
        for (int row = 0; row < grid.Height; row++)
        for (int column = 0; column < grid.Width; column++)
        {
            if (!grid.IsAvailable(row, column))
                continue;

            int x2 = 2 * (minimumX + column);
            int z2 = 2 * (grid.Height - 1 - row);
            if (!grid.IsAvailable(row - 1, column))
                edges.Add(new BoundaryEdge(new GridPoint(x2 - 1, z2 + 1), new GridPoint(x2 + 1, z2 + 1)));
            if (!grid.IsAvailable(row + 1, column))
                edges.Add(new BoundaryEdge(new GridPoint(x2 + 1, z2 - 1), new GridPoint(x2 - 1, z2 - 1)));
            if (!grid.IsAvailable(row, column - 1))
                edges.Add(new BoundaryEdge(new GridPoint(x2 - 1, z2 - 1), new GridPoint(x2 - 1, z2 + 1)));
            if (!grid.IsAvailable(row, column + 1))
                edges.Add(new BoundaryEdge(new GridPoint(x2 + 1, z2 + 1), new GridPoint(x2 + 1, z2 - 1)));
        }
        return edges;
    }

    private static void AddDirection(
        IDictionary<GridPoint, List<GridPoint>> directions,
        GridPoint point,
        GridPoint other)
    {
        if (!directions.TryGetValue(point, out List<GridPoint>? values))
        {
            values = [];
            directions.Add(point, values);
        }
        values.Add(new GridPoint(Math.Sign(other.X - point.X), Math.Sign(other.Z - point.Z)));
    }

    private static bool IsStraightBoundary(IReadOnlyList<GridPoint> directions) =>
        directions.Count == 2 &&
        directions[0].X == -directions[1].X &&
        directions[0].Z == -directions[1].Z;

    private static Mesh? CreateMirrorPlanes(
        Grid grid,
        int minimumX,
        float minimumY,
        float maximumY,
        SymmetryType symmetry)
    {
        bool vertical = symmetry is SymmetryType.VerticalReflection or SymmetryType.BothReflection;
        bool horizontal = symmetry is SymmetryType.HorizontalReflection or SymmetryType.BothReflection;
        if (!vertical && !horizontal)
            return null;

        float minimumGridX = minimumX - 0.5f - MirrorPlaneExtension;
        float maximumGridX = minimumX + grid.Width - 0.5f + MirrorPlaneExtension;
        float minimumZ = -0.5f - MirrorPlaneExtension;
        float maximumZ = grid.Height - 0.5f + MirrorPlaneExtension;
        minimumY -= MirrorPlaneExtension;
        maximumY += MirrorPlaneExtension;
        float centerX = (minimumGridX + maximumGridX) * 0.5f;
        float centerZ = (minimumZ + maximumZ) * 0.5f;
        var builder = new PlaneMeshBuilder();

        if (vertical)
        {
            builder.Add(
                new Vector3(centerX, minimumY, minimumZ),
                new Vector3(centerX, minimumY, maximumZ),
                new Vector3(centerX, maximumY, maximumZ),
                new Vector3(centerX, maximumY, minimumZ));
        }
        if (horizontal)
        {
            builder.Add(
                new Vector3(minimumGridX, minimumY, centerZ),
                new Vector3(maximumGridX, minimumY, centerZ),
                new Vector3(maximumGridX, maximumY, centerZ),
                new Vector3(minimumGridX, maximumY, centerZ));
        }

        return builder.Create("APS reflection planes");
    }

    private void DrawMirror(Mesh? mirror, Matrix4x4 matrix)
    {
        if (mirror is null)
            return;

        Graphics.DrawMesh(mirror, matrix, mirrorFillMaterial!, 0, null, 0);
        Graphics.DrawMesh(mirror, matrix, mirrorWireMaterial!, 0, null, 1);
    }

    private static Mesh? CreateRotationArrows(
        Grid grid,
        int minimumX,
        float y,
        SymmetryType symmetry)
    {
        int arrowCount = symmetry switch
        {
            SymmetryType.Rotation180 => 2,
            SymmetryType.Rotation90 => 4,
            _ => 0
        };
        if (arrowCount == 0)
            return null;

        var center = new Vector3(
            minimumX + grid.Width * 0.5f - 0.5f,
            y,
            grid.Height * 0.5f - 0.5f);
        float radius = Mathf.Clamp(Mathf.Min(grid.Width, grid.Height) * 0.32f, 0.75f, 4f);
        float segmentAngle = 360f / arrowCount;
        float rotationOffset = symmetry == SymmetryType.Rotation180 ? 90f : 0f;
        var builder = new LineMeshBuilder();

        for (int arrow = 0; arrow < arrowCount; arrow++)
            AddRotationArrow(
                builder,
                center,
                radius,
                rotationOffset + arrow * segmentAngle + 10f,
                rotationOffset + (arrow + 1) * segmentAngle - 10f);

        return builder.Create("APS rotational symmetry arrows");
    }

    private static void AddRotationArrow(
        LineMeshBuilder builder,
        Vector3 center,
        float radius,
        float startDegrees,
        float endDegrees)
    {
        const int subdivisions = 16;
        Vector3 previous = ArcPoint(center, radius, startDegrees);
        for (int step = 1; step <= subdivisions; step++)
        {
            float degrees = Mathf.Lerp(startDegrees, endDegrees, step / (float)subdivisions);
            Vector3 current = ArcPoint(center, radius, degrees);
            builder.Add(previous, current);
            previous = current;
        }

        float radians = endDegrees * Mathf.Deg2Rad;
        var tangent = new Vector3(-Mathf.Sin(radians), 0, Mathf.Cos(radians));
        var radial = new Vector3(Mathf.Cos(radians), 0, Mathf.Sin(radians));
        float arrowLength = Mathf.Clamp(radius * 0.22f, 0.18f, 0.55f);
        builder.Add(previous, previous - tangent * arrowLength + radial * arrowLength * 0.55f);
        builder.Add(previous, previous - tangent * arrowLength - radial * arrowLength * 0.55f);
    }

    private static Vector3 ArcPoint(Vector3 center, float radius, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        return center + new Vector3(Mathf.Cos(radians) * radius, 0, Mathf.Sin(radians) * radius);
    }

    private static void DrawLines(Mesh? mesh, Matrix4x4 matrix, Material material)
    {
        if (mesh is not null)
            Graphics.DrawMesh(mesh, matrix, material, 0);
    }

    private static bool TryGetPlacementMatrix(out Matrix4x4 matrix)
    {
        matrix = Matrix4x4.identity;
        cBuild? build = cBuild.GetSingleton();
        if (build is null || build.buildMarker is null ||
            (build.buildMode != enumBuildMode.active &&
             build.buildMode != enumBuildMode.activeInventory))
            return false;

        matrix = build.GetBuildMarkerTransform().localToWorldMatrix *
                 Matrix4x4.Rotate(build.GetBuildMarkerLocalRotation());
        return true;
    }

    private static Vector3 ToVector(GridPoint point, float y) =>
        new(point.X * 0.5f, y, point.Z * 0.5f);

    private static Material CreateMaterial(Shader shader, Color color)
    {
        var material = new Material(shader)
        {
            color = color,
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = 3000
        };
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_Cull", (int)CullMode.Off);
        material.SetInt("_ZWrite", 0);
        return material;
    }

    private static void DestroyMaterial(Material? material)
    {
        if (material is not null)
            UnityEngine.Object.Destroy(material);
    }

    private readonly struct GridPoint : IEquatable<GridPoint>
    {
        internal GridPoint(int x, int z)
        {
            X = x;
            Z = z;
        }

        internal int X { get; }
        internal int Z { get; }

        public bool Equals(GridPoint other) => X == other.X && Z == other.Z;
        public override bool Equals(object? obj) => obj is GridPoint other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Z);
    }

    private readonly struct BoundaryEdge
    {
        internal BoundaryEdge(GridPoint start, GridPoint end)
        {
            Start = start;
            End = end;
        }

        internal GridPoint Start { get; }
        internal GridPoint End { get; }
    }

    private sealed class LineMeshBuilder
    {
        private readonly List<Vector3> vertices = [];
        private readonly List<int> indices = [];

        internal void Add(Vector3 start, Vector3 end)
        {
            int index = vertices.Count;
            vertices.Add(start);
            vertices.Add(end);
            indices.Add(index);
            indices.Add(index + 1);
        }

        internal Mesh Create(string name)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Lines, 0, calculateBounds: true);
            return mesh;
        }
    }

    private sealed class PlaneMeshBuilder
    {
        private readonly List<Vector3> vertices = [];
        private readonly List<int> triangles = [];
        private readonly List<int> lines = [];

        internal void Add(Vector3 first, Vector3 second, Vector3 third, Vector3 fourth)
        {
            int index = vertices.Count;
            vertices.Add(first);
            vertices.Add(second);
            vertices.Add(third);
            vertices.Add(fourth);
            triangles.AddRange([index, index + 1, index + 2, index, index + 2, index + 3]);
            lines.AddRange([index, index + 1, index + 1, index + 2, index + 2, index + 3, index + 3, index]);
        }

        internal Mesh Create(string name)
        {
            var mesh = new Mesh
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                subMeshCount = 2
            };
            mesh.SetVertices(vertices);
            mesh.SetIndices(triangles, MeshTopology.Triangles, 0, calculateBounds: false);
            mesh.SetIndices(lines, MeshTopology.Lines, 1, calculateBounds: true);
            return mesh;
        }
    }

    private sealed record PreviewKey(ApsTemplate Template, ApsLayoutGeometry Geometry, SymmetryType Symmetry)
    {
        internal static PreviewKey From(ApsGenerationSettings settings)
        {
            ApsGenerationRequest request = settings.CreateRequest();
            return new(request.Problem.Template, request.Geometry, request.Problem.Symmetry);
        }
    }

    private sealed class PreviewMeshSet : IDisposable
    {
        internal PreviewMeshSet(Mesh tetris, Mesh? bottom, Mesh? cooler, Mesh? mirror, Mesh? rotation)
        {
            Tetris = tetris;
            Bottom = bottom;
            Cooler = cooler;
            Mirror = mirror;
            Rotation = rotation;
        }

        internal Mesh Tetris { get; }
        internal Mesh? Bottom { get; }
        internal Mesh? Cooler { get; }
        internal Mesh? Mirror { get; }
        internal Mesh? Rotation { get; }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(Tetris);
            DestroyMesh(Bottom);
            DestroyMesh(Cooler);
            DestroyMesh(Mirror);
            DestroyMesh(Rotation);
        }

        private static void DestroyMesh(Mesh? mesh)
        {
            if (mesh is not null)
                UnityEngine.Object.Destroy(mesh);
        }
    }
}
