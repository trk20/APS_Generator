using System;
using System.Numerics;

namespace ApsGenerator.UI.Services.Export;

public enum Face
{
    Forward = 0,
    Back = 1,
    Right = 2,
    Left = 3,
    Up = 4,
    Down = 5,
}

/// <summary>
/// Computes BLR rotation indices using the game's 24-rotation table.
/// Each rotation index maps to an axis-aligned orientation defined by a (forward, up) pair.
/// A block's local axes are transformed by the rotation: local +Z → forward, local +Y → up, local +X → right.
/// </summary>
public static class BlockRotation
{
    // From Quats helper in FtD code
    private static readonly (Vector3 Forward, Vector3 Up)[] Rotations =
    [
        // in order (index == BLR)
        (Vector3.UnitZ, Vector3.UnitY),
        (Vector3.UnitX, Vector3.UnitY),
        (-Vector3.UnitZ, Vector3.UnitY),
        (-Vector3.UnitX, Vector3.UnitY),
        (-Vector3.UnitY, Vector3.UnitZ),
        (-Vector3.UnitY, Vector3.UnitX),
        (-Vector3.UnitY, -Vector3.UnitZ),
        (-Vector3.UnitY, -Vector3.UnitX),
        (Vector3.UnitY, Vector3.UnitZ),
        (Vector3.UnitY, Vector3.UnitX),
        (Vector3.UnitY, -Vector3.UnitZ),
        (Vector3.UnitY, -Vector3.UnitX),
        (Vector3.UnitZ, -Vector3.UnitY),
        (Vector3.UnitX, -Vector3.UnitY),
        (-Vector3.UnitZ, -Vector3.UnitY),
        (-Vector3.UnitX, -Vector3.UnitY),
        (Vector3.UnitZ, Vector3.UnitX),
        (-Vector3.UnitZ, Vector3.UnitX),
        (Vector3.UnitZ, -Vector3.UnitX),
        (-Vector3.UnitZ, -Vector3.UnitX),
        (Vector3.UnitX, Vector3.UnitZ),
        (-Vector3.UnitX, Vector3.UnitZ),
        (Vector3.UnitX, -Vector3.UnitZ),
        (-Vector3.UnitX, -Vector3.UnitZ),
    ];

    /// <summary>
    /// Find the BLR index where the block's local primary axis aligns with the target direction
    /// and the local secondary axis aligns with the target secondary direction.
    /// </summary>
    public static int FindRotation(
        Vector3 localAxis, Vector3 targetDirection,
        Vector3 localSecondary, Vector3 targetSecondary)
    {
        for (int i = 0; i < 24; i++)
        {
            Vector3 transformedAxis = TransformDirection(i, localAxis);
            if (!ApproxEqual(transformedAxis, targetDirection))
                continue;

            Vector3 transformedSecondary = TransformDirection(i, localSecondary);
            if (ApproxEqual(transformedSecondary, targetSecondary))
                return i;
        }

        throw new InvalidOperationException(
            $"No rotation found: localAxis={localAxis}→{targetDirection}, localSecondary={localSecondary}→{targetSecondary}");
    }

    /// <summary>
    /// Find any BLR index where the block's local axis aligns with the target direction.
    /// Returns the first match when multiple rotations satisfy the constraint.
    /// </summary>
    public static int FindRotation(Vector3 localAxis, Vector3 targetDirection)
    {
        for (int i = 0; i < 24; i++)
        {
            if (ApproxEqual(TransformDirection(i, localAxis), targetDirection))
                return i;
        }

        throw new InvalidOperationException(
            $"No rotation found: localAxis={localAxis}→{targetDirection}");
    }

    /// <summary>
    /// Transform a local-space direction by the given rotation index.
    /// The rotation basis maps: local +X → right, local +Y → up, local +Z → forward.
    /// Right is derived as Cross(up, forward) per Unity's left-handed convention.
    /// </summary>
    public static Vector3 TransformDirection(int rotationIndex, Vector3 localDirection)
    {
        var (forward, up) = Rotations[rotationIndex];
        Vector3 right = Vector3.Cross(up, forward);
        return localDirection.X * right + localDirection.Y * up + localDirection.Z * forward;
    }

    private static bool ApproxEqual(Vector3 a, Vector3 b) =>
        Math.Abs(a.X - b.X) < 0.001f &&
        Math.Abs(a.Y - b.Y) < 0.001f &&
        Math.Abs(a.Z - b.Z) < 0.001f;

    public static Vector3 FaceToVector(Face face) => face switch
    {
        Face.Forward => Vector3.UnitZ,
        Face.Back => -Vector3.UnitZ,
        Face.Right => Vector3.UnitX,
        Face.Left => -Vector3.UnitX,
        Face.Up => Vector3.UnitY,
        Face.Down => -Vector3.UnitY,
        _ => throw new ArgumentOutOfRangeException(nameof(face)),
    };

    public static Face VectorToFace(Vector3 v)
    {
        if (v.Z > 0.5f) return Face.Forward;
        if (v.Z < -0.5f) return Face.Back;
        if (v.X > 0.5f) return Face.Right;
        if (v.X < -0.5f) return Face.Left;
        if (v.Y > 0.5f) return Face.Up;
        if (v.Y < -0.5f) return Face.Down;
        throw new ArgumentOutOfRangeException(nameof(v));
    }

    public static Face OppositeFace(Face face) => face switch
    {
        Face.Forward => Face.Back,
        Face.Back => Face.Forward,
        Face.Right => Face.Left,
        Face.Left => Face.Right,
        Face.Up => Face.Down,
        Face.Down => Face.Up,
        _ => throw new ArgumentOutOfRangeException(nameof(face)),
    };

    /// <summary>
    /// Finds BLR where localFace→worldFace and localSecondary→worldSecondary.
    /// Returns -1 if no valid rotation exists.
    /// </summary>
    public static int TryFindRotation(Face localFace, Face worldFace, Face localSecondary, Face worldSecondary)
    {
        Vector3 lv = FaceToVector(localFace), wv = FaceToVector(worldFace);
        Vector3 ls = FaceToVector(localSecondary), ws = FaceToVector(worldSecondary);

        for (int i = 0; i < 24; i++)
        {
            if (ApproxEqual(TransformDirection(i, lv), wv) &&
                ApproxEqual(TransformDirection(i, ls), ws))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Finds BLR where localFace→worldFace and localSecondary→worldSecondary.
    /// Throws if no rotation exists.
    /// </summary>
    public static int FindRotation(Face localFace, Face worldFace, Face localSecondary, Face worldSecondary)
    {
        int blr = TryFindRotation(localFace, worldFace, localSecondary, worldSecondary);
        if (blr < 0)
            throw new InvalidOperationException(
                $"No rotation found: {localFace}→{worldFace}, {localSecondary}→{worldSecondary}");
        return blr;
    }

    /// <summary>
    /// Find BLR where localFace maps to worldFace. Prefers rotation where local Up → world Up.
    /// Returns defaultBlr if no match.
    /// </summary>
    public static int FindRotationOrDefault(Face localFace, Face worldFace, int defaultBlr = 0)
    {
        Vector3 localVec = FaceToVector(localFace);
        Vector3 worldVec = FaceToVector(worldFace);
        int firstMatch = -1;

        for (int i = 0; i < 24; i++)
        {
            if (!ApproxEqual(TransformDirection(i, localVec), worldVec))
                continue;

            if (firstMatch == -1)
                firstMatch = i;

            if (ApproxEqual(TransformDirection(i, Vector3.UnitY), Vector3.UnitY))
                return i;
        }

        return firstMatch >= 0 ? firstMatch : defaultBlr;
    }
}
