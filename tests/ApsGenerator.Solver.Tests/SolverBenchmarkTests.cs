using System.Diagnostics;
using ApsGenerator.Core;
using ApsGenerator.Core.Models;
using Xunit.Abstractions;

namespace ApsGenerator.Solver.Tests;

public sealed class SolverBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public SolverBenchmarkTests(ITestOutputHelper output) => _output = output;

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
        RunBenchmark(grid, TetrisType.ThreeClip, options, "45x45_3Clip_BothReflection");
    }

    [Fact]
    [Trait("Category", "RegressionBenchmark")]
    public void Benchmark_CenterHole33x33_3Clip_VerticalReflection()
    {
        var grid = TemplateGenerator.Circle(33, blockCenter: true);
        var options = new SolverOptions
        {
            MaxTimeSeconds = 15,
            NumSolutions = 10,
            SymmetryType = SymmetryType.VerticalReflection,
            SymmetryMode = SymmetryMode.Hard,
            EarlyStopEnabled = false
        };
        RunBenchmark(grid, TetrisType.ThreeClip, options, "33x33_3Clip_VerticalReflection");
    }

    [Fact]
    [Trait("Category", "RegressionBenchmark")]
    public void Benchmark_CenterHole29x29_3Clip_NoSymmetry()
    {
        var grid = TemplateGenerator.Circle(29, blockCenter: true);
        var options = new SolverOptions
        {
            MaxTimeSeconds = 20,
            NumSolutions = 10,
            SymmetryType = SymmetryType.None,
            EarlyStopEnabled = false
        };
        RunBenchmark(grid, TetrisType.ThreeClip, options, "29x29_3Clip_NoSymmetry");
    }

    [Fact]
    [Trait("Category", "RegressionBenchmark")]
    public void Benchmark_CenterHole25x25_4Clip_Rotation180()
    {
        var grid = TemplateGenerator.Circle(25, blockCenter: true);
        var options = new SolverOptions
        {
            MaxTimeSeconds = 20,
            NumSolutions = 10,
            SymmetryType = SymmetryType.Rotation180,
            SymmetryMode = SymmetryMode.Hard,
            EarlyStopEnabled = false
        };
        RunBenchmark(grid, TetrisType.FourClip, options, "25x25_4Clip_Rotation180");
    }

    [Fact]
    [Trait("Category", "RegressionBenchmark")]
    public void Benchmark_CenterHole25x25_4Clip_Rotation90()
    {
        var grid = TemplateGenerator.Circle(25, blockCenter: true);
        var options = new SolverOptions
        {
            MaxTimeSeconds = 20,
            NumSolutions = 10,
            SymmetryType = SymmetryType.Rotation90,
            SymmetryMode = SymmetryMode.Hard,
            EarlyStopEnabled = false
        };
        RunBenchmark(grid, TetrisType.FourClip, options, "25x25_4Clip_Rotation90");
    }

    [Fact]
    [Trait("Category", "RegressionBenchmark")]
    public void Benchmark_CenterHole25x25_4Clip_VerticalReflection()
    {
        var grid = TemplateGenerator.Circle(25, blockCenter: true);
        var options = new SolverOptions
        {
            MaxTimeSeconds = 20,
            NumSolutions = 10,
            SymmetryType = SymmetryType.VerticalReflection,
            SymmetryMode = SymmetryMode.Hard,
            EarlyStopEnabled = false
        };
        RunBenchmark(grid, TetrisType.FourClip, options, "25x25_4Clip_VerticalReflection");
    }

    [Fact]
    [Trait("Category", "RegressionBenchmark")]
    public void Benchmark_21x21Rect_3Clip_VerticalReflection()
    {
        var grid = TemplateGenerator.Rectangle(21, 21);
        grid[10, 10] = CellState.Blocked;
        var options = new SolverOptions
        {
            MaxTimeSeconds = 15,
            NumSolutions = 10,
            SymmetryType = SymmetryType.VerticalReflection,
            SymmetryMode = SymmetryMode.Hard,
            EarlyStopEnabled = false
        };
        RunBenchmark(grid, TetrisType.ThreeClip, options, "21x21Rect_3Clip_VerticalReflection");
    }

    private void RunBenchmark(Grid grid, TetrisType type, SolverOptions options, string label)
    {
        var solver = new TetrisSolver();
        var sw = Stopwatch.StartNew();
        var result = solver.Solve(grid, type, options);
        sw.Stop();

        _output.WriteLine($"[{label}] Time: {sw.ElapsedMilliseconds}ms, Solutions: {result.AllSolutions.Count}, Clusters: {result.ClusterCount}, Status: {result.Status}");

        Assert.True(result.AllSolutions.Count > 0, $"[{label}] No solutions found");
    }
}
