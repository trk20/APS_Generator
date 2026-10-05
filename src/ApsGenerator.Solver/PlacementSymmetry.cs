using ApsGenerator.Core.Models;
using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver;

internal static class PlacementSymmetry
{
    public static void Add(
        SatSolver solver,
        Grid grid,
        List<Placement> placements,
        IReadOnlyList<ClusterShape> shapes,
        SymmetryType symmetryType,
        SymmetryMode symmetryMode)
    {
        if (symmetryType == SymmetryType.None)
            return;

        var transforms = GetSymmetryTransforms(symmetryType, grid);
        var lookup = new Dictionary<(int, int, int), int>(placements.Count);
        for (int i = 0; i < placements.Count; i++)
        {
            var p = placements[i];
            lookup[(p.Row, p.Col, p.ShapeIndex)] = i;
        }

        var visited = new HashSet<int>();
        var orbitBuffer = new List<int>();

        for (int i = 0; i < placements.Count; i++)
        {
            if (!visited.Add(i))
                continue;

            orbitBuffer.Clear();
            ComputeOrbit(i, placements, shapes, lookup, grid, transforms, orbitBuffer);

            foreach (int member in orbitBuffer)
                visited.Add(member);

            bool complete = IsCompleteOrbit(orbitBuffer, placements, shapes, lookup, grid, transforms);
            if (!complete || OrbitMembersShareCells(orbitBuffer, placements, shapes))
            {
                if (symmetryMode == SymmetryMode.Hard)
                    foreach (int member in orbitBuffer)
                        solver.AddClause([-(member + 1)]);
                continue;
            }
            AddOrbitEquivalences(solver, orbitBuffer);
        }
    }

    private static bool IsCompleteOrbit(List<int> orbit, List<Placement> placements,
        IReadOnlyList<ClusterShape> shapes, Dictionary<(int, int, int), int> lookup, Grid grid,
        IReadOnlyList<Func<int, int, int, Grid, TetrisType, (int r, int c, int shapeIdx)>> transforms)
    {
        foreach (int member in orbit)
        {
            var placement = placements[member];
            var type = shapes[placement.ShapeIndex].Type;
            foreach (var transform in transforms)
            {
                var mapped = transform(placement.Row, placement.Col, placement.ShapeIndex, grid, type);
                if (!lookup.ContainsKey(mapped))
                    return false;
            }
        }
        return true;
    }

    private static void AddOrbitEquivalences(SatSolver solver, List<int> orbit)
    {
        int representative = orbit[0] + 1;
        for (int index = 1; index < orbit.Count; index++)
        {
            int other = orbit[index] + 1;
            solver.AddClause([-representative, other]);
            solver.AddClause([-other, representative]);
        }
    }

    private static void ComputeOrbit(
        int placementIndex,
        List<Placement> placements,
        IReadOnlyList<ClusterShape> shapes,
        Dictionary<(int, int, int), int> lookup,
        Grid grid,
        IReadOnlyList<Func<int, int, int, Grid, TetrisType, (int r, int c, int shapeIdx)>> transforms,
        List<int> orbit)
    {
        orbit.Add(placementIndex);
        var seen = new HashSet<int> { placementIndex };

        // BFS: apply all transforms to all discovered orbit members
        for (int idx = 0; idx < orbit.Count; idx++)
        {
            var p = placements[orbit[idx]];
            var type = shapes[p.ShapeIndex].Type;

            foreach (var transform in transforms)
            {
                var (tr, tc, tsi) = transform(p.Row, p.Col, p.ShapeIndex, grid, type);
                if (lookup.TryGetValue((tr, tc, tsi), out int mapped) && seen.Add(mapped))
                    orbit.Add(mapped);
            }
        }
    }

    private static bool OrbitMembersShareCells(
        List<int> orbit,
        List<Placement> placements,
        IReadOnlyList<ClusterShape> shapes)
    {
        if (orbit.Count <= 1)
            return false;

        // Collect exclusive cells (Loader/Clip) for each orbit member
        var cellSets = new HashSet<(int, int)>[orbit.Count];
        for (int i = 0; i < orbit.Count; i++)
        {
            var p = placements[orbit[i]];
            var offsets = shapes[p.ShapeIndex].Offsets;
            var cells = new HashSet<(int, int)>();
            for (int j = 0; j < offsets.Count; j++)
            {
                if (offsets[j].Role != CellRole.Connection)
                    cells.Add((p.Row + offsets[j].DeltaRow, p.Col + offsets[j].DeltaCol));
            }
            cellSets[i] = cells;
        }

        for (int a = 0; a < orbit.Count; a++)
            for (int b = a + 1; b < orbit.Count; b++)
            {
                if (cellSets[a].Overlaps(cellSets[b]))
                    return true;
            }

        return false;
    }

    private static IReadOnlyList<Func<int, int, int, Grid, TetrisType, (int r, int c, int shapeIdx)>>
        GetSymmetryTransforms(SymmetryType symmetryType, Grid grid)
    {
        if (symmetryType == SymmetryType.Rotation90 && grid.Height != grid.Width)
            throw new ArgumentException("90° rotation symmetry requires a square grid.", nameof(symmetryType));

        return symmetryType switch
        {
            SymmetryType.HorizontalReflection => [HorizontalReflect],
            SymmetryType.VerticalReflection => [VerticalReflect],
            SymmetryType.BothReflection => [HorizontalReflect, VerticalReflect, Rotate180],
            SymmetryType.Rotation180 => [Rotate180],
            SymmetryType.Rotation90 => [Rotate90CW, Rotate180, Rotate90CCW],
            _ => []
        };
    }

    private static (int r, int c, int shapeIdx) HorizontalReflect(
        int r, int c, int shapeIndex, Grid grid, TetrisType type)
    {
        int newR = grid.Height - 1 - r;
        int newSi = type switch
        {
            TetrisType.FourClip => 0,
            TetrisType.FiveClip => shapeIndex switch { 1 => 3, 3 => 1, _ => shapeIndex },
            _ => shapeIndex switch { 0 => 2, 2 => 0, _ => shapeIndex }
        };
        return (newR, c, newSi);
    }

    private static (int r, int c, int shapeIdx) VerticalReflect(
        int r, int c, int shapeIndex, Grid grid, TetrisType type)
    {
        int newC = grid.Width - 1 - c;
        int newSi = type switch
        {
            TetrisType.FourClip => 0,
            TetrisType.FiveClip => shapeIndex switch { 0 => 2, 2 => 0, _ => shapeIndex },
            _ => shapeIndex switch { 1 => 3, 3 => 1, _ => shapeIndex }
        };
        return (r, newC, newSi);
    }

    private static (int r, int c, int shapeIdx) Rotate180(
        int r, int c, int shapeIndex, Grid grid, TetrisType type)
    {
        int newR = grid.Height - 1 - r;
        int newC = grid.Width - 1 - c;
        int newSi = type == TetrisType.FourClip ? 0 : shapeIndex switch
        {
            0 => 2,
            2 => 0,
            1 => 3,
            3 => 1,
            _ => shapeIndex
        };
        return (newR, newC, newSi);
    }

    private static (int r, int c, int shapeIdx) Rotate90CW(
        int r, int c, int shapeIndex, Grid grid, TetrisType type)
    {
        // (r,c) → (c, H-1-r)
        int newR = c;
        int newC = grid.Height - 1 - r;
        int newSi = type == TetrisType.FourClip ? 0 : shapeIndex switch
        {
            0 => 1,
            1 => 2,
            2 => 3,
            3 => 0,
            _ => shapeIndex
        };
        return (newR, newC, newSi);
    }

    private static (int r, int c, int shapeIdx) Rotate90CCW(
        int r, int c, int shapeIndex, Grid grid, TetrisType type)
    {
        // (r,c) → (H-1-c, r)
        int newR = grid.Height - 1 - c;
        int newC = r;
        int newSi = type == TetrisType.FourClip ? 0 : shapeIndex switch
        {
            0 => 3,
            3 => 2,
            2 => 1,
            1 => 0,
            _ => shapeIndex
        };
        return (newR, newC, newSi);
    }
}
