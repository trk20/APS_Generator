using System.Diagnostics;

namespace ApsGenerator.Mod.Generation;

internal interface IApsGenerationSolver
{
    ApsSolution Solve(ApsGenerationRequest request, CancellationToken cancellationToken);
    ApsSolution Reconfigure(ApsSolution existing, ApsGenerationRequest request, CancellationToken cancellationToken);
    ApsSolution Next(ApsSolution existing, ApsGenerationRequest request, CancellationToken cancellationToken);
}

internal sealed record ApsGenerationCompletion(ApsSolution? Solution, Exception? Error);

/// <summary>
/// Runs one solver operation at a time off the main thread. Keeps solutions of the
/// current problem cached so Previous/Next can step through them without solving again, and after
/// each new solution it solves the next one in the background so Next usually completes at once.
/// </summary>
internal sealed class ApsGenerationSession(IApsGenerationSolver solver) : IDisposable
{
    private readonly List<ApsSolution> history = [];
    private int position = -1;
    /// <summary>Set if Next gets UNSAT to stop queuing searches.</summary>
    private bool exhausted;
    private Operation? operation;
    private Prefetch? prefetch;
    private ApsGenerationRequest? pendingRefresh;
    private int revision;

    internal ApsSolution? Solution { get; private set; }
    internal bool IsBusy => operation is not null;
    internal bool IsRefreshing => operation?.Kind == OperationKind.Refresh;
    internal bool CanShowPrevious => position > 0;
    internal double ElapsedSeconds => operation?.Elapsed.Elapsed.TotalSeconds ?? 0;
    internal double LastSolveSeconds => Solution?.SolveSeconds ?? 0;

    internal void Generate(ApsGenerationRequest request)
    {
        if (IsBusy) return;
        ResetHistory();
        pendingRefresh = null;
        Start(token => solver.Solve(request, token), OperationKind.Generate);
    }

    /// <summary>
    /// Shows the next solution: from the history when stepping forward again, otherwise from
    /// the prefetch or a new search. The current solution stays until it completes.
    /// </summary>
    internal void Next(ApsGenerationRequest request)
    {
        if (IsBusy || Solution is not ApsSolution existing) return;
        if (existing.Request.Problem != request.Problem)
        {
            Generate(request);
            return;
        }
        pendingRefresh = null;
        if (position < history.Count - 1)
        {
            Navigate(position + 1, request);
            return;
        }
        if (exhausted)
        {
            Start(_ => existing with { Request = request, NextLayoutOutcome = ApsNextLayoutOutcome.Exhausted },
                OperationKind.Next);
            return;
        }
        if (TakePrefetch(existing) is Prefetch ready)
        {
            Start(token => UsePrefetch(ready, existing, request, token), OperationKind.Next, elapsed: ready.Elapsed);
            return;
        }
        Start(token => solver.Next(existing, request, token), OperationKind.Next);
    }

    /// <summary>Shows the previous solution, adapting it to the current prefab options if needed.</summary>
    internal void Previous(ApsGenerationRequest request)
    {
        if (IsBusy || !CanShowPrevious || Solution?.Request.Problem != request.Problem) return;
        pendingRefresh = null;
        Navigate(position - 1, request);
    }

    private void Navigate(int index, ApsGenerationRequest request)
    {
        ApsSolution target = history[index];
        Start(token => target.Request == request ? target : solver.Reconfigure(target, request, token),
            OperationKind.Navigate, index);
    }

    /// <summary>
    /// Waits for a prefetched solution (usually already finished) and adapts it to the current
    /// prefab options. Falls back to a fresh search if the prefetch failed.
    /// </summary>
    private ApsSolution UsePrefetch(Prefetch ready, ApsSolution existing, ApsGenerationRequest request,
        CancellationToken token)
    {
        CancellationTokenRegistration link = token.Register(ready.Cancellation.Cancel);
        ApsSolution next;
        try
        {
            next = ready.Task.GetAwaiter().GetResult();
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            return solver.Next(existing, request, token);
        }
        finally
        {
            // Unlink before disposing so a late Cancel can't reach a disposed source.
            link.Dispose();
            ready.Cancellation.Dispose();
        }
        token.ThrowIfCancellationRequested();
        if (next.NextLayoutOutcome != ApsNextLayoutOutcome.None)
            return existing with { Request = request, NextLayoutOutcome = next.NextLayoutOutcome };
        return next.Request == request ? next : solver.Reconfigure(next, request, token);
    }

    internal void Refresh(ApsGenerationRequest request)
    {
        if (operation is not null)
        {
            pendingRefresh = request;
            if (operation.Kind == OperationKind.Refresh) operation.Cancellation.Cancel();
            return;
        }
        if (Solution is not ApsSolution existing) return;
        Start(token => solver.Reconfigure(existing, request, token), OperationKind.Refresh);
    }

    internal void Cancel()
    {
        revision++;
        pendingRefresh = null;
        operation?.Cancellation.Cancel();
        CancelPrefetch();
    }

    internal void Clear()
    {
        Cancel();
        ResetHistory();
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
            Solution = Record(finished, finished.Task.Result);
            completion = new(Solution, null);
        }
        else if (current && error is not null)
            completion = new(null, error);
        if (current)
            StartPrefetchIfNeeded();

        if (pendingRefresh is ApsGenerationRequest request && Solution is not null)
        {
            pendingRefresh = null;
            Refresh(request);
            return null;
        }
        pendingRefresh = null;
        return completion;
    }

    /// <summary>Places a finished result in the history and returns the solution to show.</summary>
    private ApsSolution Record(Operation finished, ApsSolution result)
    {
        switch (finished.Kind)
        {
            case OperationKind.Generate:
                result = result with { SolveSeconds = finished.Elapsed.Elapsed.TotalSeconds };
                history.Clear();
                history.Add(result);
                position = 0;
                break;
            case OperationKind.Next when result.NextLayoutOutcome == ApsNextLayoutOutcome.None:
                result = result with { SolveSeconds = finished.Elapsed.Elapsed.TotalSeconds };
                history.Add(result);
                position = history.Count - 1;
                break;
            case OperationKind.Next:
                // No further solution: keep showing the current one without recording the outcome.
                exhausted |= result.NextLayoutOutcome == ApsNextLayoutOutcome.Exhausted;
                break;
            case OperationKind.Navigate:
                position = finished.TargetIndex;
                history[position] = result;
                break;
            case OperationKind.Refresh when position >= 0:
                history[position] = result;
                break;
        }
        return result;
    }

    private void ResetHistory()
    {
        CancelPrefetch();
        history.Clear();
        position = -1;
        exhausted = false;
        Solution = null;
    }

    private void Start(Func<CancellationToken, ApsSolution> solve, OperationKind kind, int targetIndex = -1,
        Stopwatch? elapsed = null)
    {
        var cancellation = new CancellationTokenSource();
        elapsed ??= Stopwatch.StartNew();
        Task<ApsSolution> task = Task.Run(() => solve(cancellation.Token), cancellation.Token);
        operation = new(task, cancellation, elapsed, revision, kind, targetIndex);
    }

    /// <summary>Starts solving the solution after the newest one unless it's already running or exhausted.</summary>
    private void StartPrefetchIfNeeded()
    {
        if (exhausted || history.Count == 0 || Solution?.NextLayoutOutcome != ApsNextLayoutOutcome.None)
            return;
        ApsSolution current = history[^1];
        if (current.Result.Placements.Count == 0)
            return;
        if (prefetch is not null && prefetch.Matches(current))
            return;
        CancelPrefetch();
        var cancellation = new CancellationTokenSource();
        var elapsed = Stopwatch.StartNew();
        ApsGenerationRequest request = current.Request;
        Task<ApsSolution> task = Task.Run(() =>
        {
            ApsSolution next = solver.Next(current, request, cancellation.Token);
            elapsed.Stop();
            return next;
        }, cancellation.Token);
        prefetch = new(current, task, cancellation, elapsed);
    }

    /// <summary>Hands over the prefetch for <paramref name="current"/>, or cancels a stale one.</summary>
    private Prefetch? TakePrefetch(ApsSolution current)
    {
        Prefetch? taken = prefetch;
        prefetch = null;
        if (taken is null || taken.Matches(current))
            return taken;
        Abandon(taken);
        return null;
    }

    private void CancelPrefetch()
    {
        if (prefetch is not Prefetch stale) return;
        prefetch = null;
        Abandon(stale);
    }

    private static void Abandon(Prefetch stale)
    {
        stale.Cancellation.Cancel();
        _ = stale.Task.ContinueWith(completed =>
        {
            _ = completed.Exception;
            stale.Cancellation.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
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

    private enum OperationKind { Generate, Next, Navigate, Refresh }

    private sealed record Operation(Task<ApsSolution> Task, CancellationTokenSource Cancellation,
        Stopwatch Elapsed, int Revision, OperationKind Kind, int TargetIndex);

    /// <summary>A background search for the solution after <see cref="Base"/>.</summary>
    private sealed record Prefetch(ApsSolution Base, Task<ApsSolution> Task, CancellationTokenSource Cancellation,
        Stopwatch Elapsed)
    {
        /// <summary>Prefab refreshes keep the solution history, so the prefetch stays valid across them.</summary>
        internal bool Matches(ApsSolution current) =>
            ReferenceEquals(Base.Layouts, current.Layouts) && Base.Request.Problem == current.Request.Problem;
    }
}
