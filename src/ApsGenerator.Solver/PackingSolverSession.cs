using System.Diagnostics;
using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver;

internal sealed class PackingSolverSession : IDisposable
{
    private readonly SolverOptions options;
    private readonly Stopwatch stopwatch;
    private readonly CancellationToken cancellation;
    private readonly CancellationTokenRegistration registration;

    public SatSolver Solver { get; } = new();
    public int NextVariable;
    public double RemainingSeconds => options.MaxTimeSeconds - stopwatch.Elapsed.TotalSeconds;

    public PackingSolverSession(PackingProblem problem, SolverOptions options,
        Stopwatch stopwatch, CancellationToken cancellation)
    {
        this.options = options;
        this.stopwatch = stopwatch;
        this.cancellation = cancellation;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            problem.Configure(Solver, ref NextVariable);
            registration = cancellation.Register(Solver.Interrupt);
        }
        catch
        {
            Solver.Dispose();
            throw;
        }
    }

    public CryptoMiniSatNative.Lbool Solve(int? assumption = null, bool useAssumptions = false)
    {
        cancellation.ThrowIfCancellationRequested();
        double remaining = RemainingSeconds;
        if (remaining <= 0)
            return CryptoMiniSatNative.Lbool.Undef;
        Solver.SetMaxTime(remaining * options.MaxThreads);
        var result = useAssumptions
            ? Solver.SolveWithAssumptions(assumption.HasValue ? [assumption.Value] : [])
            : Solver.Solve();
        cancellation.ThrowIfCancellationRequested();
        return result;
    }

    public void Dispose()
    {
        registration.Dispose();
        Solver.Dispose();
    }
}
