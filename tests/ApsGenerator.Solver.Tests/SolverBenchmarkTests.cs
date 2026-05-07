using System.Diagnostics;
using ApsGenerator.Core;
using ApsGenerator.Core.Models;
using Xunit.Abstractions;

namespace ApsGenerator.Solver.Tests;

/// <summary>
/// Benchmarks for the TetrisSolver to track performance regressions over time.
/// All benchmarks have a time limit of about double the expected solve time.
/// Expected cluster counts and solution counts are based on historical runs. 
/// Any change that causes any benchmark to exceed its time limit or produce different results is a critical regression.
/// </summary>
public sealed class SolverBenchmarkTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    [Trait("Category", "RegressionBenchmark")]
    public void Benchmark_CenterHole45x45_3Clip_BothReflection()
    {
        var grid = TemplateGenerator.Circle(45, blockCenter: true);
        var options = new SolverOptions
        {
            MaxTimeSeconds = 30,
            NumSolutions = 10,
            SymmetryType = SymmetryType.BothReflection,
            SymmetryMode = SymmetryMode.Hard,
            EarlyStopEnabled = false
        };
        RunBenchmark(grid, TetrisType.ThreeClip, options, 398, 10, "45x45_3Clip_BothReflection");
    }

    [Fact]
    [Trait("Category", "RegressionBenchmark")]
    public void Benchmark_CenterHole33x33_3Clip_VerticalReflection()
    {
        var grid = TemplateGenerator.Circle(33, blockCenter: true);
        var options = new SolverOptions
        {
            MaxTimeSeconds = 24,
            NumSolutions = 50,
            SymmetryType = SymmetryType.VerticalReflection,
            SymmetryMode = SymmetryMode.Hard,
            EarlyStopEnabled = false
        };
        RunBenchmark(grid, TetrisType.ThreeClip, options, 214, 50, "33x33_3Clip_VerticalReflection");
    }

    [Fact]
    [Trait("Category", "RegressionBenchmark")]
    public void Benchmark_CenterHole29x29_3Clip_NoSymmetry()
    {
        var grid = TemplateGenerator.Circle(29, blockCenter: true);
        var options = new SolverOptions
        {
            MaxTimeSeconds = 6,
            NumSolutions = 10,
            SymmetryType = SymmetryType.None,
            EarlyStopEnabled = false
        };
        RunBenchmark(grid, TetrisType.ThreeClip, options, 166, 10, "29x29_3Clip_NoSymmetry");
    }

    [Fact]
    [Trait("Category", "RegressionBenchmark")]
    public void Benchmark_CenterHole25x25_4Clip_Rotation180()
    {
        var grid = TemplateGenerator.Circle(25, blockCenter: true);
        var options = new SolverOptions
        {
            MaxTimeSeconds = 14,
            NumSolutions = 10,
            SymmetryType = SymmetryType.Rotation180,
            SymmetryMode = SymmetryMode.Hard,
            EarlyStopEnabled = false
        };
        RunBenchmark(grid, TetrisType.FourClip, options, 88, 8, "25x25_4Clip_Rotation180");
    }

    [Fact]
    [Trait("Category", "RegressionBenchmark")]
    public void Benchmark_CenterHole25x25_4Clip_Rotation90()
    {
        var grid = TemplateGenerator.Circle(25, blockCenter: true);
        var options = new SolverOptions
        {
            MaxTimeSeconds = 2,
            NumSolutions = 10,
            SymmetryType = SymmetryType.Rotation90,
            SymmetryMode = SymmetryMode.Hard,
            EarlyStopEnabled = false
        };
        RunBenchmark(grid, TetrisType.FourClip, options, 88, 4, "25x25_4Clip_Rotation90");
    }

    [Fact]
    [Trait("Category", "RegressionBenchmark")]
    public void Benchmark_CenterHole25x25_4Clip_VerticalReflection()
    {
        var grid = TemplateGenerator.Circle(25, blockCenter: true);
        var options = new SolverOptions
        {
            MaxTimeSeconds = 10,
            NumSolutions = 10,
            SymmetryType = SymmetryType.VerticalReflection,
            SymmetryMode = SymmetryMode.Hard,
            EarlyStopEnabled = false
        };
        RunBenchmark(grid, TetrisType.FourClip, options, 84, 10, "25x25_4Clip_VerticalReflection");
    }

    [Fact]
    [Trait("Category", "RegressionBenchmark")]
    public void Benchmark_21x21Rect_3Clip_VerticalReflection()
    {
        var grid = TemplateGenerator.Rectangle(21, 21);
        grid[10, 10] = CellState.Blocked;
        var options = new SolverOptions
        {
            MaxTimeSeconds = 8,
            NumSolutions = 10,
            SymmetryType = SymmetryType.VerticalReflection,
            SymmetryMode = SymmetryMode.Hard,
            EarlyStopEnabled = false
        };
        RunBenchmark(grid, TetrisType.ThreeClip, options, 109, 2, "21x21Rect_3Clip_VerticalReflection");
    }

    [Fact]
    [Trait("Category", "RegressionBenchmark")]
    public void Benchmark_21x21Rect_3Clip_VerticalReflectionCenterLine()
    {
        var grid = TemplateGenerator.Rectangle(20, 21);
        var options = new SolverOptions
        {
            MaxTimeSeconds = 2,
            NumSolutions = 50,
            SymmetryType = SymmetryType.VerticalReflection,
            SymmetryMode = SymmetryMode.Hard,
            EarlyStopEnabled = false,
        };
        RunBenchmark(grid, TetrisType.ThreeClip, options, 104, 50, "21x21Rect_3Clip_VerticalReflectionCenterLine");
    }

    [Fact]
    [Trait("Category", "RegressionBenchmark")]
    public void Benchmark_17x17Rect_5Clip_VerticalReflection()
    {
        var grid = TemplateGenerator.Circle(17, blockCenter: false);
        var options = new SolverOptions
        {
            MaxTimeSeconds = 24,
            NumSolutions = 10,
            SymmetryType = SymmetryType.VerticalReflection,
            SymmetryMode = SymmetryMode.Hard,
            EarlyStopEnabled = false
        };
        RunBenchmark(grid, TetrisType.FiveClip, options, 42, 10, "17x17Circle_5Clip_VerticalReflection");
    }

    private void RunBenchmark(Grid grid, TetrisType type, SolverOptions options, int expectedClusterCount, int expectedNumSolutions, string label)
    {
        var solver = new TetrisSolver();
        var sw = Stopwatch.StartNew();
        CancellationTokenSource cts = new(TimeSpan.FromSeconds(options.MaxTimeSeconds + 1)); // Ensure solver respects MaxTimeSeconds
        var result = solver.Solve(grid, type, options, cts.Token);
        sw.Stop();

        _output.WriteLine($"[{label}] Time: {sw.ElapsedMilliseconds}ms, Solutions: {result.AllSolutions.Count}, Clusters: {result.ClusterCount}, Status: {result.Status}");

        Assert.Equal(SolverStatus.Optimal, result.Status);
        Assert.Equal(expectedClusterCount, result.ClusterCount);
        Assert.Equal(expectedNumSolutions, result.AllSolutions.Count);
    }
}
