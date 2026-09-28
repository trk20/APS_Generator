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
            return Solution(request);
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
