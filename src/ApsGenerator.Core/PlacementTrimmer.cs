using System.Text;
using ApsGenerator.Core.Models;

namespace ApsGenerator.Core;

public static class PlacementTrimmer
{
    public static IReadOnlyList<Placement> Trim(
        IReadOnlyList<Placement> placements,
        Grid grid,
        TetrisType type,
        SymmetryType symmetryType,
        int targetCount)
    {
        if (targetCount <= 0) return [];
        if (targetCount >= placements.Count) return placements;

        var shapes = ClusterShape.GetShapes(type);
        var cellSets = ComputeCellSets(placements, shapes);
        var orbits = ComputeOrbits(placements, cellSets, grid.Width, grid.Height, symmetryType);
        var connectorPositions = ComputeConnectorPositions(placements, shapes, type);
        ComputeScoringData(orbits, placements, cellSets, grid);

        return RemoveOrbits(orbits, placements, targetCount, connectorPositions);
    }

    private static HashSet<(int R, int C)>[] ComputeCellSets(
        IReadOnlyList<Placement> placements,
        IReadOnlyList<ClusterShape> shapes)
    {
        var result = new HashSet<(int R, int C)>[placements.Count];
        for (int i = 0; i < placements.Count; i++)
        {
            var p = placements[i];
            var offsets = shapes[p.ShapeIndex].Offsets;
            var set = new HashSet<(int R, int C)>(offsets.Count);
            foreach (var o in offsets)
                set.Add((p.Row + o.DeltaRow, p.Col + o.DeltaCol));
            result[i] = set;
        }
        return result;
    }

    private static List<OrbitInfo> ComputeOrbits(
        IReadOnlyList<Placement> placements,
        HashSet<(int R, int C)>[] cellSets,
        int width, int height,
        SymmetryType symmetryType)
    {
        var cellSetLookup = new Dictionary<string, int>(placements.Count);
        for (int i = 0; i < placements.Count; i++)
            cellSetLookup[CellSetKey(cellSets[i])] = i;

        var transforms = GetGroupTransforms(width, height, symmetryType);
        var visited = new bool[placements.Count];
        var orbits = new List<OrbitInfo>();

        for (int i = 0; i < placements.Count; i++)
        {
            if (visited[i]) continue;

            var indices = new List<int> { i };
            visited[i] = true;

            foreach (var transform in transforms)
            {
                var imageSet = new HashSet<(int R, int C)>(cellSets[i].Count);
                foreach (var cell in cellSets[i])
                    imageSet.Add(transform(cell));

                var key = CellSetKey(imageSet);
                if (cellSetLookup.TryGetValue(key, out int mate) && !visited[mate])
                {
                    indices.Add(mate);
                    visited[mate] = true;
                }
            }

            orbits.Add(new OrbitInfo { PlacementIndices = indices });
        }

        return orbits;
    }

    private static (int R, int C)[]? ComputeConnectorPositions(
        IReadOnlyList<Placement> placements,
        IReadOnlyList<ClusterShape> shapes,
        TetrisType type)
    {
        if (type != TetrisType.FiveClip)
            return null;

        var connectorPositions = new (int R, int C)[placements.Count];
        for (int i = 0; i < placements.Count; i++)
        {
            var p = placements[i];
            var offsets = shapes[p.ShapeIndex].Offsets;
            bool found = false;
            foreach (var o in offsets)
            {
                if (o.Role != CellRole.Connection) continue;
                connectorPositions[i] = (p.Row + o.DeltaRow, p.Col + o.DeltaCol);
                found = true;
                break;
            }

            if (!found)
            {
                throw new InvalidOperationException(
                    "Five-clip placement must contain a connection cell.");
            }
        }

        return connectorPositions;
    }

    private static void RecomputeShareCounts(
        List<OrbitInfo> orbits,
        IReadOnlyList<(int R, int C)> connectorPositions)
    {
        var connectorOrbits = new Dictionary<(int R, int C), HashSet<int>>();

        for (int orbitIndex = 0; orbitIndex < orbits.Count; orbitIndex++)
        {
            var orbit = orbits[orbitIndex];
            if (orbit.Removed)
                continue;

            foreach (int placementIndex in orbit.PlacementIndices)
            {
                var connectorPosition = connectorPositions[placementIndex];
                if (!connectorOrbits.TryGetValue(connectorPosition, out var sharingOrbits))
                {
                    sharingOrbits = [];
                    connectorOrbits[connectorPosition] = sharingOrbits;
                }

                sharingOrbits.Add(orbitIndex);
            }
        }

        for (int orbitIndex = 0; orbitIndex < orbits.Count; orbitIndex++)
        {
            var orbit = orbits[orbitIndex];
            if (orbit.Removed)
            {
                orbit.ShareCount = 0;
                continue;
            }

            int shareCount = 0;
            foreach (int placementIndex in orbit.PlacementIndices)
            {
                var connectorPosition = connectorPositions[placementIndex];
                if (!connectorOrbits.TryGetValue(connectorPosition, out var sharingOrbits))
                    continue;

                if (sharingOrbits.Count > 1)
                    shareCount++;
            }

            orbit.ShareCount = shareCount;
        }
    }

    private static void ComputeScoringData(
        List<OrbitInfo> orbits,
        IReadOnlyList<Placement> placements,
        HashSet<(int R, int C)>[] cellSets,
        Grid grid)
    {
        // Bias towards keeping placements near the center
        double centerR = (grid.Height - 1) / 2.0;
        double centerC = (grid.Width - 1) / 2.0;

        foreach (var orbit in orbits)
        {
            // Centroid of all cells in the orbit
            double sumR = 0, sumC = 0;
            int cellCount = 0;
            foreach (int pi in orbit.PlacementIndices)
            {
                foreach (var (r, c) in cellSets[pi])
                {
                    sumR += r;
                    sumC += c;
                    cellCount++;
                }
            }

            double centR = sumR / cellCount;
            double centC = sumC / cellCount;
            double dr = centR - centerR;
            double dc = centC - centerC;
            orbit.CentroidDistSq = dr * dr + dc * dc;

            // Anchor: min (Row, Col) among orbit placements
            int anchorRow = int.MaxValue, anchorCol = int.MaxValue;
            foreach (int pi in orbit.PlacementIndices)
            {
                var p = placements[pi];
                if (p.Row < anchorRow || (p.Row == anchorRow && p.Col < anchorCol))
                {
                    anchorRow = p.Row;
                    anchorCol = p.Col;
                }
            }
            orbit.AnchorRow = anchorRow;
            orbit.AnchorCol = anchorCol;
        }
    }

    private static IReadOnlyList<Placement> RemoveOrbits(
        List<OrbitInfo> orbits,
        IReadOnlyList<Placement> placements,
        int targetCount,
        IReadOnlyList<(int R, int C)>? connectorPositions)
    {
        int current = placements.Count;

        while (current > targetCount)
        {
            if (connectorPositions is not null)
                RecomputeShareCounts(orbits, connectorPositions);

            OrbitInfo? best = null;

            foreach (var orbit in orbits)
            {
                if (orbit.Removed) continue;
                if (best is null || CompareRemovalPriority(orbit, best, current, targetCount) < 0)
                    best = orbit;
            }

            if (best is null) break;

            int overshoot = current - targetCount;
            int nextCount = current - best.Size;
            int undershoot = Math.Max(0, targetCount - nextCount);
            if (undershoot > overshoot)
                break;

            best.Removed = true;
            current = nextCount;
        }

        var remaining = new List<Placement>();
        foreach (var orbit in orbits)
        {
            if (orbit.Removed) continue;
            foreach (int idx in orbit.PlacementIndices)
                remaining.Add(placements[idx]);
        }
        return remaining;
    }

    /// <summary>
    /// Returns negative if <paramref name="a"/> should be removed before <paramref name="b"/>.
    /// </summary>
    private static int CompareRemovalPriority(OrbitInfo a, OrbitInfo b, int current, int target)
    {
        // 1. Lower share-count (5-clip), remove first
        int cmp = a.ShareCount.CompareTo(b.ShareCount);
        if (cmp != 0) return cmp;

        // 2. Best fit: prefer lowest overshoot, then largest size among zero-overshoot
        int overshootA = Math.Max(0, target - (current - a.Size));
        int overshootB = Math.Max(0, target - (current - b.Size));
        cmp = overshootA.CompareTo(overshootB);
        if (cmp != 0) return cmp;

        // Same overshoot: larger size removed first (gets closer to target)
        cmp = b.Size.CompareTo(a.Size);
        if (cmp != 0) return cmp;

        // 3. Farther from center, remove first
        cmp = b.CentroidDistSq.CompareTo(a.CentroidDistSq);
        if (cmp != 0) return cmp;

        // 4. Tie-break: anchor row then col
        cmp = a.AnchorRow.CompareTo(b.AnchorRow);
        if (cmp != 0) return cmp;
        return a.AnchorCol.CompareTo(b.AnchorCol);
    }

    private static string CellSetKey(HashSet<(int R, int C)> cells)
    {
        var sorted = new (int R, int C)[cells.Count];

        int idx = 0;
        foreach (var cell in cells)
            sorted[idx++] = cell;

        Array.Sort(sorted, static (a, b) =>
        {
            int cmp = a.R.CompareTo(b.R);
            return cmp != 0 ? cmp : a.C.CompareTo(b.C);
        });

        var builder = new StringBuilder(cells.Count * 8);
        for (int i = 0; i < sorted.Length; i++)
        {
            if (i > 0)
                builder.Append(';');

            builder.Append(sorted[i].R);
            builder.Append(',');
            builder.Append(sorted[i].C);
        }

        return builder.ToString();
    }

    private static List<Func<(int R, int C), (int R, int C)>> GetGroupTransforms(
        int width, int height, SymmetryType symmetryType)
    {
        int h = height - 1;
        int w = width - 1;

        return symmetryType switch
        {
            SymmetryType.None => [],
            SymmetryType.HorizontalReflection =>
            [
                p => (h - p.R, p.C)
            ],
            SymmetryType.VerticalReflection =>
            [
                p => (p.R, w - p.C)
            ],
            SymmetryType.BothReflection =>
            [
                p => (h - p.R, p.C),
                p => (p.R, w - p.C),
                p => (h - p.R, w - p.C)
            ],
            SymmetryType.Rotation180 =>
            [
                p => (h - p.R, w - p.C)
            ],
            SymmetryType.Rotation90 =>
            [
                p => (p.C, h - p.R),
                p => (h - p.R, w - p.C),
                p => (h - p.C, p.R)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(symmetryType), symmetryType, "Unsupported symmetry type.")
        };
    }

    private sealed class OrbitInfo
    {
        public required List<int> PlacementIndices { get; init; }
        public int ShareCount { get; set; }
        public bool Removed { get; set; }
        public int Size => PlacementIndices.Count;
        public double CentroidDistSq { get; set; }
        public int AnchorRow { get; set; }
        public int AnchorCol { get; set; }
    }
}
