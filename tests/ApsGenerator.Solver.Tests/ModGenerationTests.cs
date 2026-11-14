using ApsGenerator.Core.Models;
using ApsGenerator.Mod.Generation;

namespace ApsGenerator.Solver.Tests;

public sealed class ModGenerationTests
{
    [Fact]
    public async Task ClearDiscardsAWorkerThatReturnsSuccessfullyAfterCancellation()
    {
        using var worker = new ControlledSolver();
        using var session = new ApsGenerationSession(worker);
        session.Generate(Request());
        await worker.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.Clear();
        worker.Release.Set();
        Assert.Empty(await Drain(session));
        Assert.Null(session.Solution);
    }

    [Fact]
    public async Task CancelDiscardsASuccessfulResultWithoutPublishingIt()
    {
        using var worker = new ControlledSolver();
        using var session = new ApsGenerationSession(worker);
        session.Generate(Request());
        await worker.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.Cancel();
        worker.Release.Set();
        Assert.Empty(await Drain(session));
        Assert.Null(session.Solution);
    }

    [Fact]
    public async Task ChangesDuringTetrisProduceOnlyTheLatestPrefab()
    {
        using var worker = new ControlledSolver();
        using var session = new ApsGenerationSession(worker);
        ApsGenerationRequest request = Request();
        session.Generate(request);
        await worker.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.Refresh(request with { Output = request.Output with { ComponentHeight = 2 } });
        ApsGenerationRequest latest = request with { Output = request.Output with { ComponentHeight = 3 } };
        session.Refresh(latest);
        worker.Release.Set();
        ApsGenerationCompletion completion = Assert.Single(await Drain(session));
        Assert.Equal(latest, completion.Solution!.Request);
        Assert.Equal(1, worker.RefreshCount);
    }

    [Fact]
    public async Task ClearDropsQueuedRefreshAndAllowsANewProblem()
    {
        using var worker = new ControlledSolver();
        using var session = new ApsGenerationSession(worker);
        ApsGenerationRequest request = Request();
        session.Generate(request);
        await worker.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.Refresh(request with { Output = request.Output with { ComponentHeight = 2 } });
        session.Clear();
        worker.Release.Set();
        Assert.Empty(await Drain(session));
        Assert.Equal(0, worker.RefreshCount);
        var next = request with { Problem = request.Problem with { Symmetry = SymmetryType.Rotation180 } };
        session.Generate(next);
        Assert.Equal(next, Assert.Single(await Drain(session)).Solution!.Request);
    }

    [Fact]
    public async Task SupersededRefreshNeverPublishesItsResult()
    {
        using var worker = new ControlledSolver();
        using var session = new ApsGenerationSession(worker);
        ApsGenerationRequest request = Request();
        worker.Release.Set();
        session.Generate(request);
        await Drain(session);
        worker.Release.Reset();
        session.Refresh(request with { Output = request.Output with { ComponentHeight = 2 } });
        await worker.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var latest = request with { Output = request.Output with { ComponentHeight = 4 } };
        session.Refresh(latest);
        worker.Release.Set();
        Assert.Equal(latest, Assert.Single(await Drain(session)).Solution!.Request);
        Assert.Equal(2, worker.RefreshCount);
    }

    [Fact]
    public void TimedOutCoolerIsRetriedWhileSuccessfulCoolerIsReused()
    {
        var settings = new ApsGenerationSettings();
        settings.SetTetrisType(TetrisType.FiveClip);
        ApsGenerationRequest request = settings.CreateRequest();
        var timedOut = new CoolerSnakeResult { Status = CoolerSnakeStatus.TimedOut };
        ApsSolution existing = Solution(request, timedOut);
        var solver = new ApsGenerationSolver();
        ApsSolution retried = solver.Reconfigure(existing, request, CancellationToken.None);
        Assert.NotSame(timedOut, retried.Cooler);
        Assert.Equal(CoolerSnakeStatus.Sat, retried.Cooler!.Status);
        var resized = request with { Output = request.Output with { ComponentHeight = 6 } };
        Assert.Same(retried.Cooler, solver.Reconfigure(retried, resized, CancellationToken.None).Cooler);
    }

    [Fact]
    public void ReconfigureRejectsDifferentTetrisProblem()
    {
        ApsGenerationRequest request = Request();
        var different = request with { Problem = request.Problem with { Symmetry = SymmetryType.Rotation180 } };
        Assert.Throws<InvalidOperationException>(() => new ApsGenerationSolver().Reconfigure(
            Solution(request), different, CancellationToken.None));
    }

    [Fact]
    public void ClampedSettingsChangesDoNotInvalidateTheLayout()
    {
        var settings = new ApsGenerationSettings();
        settings.SetWidth(100);
        Assert.Equal(49, settings.Width);
        Assert.Equal(ApsSettingsChangeImpact.None, settings.Change(() => settings.AdjustWidth(1)));
        Assert.Equal(ApsSettingsChangeImpact.None, settings.Change(() => settings.AdjustDepth(1)));
        Assert.Equal(ApsSettingsChangeImpact.Prefab, settings.Change(() => settings.AdjustComponentLength(1)));
        Assert.Equal(ApsSettingsChangeImpact.FutureSolve, settings.Change(() => settings.AdjustMaxTime(5)));
        Assert.Equal(ApsSettingsChangeImpact.TetrisSolve, settings.Change(() => settings.SetTetrisType(TetrisType.FiveClip)));
    }

    [Fact]
    public void ResetAndPersistenceShareDefaultsAndRequestsAreSnapshots()
    {
        var settings = new ApsGenerationSettings();
        ApsGenerationRequest original = settings.CreateRequest();
        settings.SetTetrisType(TetrisType.FiveClip);
        Assert.Equal(TetrisType.ThreeClip, original.Problem.Type);
        settings.ResetToDefaults();
        Assert.Equal(original, settings.CreateRequest());
        settings.Apply(new ApsSettingsData());
        Assert.Equal(original, settings.CreateRequest());
        Assert.True(ApsSettingsData.IsSupportedSchemaVersion(ApsSettingsData.OldestSupportedSchemaVersion));
        Assert.False(ApsSettingsData.IsSupportedSchemaVersion(ApsSettingsData.OldestSupportedSchemaVersion - 1));
        Assert.False(ApsSettingsData.IsSupportedSchemaVersion(ApsSettingsData.CurrentSchemaVersion + 1));
        Assert.Throws<NotSupportedException>(() => settings.Apply(new ApsSettingsData { SchemaVersion = 99 }));
        Assert.Equal(original, settings.CreateRequest());
    }

    [Fact]
    public void StatusDistinguishesPartialAndTimedOutLayouts()
    {
        ApsSolution partial = Solution(Request(), new CoolerSnakeResult { Status = CoolerSnakeStatus.TimedOut });
        Assert.Contains("Partial", ApsGenerationStatus.Describe(partial));
        Assert.Contains("cooler timed out", ApsGenerationStatus.Describe(partial));
        ApsSolution timedOut = new(partial.Request, partial.Grid,
            partial.Result with { Status = SolverStatus.TimedOut }, new CoolerSnakeResult { Status = CoolerSnakeStatus.Sat });
        Assert.Contains("Timed out", ApsGenerationStatus.Describe(timedOut));
        ApsSolution empty = new(partial.Request, partial.Grid,
            partial.Result with { Placements = [], Status = SolverStatus.NoSolution }, null);
        Assert.Equal("No layout found", ApsGenerationStatus.Describe(empty));
    }

    [Fact]
    public void PersistedSettingsNormalizeInvalidEnumsAndDependentDimensions()
    {
        var settings = new ApsGenerationSettings();
        settings.Apply(new ApsSettingsData
        {
            TemplateShape = (ApsTemplateShape)99,
            TetrisType = (TetrisType)99, SymmetryType = (SymmetryType)99,
            SymmetryMode = (SymmetryMode)99, ExtraLayers = (ExportExtraLayers)99
        });
        Assert.Equal(new ApsGenerationSettings().CreateRequest(), settings.CreateRequest());
        settings.Apply(new ApsSettingsData
        {
            TemplateShape = ApsTemplateShape.Rectangle, Width = 8, Depth = 12,
            SymmetryType = SymmetryType.Rotation90, TetrisType = TetrisType.FiveClip,
            ComponentLength = 5, ExtraLayers = ExportExtraLayers.IntakesOnly
        });
        Assert.Equal(SymmetryType.Rotation180, settings.SymmetryType);
        Assert.Equal(6, settings.ComponentLength);
        Assert.Equal(ExportExtraLayers.EjectorsIntakesCoolerSnake, settings.ExtraLayers);
    }

    [Fact]
    public void OddDimensionSnappingCanBeEnabled()
    {
        var settings = new ApsGenerationSettings();

        Assert.Equal(ApsSettingsChangeImpact.FutureSolve,
            settings.Change(() => settings.SetSnapToOddDimensions(true)));
        settings.SetWidth(10);

        Assert.Equal(11, settings.Width);
        Assert.Equal(11, settings.Depth);
        Assert.Equal(1, settings.HoleSize);
        Assert.False(settings.AllowEvenDimensions);
        Assert.Equal(11, settings.Capture().Width);
        Assert.True(settings.Capture().SnapToOddDimensions);
    }

    [Fact]
    public void CenterHoleMaintainsParityAndTwoBlockSideClearance()
    {
        var settings = new ApsGenerationSettings();
        settings.SetHoleSize(99);
        Assert.Equal(settings.Width - 4, settings.HoleSize);

        settings.SetWidth(10);
        Assert.Equal(6, settings.HoleSize);

        settings.SetHoleSize(5);
        Assert.Equal(6, settings.HoleSize);
        settings.SetWidth(11);
        Assert.Equal(5, settings.HoleSize);
        settings.SetWidth(12);
        Assert.Equal(6, settings.HoleSize);
        settings.SetWidth(5);
        Assert.Equal(1, settings.HoleSize);
        settings.SetWidth(9);
        Assert.Equal(5, settings.HoleSize);
    }

    [Fact]
    public void HoleStepperStartsAtDisplayedSizeAfterWidthClampsIt()
    {
        var settings = new ApsGenerationSettings();
        settings.SetHoleSize(11);
        settings.SetWidth(9);
        Assert.Equal(5, settings.HoleSize);
        settings.SetWidth(15);
        Assert.Equal(11, settings.HoleSize);
        settings.SetWidth(9);

        settings.AdjustHoleSize(-1);
        Assert.Equal(3, settings.HoleSize);
        settings.SetWidth(15);
        Assert.Equal(3, settings.HoleSize);
    }

    [Fact]
    public void HoleStepperAtLimitDoesNotQueueAnInvisibleAdjustment()
    {
        var settings = new ApsGenerationSettings();
        settings.SetWidth(9);
        settings.SetHoleSize(5);

        settings.AdjustHoleSize(1);
        settings.AdjustHoleSize(1);
        Assert.Equal(5, settings.Capture().HoleSize);
        settings.SetWidth(15);
        Assert.Equal(5, settings.HoleSize);

        settings.AdjustHoleSize(-1);
        Assert.Equal(3, settings.HoleSize);
    }

    [Fact]
    public void CenterHoleShapeAndSizeArePartOfTheTetrisProblem()
    {
        var settings = new ApsGenerationSettings();
        Assert.Equal(ApsSettingsChangeImpact.TetrisSolve,
            settings.Change(() => settings.SetHoleShape(CenterHoleShape.Square)));
        Assert.Equal(ApsSettingsChangeImpact.TetrisSolve,
            settings.Change(() => settings.AdjustHoleSize(1)));

        ApsTemplate template = settings.CreateRequest().Problem.Template;
        Assert.Equal(CenterHoleShape.Square, template.HoleShape);
        Assert.Equal(3, template.HoleSize);
    }

    [Fact]
    public void CenterHoleTemplateHasMinimumUsefulDimension()
    {
        var settings = new ApsGenerationSettings();
        settings.SetWidth(3);

        Assert.Equal(ApsGenerationSettings.MinimumCenterHoleDimension, settings.Width);
        Assert.Equal(1, settings.HoleSize);
    }

    [Fact]
    public void VersionOneProfilesMoveUnchangedSolverDefaultsToTheCurrentDefaults()
    {
        var settings = new ApsGenerationSettings();
        settings.Apply(new ApsSettingsData
        {
            SchemaVersion = 1, SymmetryType = SymmetryType.None, MaxTimeSeconds = 30,
            EarlyStopEnabled = true, MaxThreads = 0
        });
        Assert.Equal(ApsGenerationDefaults.DefaultSymmetryType, settings.SymmetryType);
        Assert.Equal(ApsGenerationDefaults.MaxTimeSeconds, settings.MaxTimeSeconds);
        Assert.Equal(ApsGenerationDefaults.EarlyStopEnabled, settings.EarlyStopEnabled);
        Assert.Equal(settings.DefaultThreads, settings.MaxThreads);
        Assert.Equal(ApsSettingsData.CurrentSchemaVersion, settings.Capture().SchemaVersion);

        settings.Apply(new ApsSettingsData
        {
            SchemaVersion = 1, SymmetryType = SymmetryType.Rotation180, MaxTimeSeconds = 45,
            EarlyStopEnabled = false
        });
        Assert.Equal(SymmetryType.Rotation180, settings.SymmetryType);
        Assert.Equal(45, settings.MaxTimeSeconds);

        settings.Apply(new ApsSettingsData { SymmetryType = SymmetryType.None, MaxTimeSeconds = 30 });
        Assert.Equal(SymmetryType.None, settings.SymmetryType);
        Assert.Equal(30, settings.MaxTimeSeconds);
    }

    [Fact]
    public void ThreadsDefaultToThreeQuartersAndFollowProcessorChanges()
    {
        var settings = new ApsGenerationSettings();
        Assert.Equal(Math.Max(1, (int)(settings.MaximumThreads * 0.75)), settings.MaxThreads);
        Assert.Equal(0, settings.Capture().MaxThreads);
        settings.AdjustThreads(-1);
        if (settings.MaxThreads != settings.DefaultThreads)
            Assert.Equal(settings.MaxThreads, settings.Capture().MaxThreads);
        Assert.Equal(2, ApsGenerationDefaults.DefaultThreadCountFor(3));
        Assert.Equal(1, ApsGenerationDefaults.DefaultThreadCountFor(1));
        Assert.Equal(11, ApsGenerationDefaults.DefaultThreadCountFor(15));
    }

    [Fact]
    public void SizeLimitsDependOnTetrisOptionsAndStopTheWidthStepper()
    {
        var settings = new ApsGenerationSettings();
        settings.SetTetrisType(TetrisType.FiveClip);
        settings.SetSymmetryType(SymmetryType.None);
        int maximum = settings.MaximumWidthFor(SymmetryType.None);
        Assert.True(maximum < ApsGenerationSettings.MaximumDimension);
        Assert.True(settings.MaximumWidthFor(SymmetryType.BothReflection) > maximum);
        Assert.True(settings.RecommendedWidthFor(SymmetryType.None) <= maximum);

        settings.SetWidth(maximum);
        Assert.False(settings.IsTooLarge);
        Assert.False(settings.CanIncreaseWidth);
        Assert.Equal(ApsSettingsChangeImpact.None, settings.Change(() => settings.AdjustWidth(1)));

        settings.SetWidth(ApsGenerationSettings.MaximumDimension);
        Assert.Equal(maximum, settings.Width);

        settings.SetSymmetryType(SymmetryType.BothReflection);
        Assert.True(settings.CanIncreaseWidth);
        settings.AdjustWidth(1);
        settings.SetSymmetryType(SymmetryType.None);
        Assert.Equal(maximum, settings.Width);
        Assert.False(settings.IsTooLarge);

        settings.SetTetrisType(TetrisType.ThreeClip);
        settings.SetWidth(41);
        settings.SetTetrisType(TetrisType.FiveClip);
        Assert.Equal(maximum, settings.Width);

        settings.Apply(new ApsSettingsData { TetrisType = TetrisType.FiveClip, SymmetryType = SymmetryType.None, Width = 41 });
        Assert.Equal(maximum, settings.Width);
        settings.SetSymmetryType(SymmetryType.BothReflection);

        settings.SetSymmetryType(SymmetryType.BothReflection);
        settings.SetSymmetryMode(SymmetryMode.Soft);
        Assert.Equal(maximum, settings.MaximumWidthFor(SymmetryType.BothReflection));
    }

    [Fact]
    public void RectangleDepthStepperRespectsTheSameCellBudget()
    {
        var settings = new ApsGenerationSettings();
        settings.SetTemplateShape(ApsTemplateShape.Rectangle);
        settings.SetTetrisType(TetrisType.FiveClip);
        settings.SetSymmetryType(SymmetryType.None);
        settings.SetWidth(settings.MaximumWidthFor(SymmetryType.None));
        while (settings.CanIncreaseDepth)
            settings.AdjustDepth(1);
        Assert.False(settings.IsTooLarge);
        Assert.True(settings.Depth < ApsGenerationSettings.MaximumDimension);
    }

    [Fact]
    public void DensityAndHudScaleSnapToTheirSteps()
    {
        var settings = new ApsGenerationSettings();
        Assert.True(settings.IsMaximizingDensity);
        Assert.Null(settings.CreateRequest().Problem.TargetClusters(settings.CreateRequest().Problem.Template.CreateGrid()));
        Assert.Equal(ApsSettingsChangeImpact.TetrisSolve, settings.Change(() => settings.SetTargetDensityPercent(72)));
        Assert.Equal(70, settings.TargetDensityPercent);
        settings.SetTargetDensityPercent(1);
        Assert.Equal(ApsGenerationSettings.MinimumDensityPercent, settings.TargetDensityPercent);

        Assert.Equal(ApsSettingsChangeImpact.FutureSolve, settings.Change(() => settings.AdjustHudScale(1)));
        Assert.Equal(110, settings.HudScalePercent);
        settings.ResetToDefaults();
        Assert.Equal(110, settings.HudScalePercent);
        Assert.True(settings.IsMaximizingDensity);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void DensityTargetIsTrimmedToTheExactClusterCount()
    {
        var settings = new ApsGenerationSettings();
        settings.SetTemplateShape(ApsTemplateShape.Rectangle);
        settings.SetWidth(12);
        settings.SetDepth(12);
        settings.SetSymmetryType(SymmetryType.None);
        settings.SetTargetDensityPercent(50);
        settings.SetExtraLayers(ExportExtraLayers.TetrisOnly);
        ApsGenerationRequest request = settings.CreateRequest();
        int target = request.Problem.TargetClusters(request.Problem.Template.CreateGrid())!.Value;

        ApsSolution solution = new ApsGenerationSolver().Solve(request, CancellationToken.None);

        Assert.Equal(target, solution.Result.ClusterCount);
        Assert.Equal(SolverStatus.TargetDensityReached, solution.Result.Status);
        Assert.True(solution.Untrimmed.ClusterCount >= target);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void NextLayoutFindsDistinctLayoutsUntilExhausted()
    {
        var settings = new ApsGenerationSettings();
        settings.SetTemplateShape(ApsTemplateShape.Rectangle);
        settings.SetWidth(6);
        settings.SetDepth(4);
        settings.SetSymmetryType(SymmetryType.None);
        settings.SetExtraLayers(ExportExtraLayers.TetrisOnly);
        ApsGenerationRequest request = settings.CreateRequest();
        var solver = new ApsGenerationSolver();

        ApsSolution current = solver.Solve(request, CancellationToken.None);
        int clusters = current.Result.ClusterCount;
        var seen = new HashSet<string> { Key(current) };
        for (int step = 0; step < 200 && current.NextLayoutOutcome == ApsNextLayoutOutcome.None; step++)
        {
            ApsSolution next = solver.Next(current, request, CancellationToken.None);
            if (next.NextLayoutOutcome != ApsNextLayoutOutcome.None)
            {
                Assert.Equal(ApsNextLayoutOutcome.Exhausted, next.NextLayoutOutcome);
                Assert.Same(current.Result, next.Result);
                current = next;
                break;
            }
            Assert.Equal(current.LayoutNumber + 1, next.LayoutNumber);
            Assert.True(next.Result.ClusterCount >= clusters);
            Assert.True(seen.Add(Key(next)), "Next repeated a layout.");
            current = next;
        }
        Assert.Equal(ApsNextLayoutOutcome.Exhausted, current.NextLayoutOutcome);
        Assert.True(seen.Count > 1);

        static string Key(ApsSolution solution) => string.Join(";", solution.Result.Placements
            .OrderBy(p => p.Row).ThenBy(p => p.Col).ThenBy(p => p.ShapeIndex));
    }

    [Fact]
    public async Task NextKeepsTheCurrentLayoutWhileSearching()
    {
        using var worker = new ControlledSolver();
        using var session = new ApsGenerationSession(worker);
        ApsGenerationRequest request = Request();
        worker.Release.Set();
        session.Generate(request);
        ApsSolution first = Assert.Single(await Drain(session)).Solution!;
        worker.Release.Reset();
        session.Next(request);
        Assert.True(session.IsBusy);
        Assert.Same(first, session.Solution);
        worker.Release.Set();
        Assert.Equal(2, Assert.Single(await Drain(session)).Solution!.LayoutNumber);
    }

    [Fact]
    public async Task NextUsesThePrefetchedSolutionWithoutSearchingAgain()
    {
        using var worker = new ControlledSolver();
        using var session = new ApsGenerationSession(worker);
        ApsGenerationRequest request = Request();
        worker.Release.Set();
        session.Generate(request);
        await Drain(session);
        await WaitUntil(() => worker.NextCount == 1);

        session.Next(request);
        ApsSolution second = Assert.Single(await Drain(session)).Solution!;

        Assert.Equal(2, second.LayoutNumber);
        // The only other search is the prefetch for the third solution.
        await WaitUntil(() => worker.NextCount == 2);
    }

    [Fact]
    public async Task PrefabRefreshKeepsThePrefetchAndAdaptsItsOutput()
    {
        using var worker = new ControlledSolver();
        using var session = new ApsGenerationSession(worker);
        ApsGenerationRequest request = Request();
        worker.Release.Set();
        session.Generate(request);
        await Drain(session);
        var taller = request with { Output = request.Output with { ComponentHeight = 5 } };
        session.Refresh(taller);
        await Drain(session);

        session.Next(taller);
        ApsSolution next = Assert.Single(await Drain(session)).Solution!;

        Assert.Equal(taller, next.Request);
        Assert.Equal(2, next.LayoutNumber);
        await WaitUntil(() => worker.NextCount == 2);
    }

    [Fact]
    public async Task ClearCancelsThePrefetch()
    {
        using var worker = new ControlledSolver();
        using var session = new ApsGenerationSession(worker);
        worker.Release.Set();
        session.Generate(Request());
        await Drain(session);
        await WaitUntil(() => worker.NextCount == 1);
        session.Clear();
        lock (worker.NextTokens)
            Assert.True(worker.NextTokens[0].IsCancellationRequested);
    }

    [Fact]
    public async Task PreviousAndNextReplayTheHistoryWithoutSearching()
    {
        using var worker = new ControlledSolver();
        using var session = new ApsGenerationSession(worker);
        ApsGenerationRequest request = Request();
        worker.Release.Set();
        session.Generate(request);
        await Drain(session);
        Assert.False(session.CanShowPrevious);
        session.Next(request);
        ApsSolution second = Assert.Single(await Drain(session)).Solution!;
        await WaitUntil(() => worker.NextCount == 2);

        session.Previous(request);
        Assert.Equal(1, Assert.Single(await Drain(session)).Solution!.LayoutNumber);
        Assert.False(session.CanShowPrevious);
        session.Next(request);
        Assert.Same(second, Assert.Single(await Drain(session)).Solution);
        Assert.Equal(2, worker.NextCount);

        session.Next(request);
        Assert.Equal(3, Assert.Single(await Drain(session)).Solution!.LayoutNumber);
        Assert.True(session.CanShowPrevious);
    }

    [Fact]
    public async Task PreviousAdaptsAnOlderSolutionToNewPrefabOptions()
    {
        using var worker = new ControlledSolver();
        using var session = new ApsGenerationSession(worker);
        ApsGenerationRequest request = Request();
        worker.Release.Set();
        session.Generate(request);
        await Drain(session);
        session.Next(request);
        await Drain(session);
        var taller = request with { Output = request.Output with { ComponentHeight = 5 } };
        session.Refresh(taller);
        await Drain(session);

        session.Previous(taller);
        ApsSolution first = Assert.Single(await Drain(session)).Solution!;

        Assert.Equal(1, first.LayoutNumber);
        Assert.Equal(taller, first.Request);
        Assert.Equal(2, worker.RefreshCount);
    }

    [Fact]
    public async Task ClearForgetsTheHistory()
    {
        using var worker = new ControlledSolver();
        using var session = new ApsGenerationSession(worker);
        ApsGenerationRequest request = Request();
        worker.Release.Set();
        session.Generate(request);
        await Drain(session);
        session.Next(request);
        await Drain(session);
        session.Clear();
        Assert.False(session.CanShowPrevious);
        Assert.Null(session.Solution);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(1, timeout.Token);
    }

    private static ApsGenerationRequest Request() => new ApsGenerationSettings().CreateRequest();
    private static ApsSolution Solution(ApsGenerationRequest request, CoolerSnakeResult? cooler = null) => new(
        request, request.Problem.Template.CreateGrid(), new SolverResult
        {
            Placements = [new(3, 3, 0)], AllSolutions = [], EmptyCells = 0, Status = SolverStatus.Optimal
        }, cooler);

    private static async Task<List<ApsGenerationCompletion>> Drain(ApsGenerationSession session)
    {
        var completions = new List<ApsGenerationCompletion>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (session.IsBusy)
        {
            if (session.Poll() is ApsGenerationCompletion completed) completions.Add(completed);
            if (session.IsBusy) await Task.Delay(1, timeout.Token);
        }
        return completions;
    }

    private sealed class ControlledSolver : IApsGenerationSolver, IDisposable
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource RefreshStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly WorkerGate Release = new();
        internal int RefreshCount;
        internal int NextCount;
        internal readonly List<CancellationToken> NextTokens = [];

        public ApsSolution Solve(ApsGenerationRequest request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            Release.Wait(TimeSpan.FromSeconds(5));
            return Solution(request);
        }

        public ApsSolution Reconfigure(ApsSolution existing, ApsGenerationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref RefreshCount);
            RefreshStarted.TrySetResult();
            Release.Wait(TimeSpan.FromSeconds(5));
            // Like the real solver, a refresh keeps the solution history.
            return existing with { Request = request };
        }

        public ApsSolution Next(ApsSolution existing, ApsGenerationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref NextCount);
            lock (NextTokens) NextTokens.Add(cancellationToken);
            Started.TrySetResult();
            Release.Wait(TimeSpan.FromSeconds(5));
            return Solution(request) with { LayoutNumber = existing.LayoutNumber + 1 };
        }

        public void Dispose() => Release.Set();
    }

    private sealed class WorkerGate
    {
        private TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Set() => completion.TrySetResult();
        internal void Reset() => completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Wait(TimeSpan timeout) => completion.Task.WaitAsync(timeout).GetAwaiter().GetResult();
    }
}
