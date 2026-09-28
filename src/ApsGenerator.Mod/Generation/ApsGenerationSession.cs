using System.Diagnostics;

namespace ApsGenerator.Mod.Generation;

internal interface IApsGenerationSolver
{
    ApsSolution Solve(ApsGenerationRequest request, CancellationToken cancellationToken);
    ApsSolution Reconfigure(ApsSolution existing, ApsGenerationRequest request, CancellationToken cancellationToken);
}

internal sealed record ApsGenerationCompletion(ApsSolution? Solution, Exception? Error);

internal sealed class ApsGenerationSession(IApsGenerationSolver solver) : IDisposable
{
    private Operation? operation;
    private ApsGenerationRequest? pendingRefresh;
    private int revision;

    internal ApsSolution? Solution { get; private set; }
    internal bool IsBusy => operation is not null;
    internal bool IsRefreshing => operation?.IsRefresh == true;
    internal double ElapsedSeconds => operation?.Elapsed.Elapsed.TotalSeconds ?? 0;
    internal double LastSolveSeconds { get; private set; }

    internal void Generate(ApsGenerationRequest request)
    {
        if (IsBusy) return;
        Solution = null;
        pendingRefresh = null;
        Start(token => solver.Solve(request, token), isRefresh: false);
    }

    internal void Refresh(ApsGenerationRequest request)
    {
        if (operation is not null)
        {
            pendingRefresh = request;
            if (operation.IsRefresh) operation.Cancellation.Cancel();
            return;
        }
        if (Solution is not ApsSolution existing) return;
        Start(token => solver.Reconfigure(existing, request, token), isRefresh: true);
    }

    internal void Cancel()
    {
        revision++;
        pendingRefresh = null;
        operation?.Cancellation.Cancel();
    }

    internal void Clear()
    {
        Cancel();
        Solution = null;
    }

    internal ApsGenerationCompletion? Poll()
    {
        if (operation is not { Task.IsCompleted: true } finished) return null;
        operation = null;
        bool current = finished.Revision == revision && !finished.Cancellation.IsCancellationRequested;
        finished.Cancellation.Dispose();
        Exception? error = finished.Task.Exception?.GetBaseException();
        ApsGenerationCompletion? completion = null;
        if (current && finished.Task.Status == TaskStatus.RanToCompletion)
        {
            Solution = finished.Task.Result;
            if (!finished.IsRefresh) LastSolveSeconds = finished.Elapsed.Elapsed.TotalSeconds;
            completion = new(Solution, null);
        }
        else if (current && error is not null)
            completion = new(null, error);

        if (pendingRefresh is ApsGenerationRequest request && Solution is not null)
        {
            pendingRefresh = null;
            Refresh(request);
            return null;
        }
        pendingRefresh = null;
        return completion;
    }

    private void Start(Func<CancellationToken, ApsSolution> solve, bool isRefresh)
    {
        var cancellation = new CancellationTokenSource();
        var elapsed = Stopwatch.StartNew();
        Task<ApsSolution> task = Task.Run(() => solve(cancellation.Token), cancellation.Token);
        operation = new(task, cancellation, elapsed, revision, isRefresh);
    }

    public void Dispose()
    {
        Clear();
        if (operation is not Operation unfinished) return;
        operation = null;
        _ = unfinished.Task.ContinueWith(completed =>
        {
            _ = completed.Exception;
            unfinished.Cancellation.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private sealed record Operation(Task<ApsSolution> Task, CancellationTokenSource Cancellation,
        Stopwatch Elapsed, int Revision, bool IsRefresh);
}
