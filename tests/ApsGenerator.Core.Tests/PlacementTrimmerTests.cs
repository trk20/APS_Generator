using ApsGenerator.Core;
using ApsGenerator.Core.Models;

namespace ApsGenerator.Core.Tests;

public sealed class PlacementTrimmerTests
{
    [Fact]
    public void TargetCountGreaterOrEqual_ReturnsUnchanged()
    {
        var grid = new Grid(7, 7);
        var placements = PlacementEnumerator.Enumerate(grid, TetrisType.ThreeClip);

        var result = PlacementTrimmer.Trim(
            placements, grid, TetrisType.ThreeClip, SymmetryType.None, placements.Count + 10);

        Assert.Same(placements, result);
    }

    [Fact]
    public void TargetCountZero_ReturnsEmpty()
    {
        var grid = new Grid(7, 7);
        var placements = PlacementEnumerator.Enumerate(grid, TetrisType.ThreeClip);

        var result = PlacementTrimmer.Trim(
            placements, grid, TetrisType.ThreeClip, SymmetryType.None, 0);

        Assert.Empty(result);
    }

    [Fact]
    public void TargetCountNegative_ReturnsEmpty()
    {
        var grid = new Grid(7, 7);
        var placements = PlacementEnumerator.Enumerate(grid, TetrisType.ThreeClip);

        var result = PlacementTrimmer.Trim(
            placements, grid, TetrisType.ThreeClip, SymmetryType.None, -5);

        Assert.Empty(result);
    }

    [Fact]
    public void ThreeClip_NoSymmetry_TrimFrom5To3()
    {
        // 7x3 grid, all available. 3-clip shapes have 4 cells each.
        // Place 5 placements manually that are all valid.
        var grid = new Grid(7, 3);
        var allPlacements = PlacementEnumerator.Enumerate(grid, TetrisType.ThreeClip);
        Assert.True(allPlacements.Count >= 5, $"Need at least 5 placements, got {allPlacements.Count}");

        var fivePlacements = (IReadOnlyList<Placement>)allPlacements.Take(5).ToList();

        var result = PlacementTrimmer.Trim(
            fivePlacements, grid, TetrisType.ThreeClip, SymmetryType.None, 3);

        Assert.Equal(3, result.Count);
        Assert.All(result, p => Assert.Contains(p, fivePlacements));
    }

    [Fact]
    public void ThreeClip_NoSymmetry_TrimIsDeterministic()
    {
        var grid = new Grid(7, 7);
        var allPlacements = PlacementEnumerator.Enumerate(grid, TetrisType.ThreeClip);
        var placements = (IReadOnlyList<Placement>)allPlacements.Take(8).ToList();

        var result1 = PlacementTrimmer.Trim(
            placements, grid, TetrisType.ThreeClip, SymmetryType.None, 4);
        var result2 = PlacementTrimmer.Trim(
            placements, grid, TetrisType.ThreeClip, SymmetryType.None, 4);

        Assert.Equal(result1, result2);
    }

    [Fact]
    public void Rotation180_RemovesWholeOrbits()
    {
        // 5x5 grid with Rotation180 symmetry.
        // Placements should form pairs under 180° rotation.
        var grid = new Grid(5, 5);
        var allPlacements = PlacementEnumerator.Enumerate(grid, TetrisType.FourClip);

        // FourClip on 5x5 should have the center placement (self-symmetric)
        // plus symmetric pairs. Use all placements.
        Assert.True(allPlacements.Count >= 2);

        int target = allPlacements.Count - 2; // remove at least one orbit
        if (target < 1) target = 1;

        var result = PlacementTrimmer.Trim(
            allPlacements, grid, TetrisType.FourClip, SymmetryType.Rotation180, target);

        Assert.True(result.Count <= target);
        Assert.True(result.Count > 0);

        // Verify remaining placements still respect symmetry:
        // each remaining placement's 180° image should also be in the result
        var resultSet = new HashSet<Placement>(result);
        var shapes = ClusterShape.GetShapes(TetrisType.FourClip);
        foreach (var p in result)
        {
            var offsets = shapes[p.ShapeIndex].Offsets;
            var cellSet = offsets.Select(o => (R: p.Row + o.DeltaRow, C: p.Col + o.DeltaCol)).ToHashSet();
            var rotated = cellSet.Select(c => (R: 4 - c.R, C: 4 - c.C)).ToHashSet();

            // If rotated == cellSet, it's self-symmetric → fine.
            if (rotated.SetEquals(cellSet)) continue;

            // Otherwise the rotated image must also be in the result
            bool foundMate = result.Any(q =>
            {
                var qCells = shapes[q.ShapeIndex].Offsets
                    .Select(o => (R: q.Row + o.DeltaRow, C: q.Col + o.DeltaCol)).ToHashSet();
                return qCells.SetEquals(rotated);
            });
            Assert.True(foundMate, $"Placement at ({p.Row},{p.Col}) has no 180° mate in result");
        }
    }

    [Fact]
    public void FiveClip_ConnectorSharing_PreservesSharedOrbits()
    {
        // Build a scenario where two 5-clip placements share a connector cell,
        // and one orbit has external shares while another doesn't.
        // The non-sharing orbit should be removed first.
        var grid = new Grid(9, 5);
        var allPlacements = PlacementEnumerator.Enumerate(grid, TetrisType.FiveClip);
        Assert.True(allPlacements.Count >= 4,
            $"Need at least 4 five-clip placements, got {allPlacements.Count}");

        // Find two placements that share a connector cell
        var shapes = ClusterShape.GetShapes(TetrisType.FiveClip);
        (int R, int C) ConnectorOf(Placement p)
        {
            var offsets = shapes[p.ShapeIndex].Offsets;
            var conn = offsets.First(o => o.Role == CellRole.Connection);
            return (p.Row + conn.DeltaRow, p.Col + conn.DeltaCol);
        }

        // Group placements by connector position to find sharing pairs
        var byConnector = allPlacements
            .Select((p, i) => (Placement: p, Index: i, Conn: ConnectorOf(p)))
            .GroupBy(x => x.Conn)
            .Where(g => g.Count() >= 2)
            .ToList();

        if (byConnector.Count == 0)
        {
            // No sharing found on this grid — skip meaningful assertion
            return;
        }

        // Pick a sharing group and a non-sharing placement
        var sharingGroup = byConnector[0];
        var sharingPlacements = sharingGroup.Select(x => x.Placement).Take(2).ToList();
        var sharingIndices = sharingGroup.Select(x => x.Index).Take(2).ToHashSet();

        // Find placements not in the sharing group
        var nonSharing = allPlacements
            .Where((_, i) => !sharingIndices.Contains(i))
            .Take(2)
            .ToList();

        if (nonSharing.Count == 0) return;

        var testPlacements = (IReadOnlyList<Placement>)sharingPlacements.Concat(nonSharing).ToList();
        int target = testPlacements.Count - 1;

        var result = PlacementTrimmer.Trim(
            testPlacements, grid, TetrisType.FiveClip, SymmetryType.None, target);

        Assert.Equal(target, result.Count);

        // The sharing placements should be preserved (higher share-count = kept longer)
        foreach (var sp in sharingPlacements)
            Assert.Contains(sp, result);
    }

    [Fact]
    public void FiveClip_NoSymmetry_RecomputesShareCountsAfterRemoval()
    {
        var grid = new Grid(13, 13);

        var firstPairNearCenter = new Placement(6, 8, 0);
        var firstPairFarFromCenter = new Placement(6, 10, 2);
        var secondPairA = new Placement(3, 5, 0);
        var secondPairB = new Placement(3, 7, 2);

        IReadOnlyList<Placement> placements =
        [
            firstPairNearCenter,
            firstPairFarFromCenter,
            secondPairA,
            secondPairB
        ];

        var result = PlacementTrimmer.Trim(
            placements,
            grid,
            TetrisType.FiveClip,
            SymmetryType.None,
            targetCount: 2);

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(firstPairNearCenter, result);
        Assert.DoesNotContain(firstPairFarFromCenter, result);
        Assert.Contains(secondPairA, result);
        Assert.Contains(secondPairB, result);
    }

    [Fact]
    public void FiveClip_Rotation180_InternalShareOrbitRemovedAfterNoShareOrbit()
    {
        bool found = TryBuildRotation180InternalShareScenario(
            out var grid,
            out var placements,
            out var internalShareOrbit,
            out var noShareOrbit);

        Assert.True(found, "Could not build a deterministic internal-share vs no-share orbit scenario.");

        var result = PlacementTrimmer.Trim(
            placements,
            grid,
            TetrisType.FiveClip,
            SymmetryType.Rotation180,
            targetCount: 2);

        Assert.Equal(2, result.Count);
        foreach (var kept in internalShareOrbit)
            Assert.Contains(kept, result);

        foreach (var removed in noShareOrbit)
            Assert.DoesNotContain(removed, result);
    }

    private static bool TryBuildRotation180InternalShareScenario(
        out Grid grid,
        out IReadOnlyList<Placement> placements,
        out IReadOnlyList<Placement> internalShareOrbit,
        out IReadOnlyList<Placement> noShareOrbit)
    {
        var sizes = new[] { 7, 9, 11, 13, 15 };

        foreach (int size in sizes)
        {
            var candidateGrid = new Grid(size, size);
            var allPlacements = PlacementEnumerator.Enumerate(candidateGrid, TetrisType.FiveClip);
            if (allPlacements.Count < 4)
                continue;

            var shapes = ClusterShape.GetShapes(TetrisType.FiveClip);
            var connectorPositions = allPlacements
                .Select(p => GetConnectorPosition(p, shapes))
                .ToArray();

            var rotationPairs = FindRotation180Pairs(allPlacements, shapes, candidateGrid.Width, candidateGrid.Height);
            if (rotationPairs.Count < 2)
                continue;

            foreach (var internalPair in rotationPairs)
            {
                var internalConnectorA = connectorPositions[internalPair.A];
                var internalConnectorB = connectorPositions[internalPair.B];
                if (internalConnectorA != internalConnectorB)
                    continue;

                foreach (var noSharePair in rotationPairs)
                {
                    if (noSharePair == internalPair)
                        continue;

                    var noShareConnectorA = connectorPositions[noSharePair.A];
                    var noShareConnectorB = connectorPositions[noSharePair.B];

                    if (noShareConnectorA == noShareConnectorB)
                        continue;

                    var selectedIndices = new[]
                    {
                        internalPair.A,
                        internalPair.B,
                        noSharePair.A,
                        noSharePair.B
                    };

                    bool internalMembersShare =
                        SharesWithinSelection(internalPair.A, selectedIndices, connectorPositions)
                        && SharesWithinSelection(internalPair.B, selectedIndices, connectorPositions);

                    bool noShareMembersDoNotShare =
                        !SharesWithinSelection(noSharePair.A, selectedIndices, connectorPositions)
                        && !SharesWithinSelection(noSharePair.B, selectedIndices, connectorPositions);

                    if (!internalMembersShare || !noShareMembersDoNotShare)
                        continue;

                    grid = candidateGrid;

                    internalShareOrbit =
                    [
                        allPlacements[internalPair.A],
                        allPlacements[internalPair.B]
                    ];

                    noShareOrbit =
                    [
                        allPlacements[noSharePair.A],
                        allPlacements[noSharePair.B]
                    ];

                    placements =
                    [
                        allPlacements[internalPair.A],
                        allPlacements[internalPair.B],
                        allPlacements[noSharePair.A],
                        allPlacements[noSharePair.B]
                    ];

                    return true;
                }
            }
        }

        grid = new Grid(1, 1);
        placements = [];
        internalShareOrbit = [];
        noShareOrbit = [];
        return false;
    }

    private static List<(int A, int B)> FindRotation180Pairs(
        IReadOnlyList<Placement> placements,
        IReadOnlyList<ClusterShape> shapes,
        int width,
        int height)
    {
        var cellSets = new HashSet<(int R, int C)>[placements.Count];
        var lookup = new Dictionary<string, int>(placements.Count);

        for (int i = 0; i < placements.Count; i++)
        {
            var set = new HashSet<(int R, int C)>();
            foreach (var o in shapes[placements[i].ShapeIndex].Offsets)
                set.Add((placements[i].Row + o.DeltaRow, placements[i].Col + o.DeltaCol));

            cellSets[i] = set;
            lookup[CellSetKey(set)] = i;
        }

        int h = height - 1;
        int w = width - 1;
        var pairs = new List<(int A, int B)>();

        for (int i = 0; i < placements.Count; i++)
        {
            var rotated = new HashSet<(int R, int C)>(cellSets[i].Count);
            foreach (var (r, c) in cellSets[i])
                rotated.Add((h - r, w - c));

            if (!lookup.TryGetValue(CellSetKey(rotated), out int mate))
                continue;

            if (mate > i)
                pairs.Add((i, mate));
        }

        return pairs;
    }

    private static (int R, int C) GetConnectorPosition(
        Placement placement,
        IReadOnlyList<ClusterShape> shapes)
    {
        foreach (var offset in shapes[placement.ShapeIndex].Offsets)
        {
            if (offset.Role != CellRole.Connection)
                continue;

            return (placement.Row + offset.DeltaRow, placement.Col + offset.DeltaCol);
        }

        throw new InvalidOperationException("Five-clip shape must contain a connection cell.");
    }

    private static bool SharesWithinSelection(
        int placementIndex,
        IReadOnlyList<int> selectedIndices,
        IReadOnlyList<(int R, int C)> connectorPositions)
    {
        var connector = connectorPositions[placementIndex];
        foreach (int other in selectedIndices)
        {
            if (other == placementIndex)
                continue;

            if (connectorPositions[other] == connector)
                return true;
        }

        return false;
    }

    private static string CellSetKey(HashSet<(int R, int C)> cells)
    {
        var sorted = cells.ToArray();
        Array.Sort(sorted, static (a, b) =>
        {
            int cmp = a.R.CompareTo(b.R);
            return cmp != 0 ? cmp : a.C.CompareTo(b.C);
        });

        return string.Join(';', sorted.Select(c => $"{c.R},{c.C}"));
    }
}
