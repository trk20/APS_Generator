using ApsGenerator.Core.Export;
using UnityEngine;
using UnityEngine.Rendering;
using ApsGenerator.Mod.Generation;

namespace ApsGenerator.Mod.UI;

internal sealed class ApsPreviewHandles : IDisposable
{
    private const float MinimumHandleLength = 0.85f;
    private const float MaximumHandleLength = 3.2f;
    private const float HandleLengthPerCameraDistance = 0.1f;
    private const float HitRadiusPixels = 13f;
    private const float SelectedScale = 1.18f;
    private const float ProjectionEpsilon = 0.0001f;

    private static readonly Color WidthColor = new(0.95f, 0.12f, 0.12f, 0.95f);
    private static readonly Color DepthColor = new(0.12f, 0.38f, 1f, 0.95f);
    private static readonly Color HeightColor = new(0.12f, 0.9f, 0.2f, 0.95f);
    private static readonly Color HoleColor = new(1f, 0.9f, 0.05f, 0.95f);

    private Mesh? arrowMesh;
    private Material[]? materials;
    private PreviewHandleKind? hovered;
    private DragState? drag;

    internal void Draw(
        ApsGenerationSettings settings,
        Matrix4x4 placementMatrix,
        bool heightOnly,
        Action<Action> changeSettings)
    {
        Camera? camera = Camera.main;
        if (camera is null || !EnsureResources())
            return;

        RestrictInteraction(heightOnly);
        PreviewHandle[] handles = CreateHandles(settings, heightOnly);
        UpdateInteraction(camera, placementMatrix, handles, settings, changeSettings);
        DrawHandles(camera, placementMatrix, handles);
    }

    internal bool IsPointerOver(
        ApsGenerationSettings settings,
        Matrix4x4 placementMatrix,
        bool heightOnly)
    {
        RestrictInteraction(heightOnly);
        if (drag is not null)
            return true;
        Camera? camera = Camera.main;
        return camera is not null &&
               FindHovered(camera, placementMatrix, CreateHandles(settings, heightOnly)) is not null;
    }

    internal void CancelInteraction()
    {
        drag = null;
        hovered = null;
    }

    private void RestrictInteraction(bool heightOnly)
    {
        if (heightOnly && drag is not null && drag.Kind != PreviewHandleKind.Height)
            CancelInteraction();
    }

    public void Dispose()
    {
        if (arrowMesh is not null)
            UnityEngine.Object.Destroy(arrowMesh);
        if (materials is null)
            return;
        foreach (Material material in materials)
            UnityEngine.Object.Destroy(material);
    }

    private void UpdateInteraction(
        Camera camera,
        Matrix4x4 placementMatrix,
        IReadOnlyList<PreviewHandle> handles,
        ApsGenerationSettings settings,
        Action<Action> changeSettings)
    {
        if (drag is DragState active)
        {
            ContinueDrag(camera, active, settings, changeSettings);
            return;
        }

        hovered = FindHovered(camera, placementMatrix, handles);
        if (!hovered.HasValue || !Input.GetMouseButtonDown(0))
            return;

        PreviewHandle handle = handles.First(candidate => candidate.Kind == hovered.Value);
        Vector3 origin = placementMatrix.MultiplyPoint3x4(handle.Position);
        Vector3 axis = placementMatrix.MultiplyVector(handle.Direction).normalized;
        if (!TryProjectOntoAxis(camera.ScreenPointToRay(Input.mousePosition), origin, axis, out float initialProjection))
            return;

        drag = new DragState(handle.Kind, origin, axis, initialProjection, GetValue(settings, handle.Kind));
    }

    private void ContinueDrag(
        Camera camera,
        DragState active,
        ApsGenerationSettings settings,
        Action<Action> changeSettings)
    {
        if (!Input.GetMouseButton(0))
        {
            drag = null;
            return;
        }
        if (!TryProjectOntoAxis(
                camera.ScreenPointToRay(Input.mousePosition),
                active.Origin,
                active.Axis,
                out float projection))
            return;

        int step = ValueStep(settings, active.Kind);
        float valuePerMetre = active.Kind == PreviewHandleKind.Height ? 1f : 2f;
        int snappedDelta = Mathf.RoundToInt((projection - active.InitialProjection) * valuePerMetre / step) * step;
        int target = active.InitialValue + snappedDelta;
        if (target == GetValue(settings, active.Kind))
            return;
        changeSettings(() => SetValue(settings, active.Kind, target));
    }

    private void DrawHandles(Camera camera, Matrix4x4 placementMatrix, IReadOnlyList<PreviewHandle> handles)
    {
        foreach (PreviewHandle handle in handles)
        {
            Vector3 worldPosition = placementMatrix.MultiplyPoint3x4(handle.Position);
            Vector3 worldDirection = placementMatrix.MultiplyVector(handle.Direction).normalized;
            float length = HandleLength(camera, worldPosition);
            bool selected = handle.Kind == (drag?.Kind ?? hovered);
            float scale = selected ? length * SelectedScale : length;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.right, worldDirection);
            Matrix4x4 matrix = Matrix4x4.TRS(worldPosition, rotation, Vector3.one * scale);
            Graphics.DrawMesh(arrowMesh!, matrix, MaterialFor(handle.Color, selected), 0);
        }
    }

    private static PreviewHandleKind? FindHovered(
        Camera camera,
        Matrix4x4 placementMatrix,
        IReadOnlyList<PreviewHandle> handles)
    {
        PreviewHandleKind? closest = null;
        float closestDistance = HitRadiusPixels * HitRadiusPixels;
        Vector2 pointer = Input.mousePosition;
        foreach (PreviewHandle handle in handles)
        {
            Vector3 start = placementMatrix.MultiplyPoint3x4(handle.Position);
            Vector3 direction = placementMatrix.MultiplyVector(handle.Direction).normalized;
            Vector3 startScreen = camera.WorldToScreenPoint(start);
            Vector3 endScreen = camera.WorldToScreenPoint(start + direction * HandleLength(camera, start));
            if (startScreen.z <= 0 || endScreen.z <= 0)
                continue;

            float distance = DistanceToSegmentSquared(pointer, startScreen, endScreen);
            if (distance > closestDistance)
                continue;
            closestDistance = distance;
            closest = handle.Kind;
        }
        return closest;
    }

    private static PreviewHandle[] CreateHandles(ApsGenerationSettings settings, bool heightOnly)
    {
        ApsLayoutGeometry geometry = settings.CreateRequest().Geometry;
        float centerX = geometry.MinimumX + settings.Width * 0.5f - 0.5f;
        float centerZ = settings.Depth * 0.5f - 0.5f;
        float centerY = (geometry.IntendedHeight - 1f) * 0.5f;
        var handles = new List<PreviewHandle>
        {
            new(PreviewHandleKind.Height,
                new Vector3(centerX, geometry.IntendedHeight - 0.5f, centerZ),
                Vector3.up,
                PreviewHandleColor.Height)
        };
        if (heightOnly)
            return handles.ToArray();

        handles.AddRange([
            new(PreviewHandleKind.Width,
                new Vector3(geometry.MinimumX + settings.Width - 0.5f, centerY, centerZ),
                Vector3.right,
                PreviewHandleColor.Width),
            new(PreviewHandleKind.Depth,
                new Vector3(centerX, centerY, settings.Depth - 0.5f),
                Vector3.forward,
                PreviewHandleColor.Depth)
        ]);
        if (settings.TemplateShape != ApsTemplateShape.CircleCenterHole)
            return handles.ToArray();

        float holeRadius = settings.HoleSize * 0.5f;
        handles.Add(new PreviewHandle(
            PreviewHandleKind.HoleWidth,
            new Vector3(centerX + holeRadius, centerY, centerZ),
            Vector3.right,
            PreviewHandleColor.Hole));
        handles.Add(new PreviewHandle(
            PreviewHandleKind.HoleDepth,
            new Vector3(centerX, centerY, centerZ + holeRadius),
            Vector3.forward,
            PreviewHandleColor.Hole));
        return handles.ToArray();
    }

    private bool EnsureResources()
    {
        if (arrowMesh is not null)
            return true;
        Shader? shader = Shader.Find("Hidden/Internal-Colored");
        if (shader is null)
            return false;

        arrowMesh = CreateArrowMesh();
        materials =
        [
            CreateMaterial(shader, WidthColor),
            CreateMaterial(shader, DepthColor),
            CreateMaterial(shader, HeightColor),
            CreateMaterial(shader, HoleColor),
            CreateMaterial(shader, SelectedColor(WidthColor)),
            CreateMaterial(shader, SelectedColor(DepthColor)),
            CreateMaterial(shader, SelectedColor(HeightColor)),
            CreateMaterial(shader, SelectedColor(HoleColor))
        ];
        return true;
    }

    private Material MaterialFor(PreviewHandleColor color, bool selected) =>
        materials![(selected ? 4 : 0) + (int)color];

    private static int GetValue(ApsGenerationSettings settings, PreviewHandleKind kind) => kind switch
    {
        PreviewHandleKind.Width => settings.Width,
        PreviewHandleKind.Depth => settings.IsDepthLocked ? settings.Width : settings.Depth,
        PreviewHandleKind.Height => settings.ComponentLength,
        PreviewHandleKind.HoleWidth or PreviewHandleKind.HoleDepth => settings.HoleSize,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static void SetValue(ApsGenerationSettings settings, PreviewHandleKind kind, int value)
    {
        switch (kind)
        {
            case PreviewHandleKind.Width:
                settings.SetWidth(value);
                return;
            case PreviewHandleKind.Depth:
                if (settings.IsDepthLocked) settings.SetWidth(value);
                else settings.SetDepth(value);
                return;
            case PreviewHandleKind.Height:
                settings.SetComponentLength(value);
                return;
            case PreviewHandleKind.HoleWidth:
            case PreviewHandleKind.HoleDepth:
                settings.SetHoleSize(value);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private static int ValueStep(ApsGenerationSettings settings, PreviewHandleKind kind) => kind switch
    {
        PreviewHandleKind.Width or PreviewHandleKind.Depth => settings.SnapToOddDimensions ? 2 : 1,
        PreviewHandleKind.Height => settings.TetrisType == ApsGenerator.Core.Models.TetrisType.FiveClip
            ? ApsGenerationSettings.FiveClipSectionHeight
            : 1,
        PreviewHandleKind.HoleWidth or PreviewHandleKind.HoleDepth => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static bool TryProjectOntoAxis(Ray ray, Vector3 origin, Vector3 axis, out float projection)
    {
        Vector3 offset = ray.origin - origin;
        float rayAxisDot = Vector3.Dot(ray.direction, axis);
        float denominator = 1f - rayAxisDot * rayAxisDot;
        if (Mathf.Abs(denominator) < ProjectionEpsilon)
        {
            projection = 0;
            return false;
        }

        projection = (Vector3.Dot(axis, offset) - rayAxisDot * Vector3.Dot(ray.direction, offset)) / denominator;
        return true;
    }

    private static float DistanceToSegmentSquared(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= ProjectionEpsilon)
            return (point - start).sqrMagnitude;
        float amount = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
        return (point - (start + segment * amount)).sqrMagnitude;
    }

    private static float HandleLength(Camera camera, Vector3 position) => Mathf.Clamp(
        Vector3.Distance(camera.transform.position, position) * HandleLengthPerCameraDistance,
        MinimumHandleLength,
        MaximumHandleLength);

    private static Color SelectedColor(Color color) => Color.Lerp(color, Color.black, 0.22f);

    private static Material CreateMaterial(Shader shader, Color color)
    {
        var material = new Material(shader)
        {
            color = color,
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = 3001
        };
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_Cull", (int)CullMode.Off);
        material.SetInt("_ZWrite", 0);
        return material;
    }

    private static Mesh CreateArrowMesh()
    {
        const int sides = 12;
        const float shaftEnd = 0.72f;
        const float shaftRadius = 0.026f;
        const float headRadius = 0.105f;
        var vertices = new List<Vector3>(sides * 3 + 2);
        for (int ring = 0; ring < 3; ring++)
        {
            float x = ring == 0 ? 0 : shaftEnd;
            float radius = ring == 2 ? headRadius : shaftRadius;
            for (int side = 0; side < sides; side++)
            {
                float angle = side * Mathf.PI * 2f / sides;
                vertices.Add(new Vector3(x, Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius));
            }
        }
        int shaftCapCenter = vertices.Count;
        vertices.Add(Vector3.zero);
        int tip = vertices.Count;
        vertices.Add(Vector3.right);

        var triangles = new List<int>(sides * 12);
        for (int side = 0; side < sides; side++)
        {
            int next = (side + 1) % sides;
            triangles.AddRange([side, sides + side, sides + next, side, sides + next, next]);
            triangles.AddRange([shaftCapCenter, next, side]);
            triangles.AddRange([sides * 2 + side, tip, sides * 2 + next]);
            triangles.AddRange([sides + side, sides * 2 + next, sides * 2 + side]);
            triangles.AddRange([sides + side, sides + next, sides * 2 + next]);
        }
        var mesh = new Mesh { name = "APS resize arrow", hideFlags = HideFlags.HideAndDontSave };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private sealed record DragState(
        PreviewHandleKind Kind,
        Vector3 Origin,
        Vector3 Axis,
        float InitialProjection,
        int InitialValue);

    private readonly record struct PreviewHandle(
        PreviewHandleKind Kind,
        Vector3 Position,
        Vector3 Direction,
        PreviewHandleColor Color);

    private enum PreviewHandleKind { Width, Depth, Height, HoleWidth, HoleDepth }
    private enum PreviewHandleColor { Width, Depth, Height, Hole }
}
