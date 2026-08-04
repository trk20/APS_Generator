using System.Diagnostics;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver.Cooler;
using Avalonia.Threading;

namespace ApsGenerator.UI.Services;

/// <summary>
/// Owns cooler-snake debounce, cancellation, options, and export caching for the UI.
/// </summary>
public sealed class CoolerSolveSession : IDisposable
{
    private readonly CoolerSnakeSolver solver;
    private readonly DispatcherTimer debounceTimer;
    private CancellationTokenSource? cts;
    private int generation;
    private bool disposed;

    public CoolerSolveSession(CoolerSnakeSolver solver)
    {
        this.solver = solver ?? throw new ArgumentNullException(nameof(solver));
        debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        debounceTimer.Tick += OnDebounceTick;
    }

    public CoolerSnakeResult? Result { get; private set; }
    public bool IsBusy { get; private set; }

    public event Action? StateChanged;

    private Func<CoolerOverlaySolveRequest?>? overlayRequestFactory;

    public void ConfigureOverlay(Func<CoolerOverlaySolveRequest?> requestFactory)
    {
        overlayRequestFactory = requestFactory;
    }

    public void SeedCache(CoolerSnakeResult sat)
    {
        ArgumentNullException.ThrowIfNull(sat);
        if (sat.Status != CoolerSnakeStatus.Sat)
            throw new ArgumentException("Only SAT results can seed the cooler cache.", nameof(sat));

        Result = sat;
        RaiseStateChanged();
    }

    /// <summary>
    /// True when export must re-solve (intake-only mode cannot reuse the ejector overlay cache,
    /// or no cached result exists yet).
    /// </summary>
    public bool NeedsFreshExportSolve(ExportExtraLayers layers) =>
        layers.OmitEjectorsForCoolerSolve() || Result is null;

    public void ClearResult()
    {
        CancelPending();
        Result = null;
        IsBusy = false;
        RaiseStateChanged();
    }

    public void ScheduleOverlaySolve()
    {
        debounceTimer.Stop();

        // SAT overlay is a successful cache; non-SAT must not block retry.
        if (Result is { Status: CoolerSnakeStatus.Sat })
            return;

        var request = overlayRequestFactory?.Invoke();
        if (request is null)
        {
            CancelPending();
            Result = null;
            IsBusy = false;
            RaiseStateChanged();
            return;
        }

        debounceTimer.Start();
    }

    public async Task<CoolerSnakeResult?> SolveForExportAsync(
        CoolerExportSolveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.OmitEjectors && Result is { Status: CoolerSnakeStatus.Sat } cached)
            return cached;

        try
        {
            CoolerSnakeResult solved = await RunSolveAsync(
                request.Grid,
                request.TetrisType,
                request.Placements,
                request.Threads,
                request.MaxTimeSeconds,
                request.OmitEjectors,
                cancellationToken).ConfigureAwait(true);

            // Keep overlay/ejector cache intact when solving intake-only for export.
            if (!request.OmitEjectors && solved.Status == CoolerSnakeStatus.Sat)
                Result = solved;

            return solved;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Cooler export solve failed: {ex}");
            throw;
        }
    }

    public void CancelPending()
    {
        debounceTimer.Stop();
        cts?.Cancel();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        debounceTimer.Stop();
        debounceTimer.Tick -= OnDebounceTick;
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    private async void OnDebounceTick(object? sender, EventArgs e)
    {
        debounceTimer.Stop();
        await RunOverlaySolveAsync().ConfigureAwait(true);
    }

    private async Task RunOverlaySolveAsync()
    {
        if (Result is { Status: CoolerSnakeStatus.Sat })
            return;

        var request = overlayRequestFactory?.Invoke();
        if (request is null)
        {
            IsBusy = false;
            RaiseStateChanged();
            return;
        }

        cts?.Cancel();
        cts?.Dispose();
        cts = new CancellationTokenSource();
        var ct = cts.Token;
        var gen = ++generation;

        IsBusy = true;
        RaiseStateChanged();

        CoolerSnakeResult? result = await TryOverlaySolveAsync(request, ct, gen).ConfigureAwait(true);
        if (result is null || gen != generation || ct.IsCancellationRequested)
            return;

        Result = result;
        IsBusy = false;
        RaiseStateChanged();
    }

    private async Task<CoolerSnakeResult?> TryOverlaySolveAsync(
        CoolerOverlaySolveRequest request,
        CancellationToken ct,
        int gen)
    {
        try
        {
            return await RunSolveAsync(
                request.Grid,
                request.TetrisType,
                request.Placements,
                request.Threads,
                request.MaxTimeSeconds,
                omitEjectors: false,
                ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Cooler overlay solve failed: {ex}");
            return gen == generation
                ? new CoolerSnakeResult
                {
                    Status = CoolerSnakeStatus.Error,
                    Detail = $"error: {ex.Message}",
                }
                : null;
        }
    }

    private Task<CoolerSnakeResult> RunSolveAsync(
        Grid grid,
        TetrisType type,
        IReadOnlyList<Placement> placements,
        int threads,
        double maxTimeSeconds,
        bool omitEjectors,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
            solver.Solve(
                grid,
                type,
                placements,
                new CoolerSnakeOptions
                {
                    Threads = threads,
                    MaxTimeSeconds = maxTimeSeconds,
                    OmitEjectors = omitEjectors,
                },
                cancellationToken), cancellationToken);

    private void RaiseStateChanged() => StateChanged?.Invoke();
}

public sealed record CoolerOverlaySolveRequest(
    Grid Grid,
    TetrisType TetrisType,
    IReadOnlyList<Placement> Placements,
    int Threads,
    double MaxTimeSeconds);

public sealed record CoolerExportSolveRequest(
    Grid Grid,
    TetrisType TetrisType,
    IReadOnlyList<Placement> Placements,
    int Threads,
    double MaxTimeSeconds,
    bool OmitEjectors = false);
