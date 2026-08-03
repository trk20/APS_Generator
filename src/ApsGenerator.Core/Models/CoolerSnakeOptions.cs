namespace ApsGenerator.Core.Models;

/// <summary>Options for post-Tetris cooler snake generation.</summary>
public sealed class CoolerSnakeOptions
{
    public const double DefaultMaxTimeSeconds = 60;

    /// <summary>Wall-clock budget for cooler generation (seconds).</summary>
    public double MaxTimeSeconds { get; init; } = DefaultMaxTimeSeconds;

    /// <summary>CMSAT thread count.</summary>
    public int Threads { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);

    /// <summary>
    /// When true, solve with intake-only bottom hardware (no ejectors, zero top deficit).
    /// </summary>
    public bool OmitEjectors { get; init; }

    /// <summary>
    /// Override for undirected ejector random trials. Null uses the solver default.
    /// </summary>
    public int? UndirectedRandomTrials { get; init; }
}
