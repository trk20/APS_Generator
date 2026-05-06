using System.Numerics;
using ApsGenerator.UI.Services.Export;

namespace ApsGenerator.UI.Tests;

/// <summary>
/// Regression tests locking in the BLR rotation indices that produce correct exports.
/// Each block type defines which local axes map to its game-world target directions.
/// </summary>
public sealed class BlockRotationTests
{
    // Per-block axis definitions derived from the empirically correct rotation maps.
    // Loader: local +Y is the facing axis, local +Z stays vertical (→ world +Y).
    private static readonly Vector3 LoaderPrimary = Vector3.UnitY;
    private static readonly Vector3 LoaderSecondary = Vector3.UnitZ;
    private static readonly Vector3 LoaderSecondaryTarget = Vector3.UnitY;

    // Clip (2-8) and Clip_1 horizontal: local -Y is the connecting-face axis, local +Z stays vertical.
    private static readonly Vector3 ClipPrimary = -Vector3.UnitY;
    private static readonly Vector3 ClipSecondary = Vector3.UnitZ;
    private static readonly Vector3 ClipSecondaryTargetHorizontal = Vector3.UnitY;

    // Clip_1 vertical: same primary, but secondary target is +Z (block lies flat).
    private static readonly Vector3 ClipSecondaryTargetVertical = Vector3.UnitZ;

    // AmmoIntake: local +Z is the connecting-face axis, local +Y is secondary.
    private static readonly Vector3 IntakePrimary = Vector3.UnitZ;
    private static readonly Vector3 IntakeSecondary = Vector3.UnitY;

    // Ejector: local +Z is the ejection axis, local +Y → world -Y (mounted upside-down).
    private static readonly Vector3 EjectorPrimary = Vector3.UnitZ;
    private static readonly Vector3 EjectorSecondary = Vector3.UnitY;
    private static readonly Vector3 EjectorSecondaryTarget = -Vector3.UnitY;

    [Theory]
    [InlineData(0, 0, -1, 10)]  // North → -Z
    [InlineData(1, 0, 0, 9)]   // East → +X
    [InlineData(0, 0, 1, 8)]   // South → +Z
    [InlineData(-1, 0, 0, 11)] // West → -X
    public void Loader_FindRotation_MatchesExistingMap(float tx, float ty, float tz, int expectedBlr)
    {
        Vector3 target = new(tx, ty, tz);
        int blr = BlockRotation.FindRotation(LoaderPrimary, target, LoaderSecondary, LoaderSecondaryTarget);
        Assert.Equal(expectedBlr, blr);
    }

    [Theory]
    [InlineData(0, 0, 1, 10)]   // North → clip faces +Z
    [InlineData(1, 0, 0, 11)]   // East → clip faces +X
    [InlineData(0, 0, -1, 8)]   // South → clip faces -Z
    [InlineData(-1, 0, 0, 9)]   // West → clip faces -X
    public void Clip_FindRotation_MatchesExistingMap(float tx, float ty, float tz, int expectedBlr)
    {
        Vector3 target = new(tx, ty, tz);
        int blr = BlockRotation.FindRotation(ClipPrimary, target, ClipSecondary, ClipSecondaryTargetHorizontal);
        Assert.Equal(expectedBlr, blr);
    }

    [Theory]
    [InlineData(0, 0, 1, 10)]   // North (same as Clip 2-8)
    [InlineData(1, 0, 0, 11)]   // East
    [InlineData(0, 0, -1, 8)]   // South
    [InlineData(-1, 0, 0, 9)]   // West
    public void ClipOne_Horizontal_FindRotation_MatchesExistingMap(float tx, float ty, float tz, int expectedBlr)
    {
        Vector3 target = new(tx, ty, tz);
        int blr = BlockRotation.FindRotation(ClipPrimary, target, ClipSecondary, ClipSecondaryTargetHorizontal);
        Assert.Equal(expectedBlr, blr);
    }

    [Theory]
    [InlineData(0, 1, 0, 12)]   // Up → clip faces +Y
    [InlineData(0, -1, 0, 0)]   // Down → clip faces -Y
    public void ClipOne_Vertical_FindRotation_MatchesExistingMap(float tx, float ty, float tz, int expectedBlr)
    {
        Vector3 target = new(tx, ty, tz);
        int blr = BlockRotation.FindRotation(ClipPrimary, target, ClipSecondary, ClipSecondaryTargetVertical);
        Assert.Equal(expectedBlr, blr);
    }

    [Theory]
    [InlineData(0, 0, 1, 0, 1, 0, 0)]    // North → +Z, secondary → +Y
    [InlineData(1, 0, 0, 0, 1, 0, 1)]    // East → +X, secondary → +Y
    [InlineData(0, 0, -1, 0, 1, 0, 2)]   // South → -Z, secondary → +Y
    [InlineData(-1, 0, 0, 0, 1, 0, 3)]   // West → -X, secondary → +Y
    [InlineData(0, 1, 0, 0, 0, -1, 10)]  // Up → +Y, secondary → -Z
    [InlineData(0, -1, 0, 0, 0, 1, 4)]   // Down → -Y, secondary → +Z
    public void AmmoIntake_FindRotation_MatchesExistingMap(
        float tx, float ty, float tz,
        float sx, float sy, float sz,
        int expectedBlr)
    {
        Vector3 target = new(tx, ty, tz);
        Vector3 secondaryTarget = new(sx, sy, sz);
        int blr = BlockRotation.FindRotation(IntakePrimary, target, IntakeSecondary, secondaryTarget);
        Assert.Equal(expectedBlr, blr);
    }

    [Theory]
    [InlineData(0, 0, 1, 12)]   // North → +Z
    [InlineData(1, 0, 0, 13)]   // East → +X
    [InlineData(0, 0, -1, 14)]  // South → -Z
    [InlineData(-1, 0, 0, 15)]  // West → -X
    public void Ejector_FindRotation_MatchesExistingMap(float tx, float ty, float tz, int expectedBlr)
    {
        Vector3 target = new(tx, ty, tz);
        int blr = BlockRotation.FindRotation(EjectorPrimary, target, EjectorSecondary, EjectorSecondaryTarget);
        Assert.Equal(expectedBlr, blr);
    }

    [Fact]
    public void TransformDirection_Identity_ReturnsInput()
    {
        Assert.Equal(Vector3.UnitX, BlockRotation.TransformDirection(0, Vector3.UnitX));
        Assert.Equal(Vector3.UnitY, BlockRotation.TransformDirection(0, Vector3.UnitY));
        Assert.Equal(Vector3.UnitZ, BlockRotation.TransformDirection(0, Vector3.UnitZ));
    }

    [Fact]
    public void TransformDirection_Rotation1_MapsLocalZToWorldX()
    {
        // BLR=1: forward=+X, up=+Y → local +Z maps to world +X
        Vector3 result = BlockRotation.TransformDirection(1, Vector3.UnitZ);
        Assert.Equal(Vector3.UnitX, result);
    }

    [Fact]
    public void FindRotation_InvalidAxis_Throws()
    {
        // Diagonal vector can't be an axis-aligned result
        Assert.Throws<InvalidOperationException>(() =>
            BlockRotation.FindRotation(Vector3.UnitY, new Vector3(1, 1, 0)));
    }

    [Fact]
    public void FindRotation_SingleAxis_ReturnsFirstMatch()
    {
        // local +Z → world +Z: BLR=0 (identity) is first match
        int blr = BlockRotation.FindRotation(Vector3.UnitZ, Vector3.UnitZ);
        Assert.Equal(0, blr);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(12)]
    [InlineData(23)]
    public void TransformDirection_AllRotations_ProduceBasisVectors(int rotationIndex)
    {
        // Each rotation should produce orthogonal unit vectors for the basis
        Vector3 right = BlockRotation.TransformDirection(rotationIndex, Vector3.UnitX);
        Vector3 up = BlockRotation.TransformDirection(rotationIndex, Vector3.UnitY);
        Vector3 forward = BlockRotation.TransformDirection(rotationIndex, Vector3.UnitZ);

        // Orthogonality: dot products should be ~0
        Assert.True(Math.Abs(Vector3.Dot(right, up)) < 0.001f);
        Assert.True(Math.Abs(Vector3.Dot(right, forward)) < 0.001f);
        Assert.True(Math.Abs(Vector3.Dot(up, forward)) < 0.001f);

        // Unit length
        Assert.True(Math.Abs(right.Length() - 1f) < 0.001f);
        Assert.True(Math.Abs(up.Length() - 1f) < 0.001f);
        Assert.True(Math.Abs(forward.Length() - 1f) < 0.001f);
    }

    [Theory]
    [InlineData(Face.Forward, 0, 0, 1)]
    [InlineData(Face.Back, 0, 0, -1)]
    [InlineData(Face.Right, 1, 0, 0)]
    [InlineData(Face.Left, -1, 0, 0)]
    [InlineData(Face.Up, 0, 1, 0)]
    [InlineData(Face.Down, 0, -1, 0)]
    public void FaceToVector_MapsCorrectly(Face face, float x, float y, float z)
    {
        Vector3 result = BlockRotation.FaceToVector(face);
        Assert.Equal(new Vector3(x, y, z), result);
    }

    [Theory]
    [InlineData(Face.Forward, Face.Back)]
    [InlineData(Face.Right, Face.Left)]
    [InlineData(Face.Up, Face.Down)]
    public void OppositeFace_ReturnsCorrectPair(Face input, Face expected)
    {
        Assert.Equal(expected, BlockRotation.OppositeFace(input));
        Assert.Equal(input, BlockRotation.OppositeFace(expected));
    }

    [Theory]
    [InlineData(Face.Forward)]
    [InlineData(Face.Back)]
    [InlineData(Face.Right)]
    [InlineData(Face.Left)]
    public void Corner_SelectBlock_ProducesValidRotation(Face neighborFace)
    {
        // Corner at BLR=0 connects Down + Back.
        // After rotation, Back face should point toward neighbor, Down should map Up.
        var (blockId, blr) = CoolerBlockProfile.SelectBlock([neighborFace]);

        Assert.Equal(CoolerBlockProfile.CoolerCornerId, blockId);

        // Verify the rotation maps local Back → world neighborFace
        Vector3 localBack = BlockRotation.FaceToVector(Face.Back);
        Vector3 worldNeighbor = BlockRotation.FaceToVector(neighborFace);
        Vector3 transformed = BlockRotation.TransformDirection(blr, localBack);
        Assert.Equal(worldNeighbor.X, transformed.X, 0.001f);
        Assert.Equal(worldNeighbor.Y, transformed.Y, 0.001f);
        Assert.Equal(worldNeighbor.Z, transformed.Z, 0.001f);

        // Verify local Down → world Up (connects upward for stacking)
        Vector3 localDown = BlockRotation.FaceToVector(Face.Down);
        Vector3 worldUp = BlockRotation.FaceToVector(Face.Up);
        Vector3 transformedDown = BlockRotation.TransformDirection(blr, localDown);
        Assert.Equal(worldUp.X, transformedDown.X, 0.001f);
        Assert.Equal(worldUp.Y, transformedDown.Y, 0.001f);
        Assert.Equal(worldUp.Z, transformedDown.Z, 0.001f);
    }

    [Theory]
    [InlineData(Face.Forward, Face.Back)]
    [InlineData(Face.Right, Face.Left)]
    public void Splitter_OppositePair_ProducesValidRotation(Face a, Face b)
    {
        // Splitter at BLR=0 connects Left + Right + Back.
        // After rotation, one lateral maps to a, other to b, branch maps to Up.
        var (blockId, blr) = CoolerBlockProfile.SelectBlock([a, b]);

        Assert.Equal(CoolerBlockProfile.CoolerSplitterId, blockId);

        // Verify the through-axis maps to the connected pair
        Vector3 localLeft = BlockRotation.FaceToVector(Face.Left);
        Vector3 localRight = BlockRotation.FaceToVector(Face.Right);
        Vector3 transformedLeft = BlockRotation.TransformDirection(blr, localLeft);
        Vector3 transformedRight = BlockRotation.TransformDirection(blr, localRight);

        var worldPair = new HashSet<Vector3> { BlockRotation.FaceToVector(a), BlockRotation.FaceToVector(b) };
        Assert.Contains(transformedLeft, worldPair);
        Assert.Contains(transformedRight, worldPair);

        // Verify branch (Back) → Up
        Vector3 localBack = BlockRotation.FaceToVector(Face.Back);
        Vector3 transformedBranch = BlockRotation.TransformDirection(blr, localBack);
        Assert.Equal(Vector3.UnitY, transformedBranch);
    }

    [Theory]
    [InlineData(Face.Forward, Face.Right)]
    [InlineData(Face.Right, Face.Back)]
    [InlineData(Face.Back, Face.Left)]
    [InlineData(Face.Left, Face.Forward)]
    public void FourWay_AdjacentPair_ProducesValidRotation(Face a, Face b)
    {
        // 4-way at BLR=0 has non-connecting: Forward + Up.
        // After rotation, both non-connecting faces map to missing horizontals,
        // so Up AND Down are both connected (vertical pass-through).
        var (blockId, blr) = CoolerBlockProfile.SelectBlock([a, b]);

        Assert.Equal(CoolerBlockProfile.Cooler4WayId, blockId);

        // The 4-way connects Left+Back+Right+Down at BLR=0.
        // Verify that connected faces map to include the requested adjacent pair + both verticals.
        Face[] localConnected = [Face.Left, Face.Back, Face.Right, Face.Down];
        var worldConnectedFaces = new HashSet<Face>();
        foreach (Face localFace in localConnected)
        {
            Vector3 transformed = BlockRotation.TransformDirection(blr, BlockRotation.FaceToVector(localFace));
            worldConnectedFaces.Add(BlockRotation.VectorToFace(transformed));
        }

        Assert.Contains(a, worldConnectedFaces);
        Assert.Contains(b, worldConnectedFaces);
        Assert.Contains(Face.Up, worldConnectedFaces);
        Assert.Contains(Face.Down, worldConnectedFaces);
    }

    [Fact]
    public void FiveWay_AllCardinalNeighbors_NonConnectingPointsDown()
    {
        // 5-way with 4 cardinal neighbors: non-connecting should face Down
        var (blockId, blr) = CoolerBlockProfile.SelectBlock([Face.Forward, Face.Back, Face.Right, Face.Left]);

        Assert.Equal(CoolerBlockProfile.Cooler5WayId, blockId);

        // Local Forward (non-connecting at BLR=0) should map to Down
        Vector3 localForward = BlockRotation.FaceToVector(Face.Forward);
        Vector3 transformed = BlockRotation.TransformDirection(blr, localForward);
        Assert.Equal(-Vector3.UnitY, transformed);
    }

    [Theory]
    [InlineData(Face.Forward)]
    [InlineData(Face.Back)]
    [InlineData(Face.Right)]
    [InlineData(Face.Left)]
    public void FiveWay_ThreeNeighbors_NonConnectingPointsToMissing(Face missingFace)
    {
        // Build 3-neighbor list (all laterals except missingFace)
        Face[] allLaterals = [Face.Forward, Face.Back, Face.Right, Face.Left];
        List<Face> neighbors = allLaterals.Where(f => f != missingFace).ToList();

        var (blockId, blr) = CoolerBlockProfile.SelectBlock(neighbors);

        Assert.Equal(CoolerBlockProfile.Cooler5WayId, blockId);

        // Local Forward (non-connecting at BLR=0) should map to missingFace
        Vector3 localForward = BlockRotation.FaceToVector(Face.Forward);
        Vector3 expected = BlockRotation.FaceToVector(missingFace);
        Vector3 transformed = BlockRotation.TransformDirection(blr, localForward);
        Assert.Equal(expected.X, transformed.X, 0.001f);
        Assert.Equal(expected.Y, transformed.Y, 0.001f);
        Assert.Equal(expected.Z, transformed.Z, 0.001f);
    }

    [Fact]
    public void TryFindRotation_ValidMatch_ReturnsCorrectBlr()
    {
        int blr = BlockRotation.TryFindRotation(Face.Forward, Face.Right, Face.Up, Face.Up);
        Assert.Equal(1, blr);
    }

    [Fact]
    public void TryFindRotation_NoMatch_ReturnsNegativeOne()
    {
        // Forward→Up with Up→Up: two different local axes can't map to the same world axis
        int blr = BlockRotation.TryFindRotation(Face.Forward, Face.Up, Face.Up, Face.Up);
        Assert.Equal(-1, blr);
    }

    [Fact]
    public void FindRotationOrDefault_ValidMatch_ReturnsBlr()
    {
        int blr = BlockRotation.FindRotationOrDefault(Face.Forward, Face.Forward);
        Assert.Equal(0, blr); // Identity: Forward→Forward, prefers Up→Up
    }

    [Fact]
    public void FindRotationOrDefault_PrefersUpToUp()
    {
        // Forward→Right: multiple rotations match. Should prefer one where Up→Up.
        int blr = BlockRotation.FindRotationOrDefault(Face.Forward, Face.Right);
        // BLR=1 has Forward→Right and Up→Up
        Assert.Equal(1, blr);
    }

    [Fact]
    public void FindRotationOrDefault_ValidMapping_ReturnsCorrectTransform()
    {
        // Verify Forward→Down returns a valid BLR that correctly transforms
        int blr = BlockRotation.FindRotationOrDefault(Face.Forward, Face.Down);
        Assert.True(blr >= 0 && blr < 24);
        Vector3 transformed = BlockRotation.TransformDirection(blr, BlockRotation.FaceToVector(Face.Forward));
        Assert.Equal(BlockRotation.FaceToVector(Face.Down), transformed);
    }
}
