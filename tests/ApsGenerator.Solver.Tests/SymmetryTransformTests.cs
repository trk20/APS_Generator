using ApsGenerator.Core;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver;
using System.Reflection;

namespace ApsGenerator.Solver.Tests;

public sealed class SymmetryTransformTests
{
    public enum TransformKind
    {
        HorizontalReflect,
        VerticalReflect,
        Rotate180,
        Rotate90CW,
        Rotate90CCW
    }

    private static readonly IReadOnlyDictionary<TransformKind, MethodInfo> TransformMethods =
        new Dictionary<TransformKind, MethodInfo>
        {
            [TransformKind.HorizontalReflect] = GetTransformMethod("HorizontalReflect"),
            [TransformKind.VerticalReflect] = GetTransformMethod("VerticalReflect"),
            [TransformKind.Rotate180] = GetTransformMethod("Rotate180"),
            [TransformKind.Rotate90CW] = GetTransformMethod("Rotate90CW"),
            [TransformKind.Rotate90CCW] = GetTransformMethod("Rotate90CCW")
        };

    public static IEnumerable<object[]> AllTypeShapeTransformCases()
    {
        foreach (TetrisType type in Enum.GetValues<TetrisType>())
        {
            int shapeCount = ClusterShape.GetShapes(type).Count;
            foreach (TransformKind transform in Enum.GetValues<TransformKind>())
            for (int shapeIndex = 0; shapeIndex < shapeCount; shapeIndex++)
                yield return [type, shapeIndex, transform];
        }
    }

    public static IEnumerable<object[]> AllTypeShapeCases()
    {
        foreach (TetrisType type in Enum.GetValues<TetrisType>())
        {
            int shapeCount = ClusterShape.GetShapes(type).Count;
            for (int shapeIndex = 0; shapeIndex < shapeCount; shapeIndex++)
                yield return [type, shapeIndex];
        }
    }

    [Theory]
    [MemberData(nameof(AllTypeShapeTransformCases))]
    public void ShapeTransformMapping_MatchesGeometricOffsets(
        TetrisType type,
        int shapeIndex,
        TransformKind transform)
    {
        var shapes = ClusterShape.GetShapes(type);
        var grid = CreateGridForTransform(transform);

        var transformedPlacement = ApplyTransform(transform, row: 2, col: 3, shapeIndex, grid, type);
        int mappedShapeIndex = transformedPlacement.ShapeIndex;

        var transformedOffsetSet = new HashSet<(int DeltaRow, int DeltaCol, CellRole Role)>(
            shapes[shapeIndex].Offsets.Select(offset => TransformOffset(offset, transform)));

        var mappedOffsetSet = new HashSet<(int DeltaRow, int DeltaCol, CellRole Role)>(
            shapes[mappedShapeIndex].Offsets.Select(offset => (offset.DeltaRow, offset.DeltaCol, offset.Role)));

        Assert.True(
            transformedOffsetSet.SetEquals(mappedOffsetSet),
            $"{transform} produced mapped shape index {mappedShapeIndex} for type {type} shape {shapeIndex}, but geometric offsets do not match.");
    }

    [Theory]
    [MemberData(nameof(AllTypeShapeCases))]
    public void HorizontalReflect_Twice_IsIdentity(TetrisType type, int shapeIndex)
    {
        var grid = TemplateGenerator.Rectangle(width: 8, height: 6);
        AssertRepeatedTransformIsIdentity(grid, type, shapeIndex, TransformKind.HorizontalReflect, repeatCount: 2);
    }

    [Theory]
    [MemberData(nameof(AllTypeShapeCases))]
    public void VerticalReflect_Twice_IsIdentity(TetrisType type, int shapeIndex)
    {
        var grid = TemplateGenerator.Rectangle(width: 8, height: 6);
        AssertRepeatedTransformIsIdentity(grid, type, shapeIndex, TransformKind.VerticalReflect, repeatCount: 2);
    }

    [Theory]
    [MemberData(nameof(AllTypeShapeCases))]
    public void Rotate180_Twice_IsIdentity(TetrisType type, int shapeIndex)
    {
        var grid = TemplateGenerator.Rectangle(width: 8, height: 6);
        AssertRepeatedTransformIsIdentity(grid, type, shapeIndex, TransformKind.Rotate180, repeatCount: 2);
    }

    [Theory]
    [MemberData(nameof(AllTypeShapeCases))]
    public void Rotate90CW_FourTimes_IsIdentity(TetrisType type, int shapeIndex)
    {
        var grid = TemplateGenerator.Rectangle(width: 7, height: 7);
        AssertRepeatedTransformIsIdentity(grid, type, shapeIndex, TransformKind.Rotate90CW, repeatCount: 4);
    }

    [Theory]
    [MemberData(nameof(AllTypeShapeCases))]
    public void HorizontalPlusVertical_EqualsRotate180(TetrisType type, int shapeIndex)
    {
        var grid = TemplateGenerator.Rectangle(width: 8, height: 6);

        for (int row = 0; row < grid.Height; row++)
        for (int col = 0; col < grid.Width; col++)
        {
            var hThenV = ApplyTransform(TransformKind.HorizontalReflect, row, col, shapeIndex, grid, type);
            hThenV = ApplyTransform(TransformKind.VerticalReflect, hThenV.Row, hThenV.Col, hThenV.ShapeIndex, grid, type);

            var vThenH = ApplyTransform(TransformKind.VerticalReflect, row, col, shapeIndex, grid, type);
            vThenH = ApplyTransform(TransformKind.HorizontalReflect, vThenH.Row, vThenH.Col, vThenH.ShapeIndex, grid, type);

            var rotate180 = ApplyTransform(TransformKind.Rotate180, row, col, shapeIndex, grid, type);

            Assert.Equal(rotate180, hThenV);
            Assert.Equal(rotate180, vThenH);
        }
    }

    [Fact]
    public void HardHorizontalReflection_CellCoverage_IsInvariant_ThreeClip()
    {
        var grid = TemplateGenerator.Circle(diameter: 5, blockCenter: true);
        SolverResult result = SolveWithHardSymmetry(grid, TetrisType.ThreeClip, SymmetryType.HorizontalReflection);

        SymmetryCellAssertions.AssertCellLevelSymmetry(
            grid,
            TetrisType.ThreeClip,
            result,
            static (r, c, g) => (g.Height - 1 - r, c));
    }

    [Fact]
    public void HardVerticalReflection_CellCoverage_IsInvariant_ThreeClip()
    {
        var grid = TemplateGenerator.Circle(diameter: 5, blockCenter: true);
        SolverResult result = SolveWithHardSymmetry(grid, TetrisType.ThreeClip, SymmetryType.VerticalReflection);

        SymmetryCellAssertions.AssertCellLevelSymmetry(
            grid,
            TetrisType.ThreeClip,
            result,
            static (r, c, g) => (r, g.Width - 1 - c));
    }

    [Fact]
    public void HardHorizontalReflection_CellCoverage_IsInvariant_FiveClip()
    {
        var grid = TemplateGenerator.Circle(diameter: 7, blockCenter: true);
        SolverResult result = SolveWithHardSymmetry(grid, TetrisType.FiveClip, SymmetryType.HorizontalReflection);

        SymmetryCellAssertions.AssertCellLevelSymmetry(
            grid,
            TetrisType.FiveClip,
            result,
            static (r, c, g) => (g.Height - 1 - r, c));
    }

    [Fact]
    public void HardVerticalReflection_CellCoverage_IsInvariant_FiveClip()
    {
        var grid = TemplateGenerator.Circle(diameter: 7, blockCenter: true);
        SolverResult result = SolveWithHardSymmetry(grid, TetrisType.FiveClip, SymmetryType.VerticalReflection);

        SymmetryCellAssertions.AssertCellLevelSymmetry(
            grid,
            TetrisType.FiveClip,
            result,
            static (r, c, g) => (r, g.Width - 1 - c));
    }

    [Fact]
    public void HardRotation90_CellCoverage_IsInvariant_ThreeClip()
    {
        var grid = TemplateGenerator.Circle(diameter: 9, blockCenter: true);
        SolverResult result = SolveWithHardSymmetry(grid, TetrisType.ThreeClip, SymmetryType.Rotation90);

        SymmetryCellAssertions.AssertCellLevelSymmetry(
            grid,
            TetrisType.ThreeClip,
            result,
            static (r, c, g) => (c, g.Height - 1 - r));
    }

    [Fact]
    public void HardRotation180_CellCoverage_IsInvariant_ThreeClip()
    {
        var grid = TemplateGenerator.Circle(diameter: 11, blockCenter: true);
        SolverResult result = SolveWithHardSymmetry(grid, TetrisType.ThreeClip, SymmetryType.Rotation180);

        SymmetryCellAssertions.AssertCellLevelSymmetry(
            grid,
            TetrisType.ThreeClip,
            result,
            static (r, c, g) => (g.Height - 1 - r, g.Width - 1 - c));
    }

    [Fact]
    public void HardBothReflection_CellCoverage_IsInvariant_ThreeClip()
    {
        var grid = TemplateGenerator.Circle(diameter: 9, blockCenter: true);
        SolverResult result = SolveWithHardSymmetry(grid, TetrisType.ThreeClip, SymmetryType.BothReflection);

        SymmetryCellAssertions.AssertCellLevelSymmetry(
            grid,
            TetrisType.ThreeClip,
            result,
            static (r, c, g) => (g.Height - 1 - r, c));

        SymmetryCellAssertions.AssertCellLevelSymmetry(
            grid,
            TetrisType.ThreeClip,
            result,
            static (r, c, g) => (r, g.Width - 1 - c));

        SymmetryCellAssertions.AssertCellLevelSymmetry(
            grid,
            TetrisType.ThreeClip,
            result,
            static (r, c, g) => (g.Height - 1 - r, g.Width - 1 - c));
    }

    [Fact]
    public void FiveClip_ConnectionUsage_IsSymmetricUnderHorizontalReflection()
    {
        var fiveClipShapes = ClusterShape.GetShapes(TetrisType.FiveClip);
        foreach (var shape in fiveClipShapes)
            Assert.Equal(CellRole.Connection, shape.Offsets[^1].Role);

        var grid = TemplateGenerator.Circle(diameter: 11, blockCenter: true);
        SolverResult result = SolveWithHardSymmetry(grid, TetrisType.FiveClip, SymmetryType.HorizontalReflection, maxTimeSeconds: 60);

        var connectionUsage = SymmetryCellAssertions.GetConnectionUsageByCell(result, TetrisType.FiveClip);
        Assert.NotEmpty(connectionUsage);

        foreach (var usage in connectionUsage)
        {
            var reflectedCell = (Row: grid.Height - 1 - usage.Key.Row, Col: usage.Key.Col);
            Assert.True(
                connectionUsage.TryGetValue(reflectedCell, out int reflectedUsage),
                $"Reflected connection cell ({reflectedCell.Row}, {reflectedCell.Col}) not present for original connection cell ({usage.Key.Row}, {usage.Key.Col}).");

            Assert.Equal(usage.Value, reflectedUsage);
        }
    }

    private static SolverResult SolveWithHardSymmetry(
        Grid grid,
        TetrisType type,
        SymmetryType symmetryType,
        int maxTimeSeconds = 30)
    {
        var solver = new TetrisSolver();
        return solver.Solve(grid, type, new SolverOptions
        {
            SymmetryType = symmetryType,
            SymmetryMode = SymmetryMode.Hard,
            MaxTimeSeconds = maxTimeSeconds
        });
    }

    private static void AssertRepeatedTransformIsIdentity(
        Grid grid,
        TetrisType type,
        int shapeIndex,
        TransformKind transform,
        int repeatCount)
    {
        for (int row = 0; row < grid.Height; row++)
        for (int col = 0; col < grid.Width; col++)
        {
            int currentRow = row;
            int currentCol = col;
            int currentShapeIndex = shapeIndex;

            for (int i = 0; i < repeatCount; i++)
            {
                var next = ApplyTransform(transform, currentRow, currentCol, currentShapeIndex, grid, type);
                currentRow = next.Row;
                currentCol = next.Col;
                currentShapeIndex = next.ShapeIndex;
            }

            Assert.Equal(row, currentRow);
            Assert.Equal(col, currentCol);
            Assert.Equal(shapeIndex, currentShapeIndex);
        }
    }

    private static Grid CreateGridForTransform(TransformKind transform) =>
        transform is TransformKind.Rotate90CW or TransformKind.Rotate90CCW
            ? TemplateGenerator.Rectangle(width: 9, height: 9)
            : TemplateGenerator.Rectangle(width: 9, height: 7);

    private static (int Row, int Col, int ShapeIndex) ApplyTransform(
        TransformKind transform,
        int row,
        int col,
        int shapeIndex,
        Grid grid,
        TetrisType type)
    {
        MethodInfo method = TransformMethods[transform];
        object? transformed = method.Invoke(null, [row, col, shapeIndex, grid, type]);
        Assert.NotNull(transformed);

        var tuple = (ValueTuple<int, int, int>)transformed!;
        return (tuple.Item1, tuple.Item2, tuple.Item3);
    }

    private static (int DeltaRow, int DeltaCol, CellRole Role) TransformOffset(CellOffset offset, TransformKind transform)
    {
        (int dr, int dc) = transform switch
        {
            TransformKind.HorizontalReflect => (-offset.DeltaRow, offset.DeltaCol),
            TransformKind.VerticalReflect => (offset.DeltaRow, -offset.DeltaCol),
            TransformKind.Rotate180 => (-offset.DeltaRow, -offset.DeltaCol),
            TransformKind.Rotate90CW => (offset.DeltaCol, -offset.DeltaRow),
            TransformKind.Rotate90CCW => (-offset.DeltaCol, offset.DeltaRow),
            _ => throw new ArgumentOutOfRangeException(nameof(transform), transform, null)
        };

        return (dr, dc, offset.Role);
    }

    private static MethodInfo GetTransformMethod(string methodName)
    {
        return typeof(TetrisSolver).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
               ?? throw new InvalidOperationException($"Expected private static method '{methodName}' in TetrisSolver.");
    }
}

internal static class SymmetryCellAssertions
{
    public static void AssertCellLevelSymmetry(
        Grid grid,
        TetrisType type,
        SolverResult result,
        Func<int, int, Grid, (int Row, int Col)> cellTransform)
    {
        var coveredCells = GetCoveredCells(type, result);
        var transformedCells = new HashSet<(int Row, int Col)>();

        foreach (var cell in coveredCells)
        {
            var transformed = cellTransform(cell.Row, cell.Col, grid);
            Assert.True(
                grid.IsInBounds(transformed.Row, transformed.Col),
                $"Transformed cell ({transformed.Row}, {transformed.Col}) is out of bounds for grid {grid.Height}x{grid.Width}.");

            transformedCells.Add(transformed);
        }

        Assert.Equal(coveredCells.Count, transformedCells.Count);
        Assert.True(
            coveredCells.SetEquals(transformedCells),
            "Covered cells are not invariant under the symmetry transform.");
    }

    public static Dictionary<(int Row, int Col), int> GetConnectionUsageByCell(SolverResult result, TetrisType type)
    {
        if (type != TetrisType.FiveClip)
            throw new ArgumentException("Connection usage is only defined for FiveClip.", nameof(type));

        var shapes = ClusterShape.GetShapes(type);
        var usage = new Dictionary<(int Row, int Col), int>();

        foreach (var placement in result.Placements)
        {
            var offsets = shapes[placement.ShapeIndex].Offsets;
            var connectorOffset = offsets[^1];

            Assert.Equal(CellRole.Connection, connectorOffset.Role);

            var cell = (
                Row: placement.Row + connectorOffset.DeltaRow,
                Col: placement.Col + connectorOffset.DeltaCol);

            usage[cell] = usage.TryGetValue(cell, out int count) ? count + 1 : 1;
        }

        return usage;
    }

    private static HashSet<(int Row, int Col)> GetCoveredCells(TetrisType type, SolverResult result)
    {
        var shapes = ClusterShape.GetShapes(type);
        var coveredCells = new HashSet<(int Row, int Col)>();

        foreach (var placement in result.Placements)
        {
            foreach (var offset in shapes[placement.ShapeIndex].Offsets)
                coveredCells.Add((placement.Row + offset.DeltaRow, placement.Col + offset.DeltaCol));
        }

        return coveredCells;
    }
}