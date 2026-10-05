using System.Diagnostics;
using ApsGenerator.Core;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver.Tests;

public sealed class IncrementalOptimizationTests
{
    [Fact]
    public void CounterAssumptionsCanTightenAndLoosenAfterUnsat()
    {
        using var solver = new SatSolver();
        solver.AddVariables(5);
        int nextVariable = 5;
        var root = SatTotalizer.BuildAtMostKCounter(solver, [1, 2, 3, 4, 5], 4, ref nextVariable);
        int[] allTrue = [1, 2, 3, 4, 5];
        int[] twoTrue = [1, 2, -3, -4, -5];
        Assert.Equal(CryptoMiniSatNative.Lbool.False, solver.SolveWithAssumptions([.. allTrue, -root[4]]));
        Assert.Equal(CryptoMiniSatNative.Lbool.False, solver.SolveWithAssumptions([.. allTrue, -root[2]]));
        Assert.Equal(CryptoMiniSatNative.Lbool.True, solver.SolveWithAssumptions([.. twoTrue, -root[4]]));
        Assert.Equal(CryptoMiniSatNative.Lbool.True, solver.SolveWithAssumptions([.. twoTrue, -root[2]]));
        Assert.Equal(CryptoMiniSatNative.Lbool.False, solver.SolveWithAssumptions([.. twoTrue, -root[1]]));
        Assert.Equal(CryptoMiniSatNative.Lbool.True, solver.SolveWithAssumptions([.. twoTrue, -root[3]]));
    }

    [Theory]
    [InlineData(SolverStatus.TargetDensityReached)]
    [InlineData(SolverStatus.LikelyOptimal)]
    [InlineData(SolverStatus.TimedOut)]
    public void EnumerationOfAnUnprovedPackingKeepsOneObjective(SolverStatus status)
    {
        var grid = TemplateGenerator.Rectangle(5, 5);
        var options = new SolverOptions { MaxThreads = 1, MaxTimeSeconds = 5, NumSolutions = 3 };
        var problem = new PackingProblem(grid, TetrisType.ThreeClip, options);
        var firstModel = new bool?[problem.Placements.Count];
        firstModel[0] = true;
        var first = problem.Decode(firstModel, status);
        var result = PackingEnumeration.Solve(problem, firstModel, first, options,
            Stopwatch.StartNew(), CancellationToken.None);
        Assert.Equal(status, result.Status);
        Assert.Equal(3, result.AllSolutions.Count);
        Assert.Equal(1, result.ClusterCount);
        var signatures = new HashSet<string>();
        foreach (var solution in result.AllSolutions)
        {
            Assert.Single(solution);
            Assert.Equal(result.EmptyCells, PlacementCoverage.EmptyExclusiveCellCount(
                solution, ClusterShape.GetShapes(TetrisType.ThreeClip), grid.AvailableCellCount));
            Assert.True(signatures.Add(string.Join(";", solution)));
        }
    }

    [Theory]
    [InlineData(TetrisType.ThreeClip)]
    [InlineData(TetrisType.FourClip)]
    [InlineData(TetrisType.FiveClip)]
    public void TargetSolvesReturnDistinctLayoutsWithMatchingCoverage(TetrisType type)
    {
        var grid = TemplateGenerator.Rectangle(7, 7);
        var options = new SolverOptions
        {
            MaxThreads = 1, MaxTimeSeconds = 5, NumSolutions = 3,
            TargetClusterCount = 1, EarlyStopEnabled = false
        };
        var result = new TetrisSolver().Solve(grid, type, options);
        Assert.Equal(SolverStatus.TargetDensityReached, result.Status);
        Assert.Equal(3, result.AllSolutions.Count);
        var signatures = new HashSet<string>();
        foreach (var solution in result.AllSolutions)
        {
            Assert.Equal(result.ClusterCount, solution.Count);
            Assert.Equal(result.EmptyCells, PlacementCoverage.EmptyExclusiveCellCount(
                solution, ClusterShape.GetShapes(type), grid.AvailableCellCount));
            Assert.True(signatures.Add(string.Join(";", solution.OrderBy(p => p.Row)
                .ThenBy(p => p.Col).ThenBy(p => p.ShapeIndex))));
        }
    }

    [Fact]
    public void PreCancelledSolveThrowsBeforeCreatingNativeSolver()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new TetrisSolver().Solve(
            TemplateGenerator.Rectangle(5, 5), TetrisType.ThreeClip, ct: cancellation.Token));
    }

    [Fact]
    public void CancellationDuringOptimizationInterruptsTheNativeSolver()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var watch = Stopwatch.StartNew();
        Assert.Throws<OperationCanceledException>(() => new TetrisSolver().Solve(
            TemplateGenerator.Circle(49, blockCenter: false), TetrisType.FourClip,
            new SolverOptions { MaxThreads = 1, MaxTimeSeconds = 30, EarlyStopEnabled = false }, cancellation.Token));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ZeroBudgetReturnsTimedOutWithoutAnyLayouts()
    {
        var result = new TetrisSolver().Solve(TemplateGenerator.Rectangle(5, 5), TetrisType.ThreeClip,
            new SolverOptions { MaxThreads = 1, MaxTimeSeconds = 0 });
        Assert.Equal(SolverStatus.TimedOut, result.Status);
        Assert.Empty(result.AllSolutions);
    }
}
