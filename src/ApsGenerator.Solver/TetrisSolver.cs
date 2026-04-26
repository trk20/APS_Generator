using ApsGenerator.Core;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver.Interop;
using System.Diagnostics;

namespace ApsGenerator.Solver;

public sealed class TetrisSolver
{
    /// <summary>
    /// AMO constraints with ≤ this many variables use pairwise encoding.
    /// Above this threshold, Sinz sequential counter is used.
    /// </summary>
    private const int PairwiseAmoThreshold = 6;
    private const double EarlyStopBaseBudget = 5.13;
    private const double EarlyStopDecayRate = 0.75;
    private const double EarlyStopMinMs = 177.0;
    private const int EarlyStopMinHistory = 2;

    public SolverResult Solve(Grid grid, TetrisType type, SolverOptions? options = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var opts = options ?? new SolverOptions();
        var placements = PlacementEnumerator.Enumerate(grid, type);

        if (placements.Count == 0)
            return EmptyResult(grid.AvailableCellCount, SolverStatus.NoSolution);

        var shapes = ClusterShape.GetShapes(type);
        var (exclusiveMap, connectionMap) = BuildCellMaps(placements, shapes);
        int placementCount = placements.Count;
        int availableCells = grid.AvailableCellCount;

        var cellList = new List<(int Row, int Col)>();
        for (int r = 0; r < grid.Height; r++)
            for (int c = 0; c < grid.Width; c++)
                if (grid.IsAvailable(r, c))
                    cellList.Add((r, c));

        // Pre-solve
        int step = GetDescentStep(type);
        int remainder = availableCells % step;

        double initialRemaining = opts.MaxTimeSeconds - stopwatch.Elapsed.TotalSeconds;
        if (initialRemaining <= 0)
            return EmptyResult(availableCells, SolverStatus.TimedOut);

        bool needMultipleSolutions = opts.NumSolutions > 1;

        var (bestModel, phaseOneResult, phaseOneSolver) = SolveUnconstrained(
            grid, placements, shapes, exclusiveMap, connectionMap,
            opts, initialRemaining, retainSolver: false, ct);

        if (phaseOneResult != CryptoMiniSatNative.Lbool.True)
        {
            phaseOneSolver?.Dispose();

            SolverStatus phaseOneStatus = phaseOneResult == CryptoMiniSatNative.Lbool.Undef
                ? SolverStatus.TimedOut
                : SolverStatus.NoSolution;
            return EmptyResult(availableCells, phaseOneStatus);
        }

        int bestEmpty = CountEmptyCells(bestModel!, placements, shapes, availableCells);
        int bestClusterCount = CountClusters(bestModel!, placementCount);

        // Dispose Phase 1 solver
        phaseOneSolver?.Dispose();

        if (bestEmpty <= 0)
        {
            var earlyResult = DecodeModel(bestModel!, placements, shapes, availableCells, SolverStatus.Optimal);
            if (needMultipleSolutions)
            {
                earlyResult = BuildTightSolverAndEnumerate(
                    grid, placements, shapes, exclusiveMap, connectionMap, cellList,
                    bestEmpty, bestModel!, earlyResult, opts, stopwatch, placementCount, availableCells, ct);
            }
            return earlyResult;
        }

        if (opts.TargetClusterCount.HasValue && bestClusterCount >= opts.TargetClusterCount.Value)
        {
            var earlyResult = DecodeModel(bestModel!, placements, shapes, availableCells, SolverStatus.LikelyOptimal);
            if (needMultipleSolutions)
            {
                earlyResult = BuildTightSolverAndEnumerate(
                    grid, placements, shapes, exclusiveMap, connectionMap, cellList,
                    bestEmpty, bestModel!, earlyResult, opts, stopwatch, placementCount, availableCells, ct);
            }
            return earlyResult;
        }

        SolverStatus status = SolverStatus.Optimal;
        SatSolver? retainedBoundSolver = null;
        var satIterationDurationsMs = new List<double>();

        // Phase 2: Linear descent with fresh solver per bound.
        int bound = GetNextBound(bestEmpty, remainder, step);

        while (bound >= 0)
        {
            ct.ThrowIfCancellationRequested();

            double remaining = opts.MaxTimeSeconds - stopwatch.Elapsed.TotalSeconds;
            if (remaining <= 0)
            {
                status = SolverStatus.TimedOut;
                break;
            }

            var iterationStopwatch = Stopwatch.StartNew();

            var (candidateModel, result, candidateSolver) = SolveWithBound(
                grid, placements, shapes, exclusiveMap, connectionMap,
                cellList, exclusiveMap, bound, opts, remaining,
                retainSolver: false, ct);

            double iterationMs = iterationStopwatch.Elapsed.TotalMilliseconds;

            if (result == CryptoMiniSatNative.Lbool.True)
            {
                satIterationDurationsMs.Add(iterationMs);

                retainedBoundSolver?.Dispose();
                retainedBoundSolver = candidateSolver;

                bestModel = candidateModel;
                bestEmpty = CountEmptyCells(bestModel!, placements, shapes, availableCells);

                if (opts.TargetClusterCount.HasValue)
                {
                    int clusterCount = CountClusters(bestModel!, placementCount);
                    if (clusterCount >= opts.TargetClusterCount.Value)
                    {
                        var earlyResult = DecodeModel(bestModel!, placements, shapes, availableCells, SolverStatus.LikelyOptimal);
                        if (needMultipleSolutions)
                        {
                            retainedBoundSolver?.Dispose();
                            retainedBoundSolver = null;
                            earlyResult = BuildTightSolverAndEnumerate(
                                grid, placements, shapes, exclusiveMap, connectionMap, cellList,
                                bestEmpty, bestModel!, earlyResult, opts, stopwatch, placementCount, availableCells, ct);
                        }
                        retainedBoundSolver?.Dispose();
                        return earlyResult;
                    }
                }

                if (opts.EarlyStopEnabled && satIterationDurationsMs.Count > EarlyStopMinHistory)
                {
                    double currentMs = satIterationDurationsMs[^1];
                    if (currentMs >= EarlyStopMinMs)
                    {
                        int historyCount = satIterationDurationsMs.Count - 1;
                        double totalPreviousMs = 0;
                        for (int i = 0; i < historyCount; i++)
                            totalPreviousMs += satIterationDurationsMs[i];

                        double patience = EarlyStopBaseBudget / (1.0 + EarlyStopDecayRate * Math.Max(0, historyCount - EarlyStopMinHistory));

                        if (currentMs >= patience * totalPreviousMs)
                        {
                            status = SolverStatus.LikelyOptimal;
                            break;
                        }
                    }
                }

                bound = GetNextBound(bestEmpty, remainder, step);
            }
            else if (result == CryptoMiniSatNative.Lbool.Undef)
            {
                candidateSolver?.Dispose();
                status = SolverStatus.TimedOut;
                break;
            }
            else
            {
                candidateSolver?.Dispose();
                status = SolverStatus.Optimal;
                break;
            }
        }

        if (bestModel is not null)
        {
            var finalResult = DecodeModel(bestModel, placements, shapes, availableCells, status);
            if (needMultipleSolutions)
            {
                retainedBoundSolver?.Dispose();
                retainedBoundSolver = null;
                finalResult = BuildTightSolverAndEnumerate(
                    grid, placements, shapes, exclusiveMap, connectionMap, cellList,
                    bestEmpty, bestModel, finalResult, opts, stopwatch, placementCount, availableCells, ct);
            }
            retainedBoundSolver?.Dispose();
            return finalResult;
        }

        retainedBoundSolver?.Dispose();
        return EmptyResult(availableCells, status == SolverStatus.Optimal ? SolverStatus.NoSolution : status);
    }

    /// <summary>
    /// Builds a fresh solver with AMK(≤bestEmpty) and enumerates additional distinct solutions.
    /// Ensures all enumerated solutions share the same optimal objective.
    /// </summary>
    private static SolverResult BuildTightSolverAndEnumerate(
        Grid grid,
        List<Placement> placements,
        IReadOnlyList<ClusterShape> shapes,
        Dictionary<(int, int), List<int>> exclusiveMap,
        Dictionary<(int, int), List<int>> connectionMap,
        IReadOnlyList<(int Row, int Col)> cellList,
        int bestEmpty,
        bool?[] firstModel,
        SolverResult firstResult,
        SolverOptions opts,
        Stopwatch stopwatch,
        int placementCount,
        int availableCells,
        CancellationToken ct)
    {
        double remaining = opts.MaxTimeSeconds - stopwatch.Elapsed.TotalSeconds;
        if (remaining <= 0)
            return firstResult;

        var (_, result, tightSolver) = SolveWithBound(
            grid, placements, shapes, exclusiveMap, connectionMap, cellList, exclusiveMap,
            bestEmpty, opts, remaining, retainSolver: true, ct);

        if (result != CryptoMiniSatNative.Lbool.True || tightSolver is null)
        {
            tightSolver?.Dispose();
            return firstResult;
        }

        try
        {
            return EnumerateAdditionalSolutions(
                tightSolver, firstModel, firstResult, placements, shapes,
                availableCells, placementCount, opts, stopwatch, ct);
        }
        finally
        {
            tightSolver.Dispose();
        }
    }

    /// <summary>
    /// Enumerates additional distinct solutions by adding blocking clauses to the retained solver.
    /// Each blocking clause forbids the exact set of true placement variables from the previous model.
    /// The bound constraint already present on the solver ensures all solutions have the same objective.
    /// </summary>
    private static SolverResult EnumerateAdditionalSolutions(
        SatSolver solver,
        bool?[] firstModel,
        SolverResult firstResult,
        List<Placement> placements,
        IReadOnlyList<ClusterShape> shapes,
        int availableCells,
        int placementCount,
        SolverOptions opts,
        Stopwatch stopwatch,
        CancellationToken ct)
    {
        var allSolutions = new List<IReadOnlyList<Placement>> { firstResult.Placements };
        var allModels = new List<bool?[]> { firstModel };

        for (int i = 1; i < opts.NumSolutions; i++)
        {
            ct.ThrowIfCancellationRequested();

            double remaining = opts.MaxTimeSeconds - stopwatch.Elapsed.TotalSeconds;
            if (remaining <= 0)
                break;

            // Build blocking clause: forbid exact placement assignment from last model
            var lastModel = allModels[^1];
            var blockingClause = new int[placementCount];
            for (int p = 0; p < placementCount; p++)
            {
                int lit = p + 1; // 1-based literal
                blockingClause[p] = lastModel[p] == true ? -lit : lit;
            }
            solver.AddClause(blockingClause);

            solver.SetMaxTime(remaining * opts.MaxThreads);

            CryptoMiniSatNative.Lbool result;
            using (ct.Register(solver.Interrupt))
                result = solver.Solve();

            if (result != CryptoMiniSatNative.Lbool.True)
                break;

            ct.ThrowIfCancellationRequested();

            var model = solver.GetModel();
            var decoded = DecodePlacements(model, placements, shapes, availableCells);
            allSolutions.Add(decoded.Placements);
            allModels.Add(model);
        }

        return firstResult with { AllSolutions = allSolutions };
    }

    private static (bool?[]? model, CryptoMiniSatNative.Lbool result, SatSolver? retainedSolver) SolveUnconstrained(
        Grid grid,
        List<Placement> placements,
        IReadOnlyList<ClusterShape> shapes,
        Dictionary<(int, int), List<int>> exclusiveMap,
        Dictionary<(int, int), List<int>> connectionMap,
        SolverOptions options,
        double remainingSeconds,
        bool retainSolver,
        CancellationToken ct)
    {
        var solver = new SatSolver();
        bool disposed = false;
        try
        {
            int nextVar = 0;

            ConfigureSolver(
                solver, grid, placements, shapes,
                exclusiveMap, connectionMap, options,
                ref nextVar);

            solver.SetMaxTime(remainingSeconds * options.MaxThreads);

            CryptoMiniSatNative.Lbool result;
            using (ct.Register(solver.Interrupt))
                result = solver.Solve();

            ct.ThrowIfCancellationRequested();

            if (result != CryptoMiniSatNative.Lbool.True)
            {
                solver.Dispose();
                disposed = true;
                return (null, result, null);
            }

            var model = solver.GetModel();
            if (retainSolver)
                return (model, result, solver);

            solver.Dispose();
            disposed = true;
            return (model, result, null);
        }
        catch
        {
            if (!disposed)
                solver.Dispose();
            throw;
        }
    }

    private static (bool?[]? model, CryptoMiniSatNative.Lbool result, SatSolver? retainedSolver) SolveWithBound(
        Grid grid,
        List<Placement> placements,
        IReadOnlyList<ClusterShape> shapes,
        Dictionary<(int, int), List<int>> exclusiveMap,
        Dictionary<(int, int), List<int>> connectionMap,
        IReadOnlyList<(int Row, int Col)> availableCells,
        Dictionary<(int, int), List<int>> exclusiveCoverageMap,
        int bound,
        SolverOptions options,
        double remainingSeconds,
        bool retainSolver,
        CancellationToken ct)
    {
        var solver = new SatSolver();
        bool disposed = false;
        try
        {
            int nextVar = 0;

            ConfigureSolver(
                solver, grid, placements, shapes,
                exclusiveMap, connectionMap, options,
                ref nextVar);

            int[] uncoveredLits = AddOneDirectionalCoverageChanneling(
                solver,
                availableCells,
                exclusiveCoverageMap,
                ref nextVar);

            BuildPermanentAmkConstraint(solver, uncoveredLits, bound, ref nextVar);

            solver.SetMaxTime(remainingSeconds * options.MaxThreads);

            CryptoMiniSatNative.Lbool result;
            using (ct.Register(solver.Interrupt))
                result = solver.Solve();

            ct.ThrowIfCancellationRequested();

            if (result != CryptoMiniSatNative.Lbool.True)
            {
                solver.Dispose();
                disposed = true;
                return (null, result, null);
            }

            var model = solver.GetModel();
            if (retainSolver)
                return (model, result, solver);

            solver.Dispose();
            disposed = true;
            return (model, result, null);
        }
        catch
        {
            if (!disposed)
                solver.Dispose();
            throw;
        }
    }

    private static void ConfigureSolver(
        SatSolver solver,
        Grid grid,
        List<Placement> placements,
        IReadOnlyList<ClusterShape> shapes,
        Dictionary<(int, int), List<int>> exclusiveMap,
        Dictionary<(int, int), List<int>> connectionMap,
        SolverOptions options,
        ref int nextVar)
    {
        if (options.MaxThreads > 1)
            solver.SetThreadCount(options.MaxThreads);

        nextVar = placements.Count;
        solver.AddVariables(placements.Count);
        AddAmoConstraints(solver, exclusiveMap, connectionMap, ref nextVar);
        AddSymmetryConstraints(solver, grid, placements, shapes,
            options.SymmetryType, options.SymmetryMode);
    }

    /// <summary>
    /// Descent step equals exclusive footprint size:
    /// 3-clip = 4, 4-clip = 5, 5-clip = 4.
    /// Connector sharing does not affect this because objective coverage is exclusive-only.
    /// </summary>
    private static int GetDescentStep(TetrisType type) => type switch
    {
        TetrisType.ThreeClip => 4,
        TetrisType.FourClip => 5,
        TetrisType.FiveClip => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported tetris type.")
    };

    private static int GetNextBound(int bestEmpty, int remainder, int step)
    {
        // Uncovered cells must remain in same congruence class modulo step.
        int alignedBestEmpty = AlignDownToResidue(bestEmpty, remainder, step);
        int nextBound = alignedBestEmpty - step;
        return nextBound >= 0 ? nextBound : -1;
    }

    private static int AlignDownToResidue(int value, int remainder, int step)
    {
        int residue = value % step;
        int delta = residue - remainder;
        if (delta < 0)
            delta += step;

        return value - delta;
    }

    private static int CountEmptyCells(bool?[] model, List<Placement> placements,
        IReadOnlyList<ClusterShape> shapes, int availableCells)
    {
        var coveredCells = new HashSet<(int, int)>();
        for (int i = 0; i < placements.Count; i++)
        {
            if (model[i] != true) continue;
            var p = placements[i];
            var offsets = shapes[p.ShapeIndex].Offsets;
            for (int j = 0; j < offsets.Count; j++)
            {
                if (offsets[j].Role == CellRole.Connection)
                    continue;

                coveredCells.Add((p.Row + offsets[j].DeltaRow, p.Col + offsets[j].DeltaCol));
            }
        }
        return availableCells - coveredCells.Count;
    }

    private static int CountClusters(bool?[] model, int placementCount)
    {
        int count = 0;
        for (int i = 0; i < placementCount; i++)
        {
            if (model[i] == true)
                count++;
        }

        return count;
    }

    private static (Dictionary<(int, int), List<int>> exclusive, Dictionary<(int, int), List<int>> connection)
        BuildCellMaps(List<Placement> placements, IReadOnlyList<ClusterShape> shapes)
    {
        var exclusiveMap = new Dictionary<(int, int), List<int>>();
        var connectionMap = new Dictionary<(int, int), List<int>>();

        for (int i = 0; i < placements.Count; i++)
        {
            var p = placements[i];
            var offsets = shapes[p.ShapeIndex].Offsets;

            for (int j = 0; j < offsets.Count; j++)
            {
                var offset = offsets[j];
                int r = p.Row + offset.DeltaRow;
                int c = p.Col + offset.DeltaCol;
                var key = (r, c);

                var map = offset.Role == CellRole.Connection ? connectionMap : exclusiveMap;

                if (!map.TryGetValue(key, out var list))
                {
                    list = [];
                    map[key] = list;
                }

                list.Add(i);
            }
        }

        return (exclusiveMap, connectionMap);
    }

    private static int[] AddOneDirectionalCoverageChanneling(
        SatSolver solver,
        IReadOnlyList<(int Row, int Col)> availableCells,
        Dictionary<(int, int), List<int>> exclusiveCoverageMap,
        ref int nextVar)
    {
        solver.AddVariables(availableCells.Count);

        int firstCoveredLit = nextVar + 1;
        nextVar += availableCells.Count;

        int[] uncoveredLits = new int[availableCells.Count];

        for (int i = 0; i < availableCells.Count; i++)
        {
            int coveredLit = firstCoveredLit + i;
            int uncoveredLit = -coveredLit;
            uncoveredLits[i] = uncoveredLit;

            var cell = availableCells[i];
            if (exclusiveCoverageMap.TryGetValue(cell, out var coveringPlacements) && coveringPlacements.Count > 0)
            {
                int[] channelClause = new int[coveringPlacements.Count + 1];
                channelClause[0] = -coveredLit;
                for (int j = 0; j < coveringPlacements.Count; j++)
                    channelClause[j + 1] = coveringPlacements[j] + 1;

                solver.AddClause(channelClause);
            }
            else
            {
                solver.AddClause([-coveredLit]);
            }
        }

        return uncoveredLits;
    }

    private static void AddAmoConstraints(
        SatSolver solver,
        Dictionary<(int, int), List<int>> exclusiveMap,
        Dictionary<(int, int), List<int>> connectionMap,
        ref int nextVar)
    {
        // AMO among exclusive placements per cell:
        // pairwise for small sets, sequential counter for larger sets.
        foreach (var (cell, exclusiveList) in exclusiveMap)
        {
            int n = exclusiveList.Count;

            if (n > 1 && n <= PairwiseAmoThreshold)
            {
                for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    int litA = -(exclusiveList[i] + 1);
                    int litB = -(exclusiveList[j] + 1);
                    solver.AddClause([litA, litB]);
                }
            }
            else if (n > PairwiseAmoThreshold)
            {
                int auxCount = n - 1;
                solver.AddVariables(auxCount);

                int[] s = new int[auxCount];
                for (int i = 0; i < auxCount; i++)
                {
                    s[i] = nextVar + 1; // 1-based literal
                    nextVar++;
                }

                int firstX = exclusiveList[0] + 1;
                solver.AddClause([-firstX, s[0]]);

                int lastX = exclusiveList[n - 1] + 1;
                solver.AddClause([-lastX, -s[n - 2]]);

                for (int i = 1; i <= n - 2; i++)
                {
                    int x = exclusiveList[i] + 1;
                    int sPrev = s[i - 1];
                    int sCurr = s[i];

                    solver.AddClause([-x, sCurr]);
                    solver.AddClause([-x, -sPrev]);
                    solver.AddClause([-sPrev, sCurr]);
                }
            }

            // Each exclusive conflicts with each connection on same cell
            if (connectionMap.TryGetValue(cell, out var connList))
            {
                for (int i = 0; i < exclusiveList.Count; i++)
                for (int j = 0; j < connList.Count; j++)
                {
                    int litE = -(exclusiveList[i] + 1);
                    int litC = -(connList[j] + 1);
                    solver.AddClause([litE, litC]);
                }
            }
        }
    }

    private static void BuildPermanentAmkConstraint(
        SatSolver solver,
        ReadOnlySpan<int> literals,
        int maxTrue,
        ref int nextVar)
    {
        int inputCount = literals.Length;

        if (maxTrue < 0)
        {
            solver.AddVariables(1);
            int contradictionLit = nextVar + 1;
            nextVar++;
            solver.AddClause([contradictionLit]);
            solver.AddClause([-contradictionLit]);
            return;
        }

        if (inputCount <= 1 || maxTrue >= inputCount)
            return;

        if (maxTrue == 0)
        {
            for (int i = 0; i < inputCount; i++)
                solver.AddClause([-literals[i]]);
            return;
        }

        int[,] s = new int[inputCount - 1, maxTrue];
        int auxVarCount = (inputCount - 1) * maxTrue;
        solver.AddVariables(auxVarCount);

        for (int i = 0; i < inputCount - 1; i++)
        for (int j = 0; j < maxTrue; j++)
        {
            s[i, j] = nextVar + 1;
            nextVar++;
        }

        // Base row (i = 0)
        solver.AddClause([-literals[0], s[0, 0]]);
        for (int j = 1; j < maxTrue; j++)
            solver.AddClause([-s[0, j]]);

        // 0 < i < n - 1
        for (int i = 1; i < inputCount - 1; i++)
        {
            solver.AddClause([-literals[i], s[i, 0]]);
            solver.AddClause([-s[i - 1, 0], s[i, 0]]);

            for (int j = 1; j < maxTrue; j++)
            {
                solver.AddClause([-literals[i], -s[i - 1, j - 1], s[i, j]]);
                solver.AddClause([-s[i - 1, j], s[i, j]]);
            }

            // Overflow for this row
            solver.AddClause([-literals[i], -s[i - 1, maxTrue - 1]]);
        }

        // Overflow for last input
        solver.AddClause([-literals[inputCount - 1], -s[inputCount - 2, maxTrue - 1]]);
    }

    private static SolverResult DecodeModel(
        bool?[] model, List<Placement> placements, IReadOnlyList<ClusterShape> shapes,
        int availableCells, SolverStatus status)
    {
        var (Placements, EmptyCells) = DecodePlacements(model, placements, shapes, availableCells);
        return new SolverResult
        {
            Placements = Placements,
            AllSolutions = [Placements],
            EmptyCells = EmptyCells,
            Status = status
        };
    }

    private static (IReadOnlyList<Placement> Placements, int EmptyCells) DecodePlacements(
        bool?[] model, List<Placement> placements, IReadOnlyList<ClusterShape> shapes,
        int availableCells)
    {
        var result = new List<Placement>();
        var coveredCells = new HashSet<(int, int)>();

        for (int i = 0; i < placements.Count; i++)
        {
            if (model[i] != true)
                continue;

            var p = placements[i];
            result.Add(new Placement(p.Row, p.Col, p.ShapeIndex));

            var offsets = shapes[p.ShapeIndex].Offsets;
            for (int j = 0; j < offsets.Count; j++)
            {
                if (offsets[j].Role == CellRole.Connection)
                    continue;

                coveredCells.Add((p.Row + offsets[j].DeltaRow, p.Col + offsets[j].DeltaCol));
            }
        }

        return (result, availableCells - coveredCells.Count);
    }

    private static SolverResult EmptyResult(int availableCells, SolverStatus status = SolverStatus.NoSolution) =>
        new()
        {
            Placements = [],
            AllSolutions = [],
            EmptyCells = availableCells,
            Status = status
        };

    private static void AddSymmetryConstraints(
        SatSolver solver,
        Grid grid,
        List<Placement> placements,
        IReadOnlyList<ClusterShape> shapes,
        SymmetryType symmetryType,
        SymmetryMode symmetryMode)
    {
        if (symmetryType == SymmetryType.None)
            return;

        var transforms = GetSymmetryTransforms(symmetryType, grid);
        var lookup = new Dictionary<(int, int, int), int>(placements.Count);
        for (int i = 0; i < placements.Count; i++)
        {
            var p = placements[i];
            lookup[(p.Row, p.Col, p.ShapeIndex)] = i;
        }

        var visited = new HashSet<int>();
        var orbitBuffer = new List<int>();

        for (int i = 0; i < placements.Count; i++)
        {
            if (!visited.Add(i))
                continue;

            orbitBuffer.Clear();
            ComputeOrbit(i, placements, shapes, lookup, grid, transforms, orbitBuffer);

            foreach (int member in orbitBuffer)
                visited.Add(member);

            bool allTransformsFound = true;
            // Check if orbit is complete: every transform of every member maps to a valid placement
            foreach (int member in orbitBuffer)
            {
                var p = placements[member];
                var type = shapes[p.ShapeIndex].Type;
                foreach (var transform in transforms)
                {
                    var (tr, tc, tsi) = transform(p.Row, p.Col, p.ShapeIndex, grid, type);
                    if (!lookup.ContainsKey((tr, tc, tsi)))
                    {
                        allTransformsFound = false;
                        break;
                    }
                }
                if (!allTransformsFound)
                    break;
            }

            if (allTransformsFound && !OrbitMembersShareCells(orbitBuffer, placements, shapes))
            {
                // Complete orbit: all members must agree.
                int rep = orbitBuffer[0];
                int repLit = rep + 1;
                for (int j = 1; j < orbitBuffer.Count; j++)
                {
                    int other = orbitBuffer[j];
                    int otherLit = other + 1;
                    // rep → other: (-rep ∨ other)
                    solver.AddClause([-repLit, otherLit]);
                    // other → rep: (-other ∨ rep)
                    solver.AddClause([-otherLit, repLit]);
                }
            }
            else
            {
                if (symmetryMode == SymmetryMode.Hard)
                {
                    // Forbid all members
                    foreach (int member in orbitBuffer)
                        solver.AddClause([-(member + 1)]);
                }
                // Soft mode: no constraint
            }
        }
    }

    private static void ComputeOrbit(
        int placementIndex,
        List<Placement> placements,
        IReadOnlyList<ClusterShape> shapes,
        Dictionary<(int, int, int), int> lookup,
        Grid grid,
        IReadOnlyList<Func<int, int, int, Grid, TetrisType, (int r, int c, int shapeIdx)>> transforms,
        List<int> orbit)
    {
        orbit.Add(placementIndex);
        var seen = new HashSet<int> { placementIndex };

        // BFS: apply all transforms to all discovered orbit members
        for (int idx = 0; idx < orbit.Count; idx++)
        {
            var p = placements[orbit[idx]];
            var type = shapes[p.ShapeIndex].Type;

            foreach (var transform in transforms)
            {
                var (tr, tc, tsi) = transform(p.Row, p.Col, p.ShapeIndex, grid, type);
                if (lookup.TryGetValue((tr, tc, tsi), out int mapped) && seen.Add(mapped))
                    orbit.Add(mapped);
            }
        }
    }

    private static bool OrbitMembersShareCells(
        List<int> orbit,
        List<Placement> placements,
        IReadOnlyList<ClusterShape> shapes)
    {
        if (orbit.Count <= 1)
            return false;

        // Collect exclusive cells (Loader/Clip) for each orbit member
        var cellSets = new HashSet<(int, int)>[orbit.Count];
        for (int i = 0; i < orbit.Count; i++)
        {
            var p = placements[orbit[i]];
            var offsets = shapes[p.ShapeIndex].Offsets;
            var cells = new HashSet<(int, int)>();
            for (int j = 0; j < offsets.Count; j++)
            {
                if (offsets[j].Role != CellRole.Connection)
                    cells.Add((p.Row + offsets[j].DeltaRow, p.Col + offsets[j].DeltaCol));
            }
            cellSets[i] = cells;
        }

        for (int a = 0; a < orbit.Count; a++)
        for (int b = a + 1; b < orbit.Count; b++)
        {
            if (cellSets[a].Overlaps(cellSets[b]))
                return true;
        }

        return false;
    }

    private static IReadOnlyList<Func<int, int, int, Grid, TetrisType, (int r, int c, int shapeIdx)>>
        GetSymmetryTransforms(SymmetryType symmetryType, Grid grid)
    {
        if (symmetryType == SymmetryType.Rotation90 && grid.Height != grid.Width)
            throw new ArgumentException("90° rotation symmetry requires a square grid.", nameof(symmetryType));

        return symmetryType switch
        {
            SymmetryType.HorizontalReflection => [HorizontalReflect],
            SymmetryType.VerticalReflection => [VerticalReflect],
            SymmetryType.BothReflection => [HorizontalReflect, VerticalReflect, Rotate180],
            SymmetryType.Rotation180 => [Rotate180],
            SymmetryType.Rotation90 => [Rotate90CW, Rotate180, Rotate90CCW],
            _ => []
        };
    }

    private static (int r, int c, int shapeIdx) HorizontalReflect(
        int r, int c, int shapeIndex, Grid grid, TetrisType type)
    {
        int newR = grid.Height - 1 - r;
        int newSi = type switch
        {
            TetrisType.FourClip => 0,
            TetrisType.FiveClip => shapeIndex switch { 1 => 3, 3 => 1, _ => shapeIndex },
            _ => shapeIndex switch { 0 => 2, 2 => 0, _ => shapeIndex }
        };
        return (newR, c, newSi);
    }

    private static (int r, int c, int shapeIdx) VerticalReflect(
        int r, int c, int shapeIndex, Grid grid, TetrisType type)
    {
        int newC = grid.Width - 1 - c;
        int newSi = type switch
        {
            TetrisType.FourClip => 0,
            TetrisType.FiveClip => shapeIndex switch { 0 => 2, 2 => 0, _ => shapeIndex },
            _ => shapeIndex switch { 1 => 3, 3 => 1, _ => shapeIndex }
        };
        return (r, newC, newSi);
    }

    private static (int r, int c, int shapeIdx) Rotate180(
        int r, int c, int shapeIndex, Grid grid, TetrisType type)
    {
        int newR = grid.Height - 1 - r;
        int newC = grid.Width - 1 - c;
        int newSi = type == TetrisType.FourClip ? 0 : shapeIndex switch
        {
            0 => 2, 2 => 0, 1 => 3, 3 => 1, _ => shapeIndex
        };
        return (newR, newC, newSi);
    }

    private static (int r, int c, int shapeIdx) Rotate90CW(
        int r, int c, int shapeIndex, Grid grid, TetrisType type)
    {
        // (r,c) → (c, H-1-r)
        int newR = c;
        int newC = grid.Height - 1 - r;
        int newSi = type == TetrisType.FourClip ? 0 : shapeIndex switch
        {
            0 => 1, 1 => 2, 2 => 3, 3 => 0, _ => shapeIndex
        };
        return (newR, newC, newSi);
    }

    private static (int r, int c, int shapeIdx) Rotate90CCW(
        int r, int c, int shapeIndex, Grid grid, TetrisType type)
    {
        // (r,c) → (H-1-c, r)
        int newR = grid.Height - 1 - c;
        int newC = r;
        int newSi = type == TetrisType.FourClip ? 0 : shapeIndex switch
        {
            0 => 3, 3 => 2, 2 => 1, 1 => 0, _ => shapeIndex
        };
        return (newR, newC, newSi);
    }
}
