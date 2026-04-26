namespace ApsGenerator.Solver.Interop;

internal sealed class SatSolver : IDisposable
{
    private const int StackAllocThreshold = 128;

    private nint solverHandle;

    public SatSolver()
    {
        solverHandle = CryptoMiniSatNative.CmsatNew();
        if (solverHandle == 0)
        {
            throw new InvalidOperationException("Failed to create CryptoMiniSat solver instance.");
        }
    }

    public void Dispose()
    {
        if (solverHandle != 0)
        {
            CryptoMiniSatNative.CmsatFree(solverHandle);
            solverHandle = 0;
        }

        GC.SuppressFinalize(this);
    }

    ~SatSolver()
    {
        if (solverHandle != 0)
        {
            CryptoMiniSatNative.CmsatFree(solverHandle);
            solverHandle = 0;
        }
    }

    public void AddVariables(int count)
    {
        EnsureNotDisposed();

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count == 0)
        {
            return;
        }

        CryptoMiniSatNative.CmsatNewVars(solverHandle, (nuint)count);
    }

    public unsafe void AddClause(ReadOnlySpan<int> literals)
    {
        EnsureNotDisposed();

        Span<CryptoMiniSatNative.CLit> clause = literals.Length <= StackAllocThreshold
            ? stackalloc CryptoMiniSatNative.CLit[literals.Length]
            : new CryptoMiniSatNative.CLit[literals.Length];

        for (int i = 0; i < literals.Length; i++)
        {
            clause[i] = ToNativeLit(literals[i], nameof(literals));
        }

        fixed (CryptoMiniSatNative.CLit* clausePtr = clause)
        {
            CryptoMiniSatNative.CmsatAddClause(solverHandle, clausePtr, (nuint)clause.Length);
        }
    }

    public CryptoMiniSatNative.Lbool Solve()
    {
        EnsureNotDisposed();
        return ToLbool(CryptoMiniSatNative.CmsatSolve(solverHandle));
    }

    public unsafe CryptoMiniSatNative.Lbool SolveWithAssumptions(ReadOnlySpan<int> literals)
    {
        EnsureNotDisposed();

        Span<CryptoMiniSatNative.CLit> assumptions = literals.Length <= StackAllocThreshold
            ? stackalloc CryptoMiniSatNative.CLit[literals.Length]
            : new CryptoMiniSatNative.CLit[literals.Length];

        for (int i = 0; i < literals.Length; i++)
        {
            assumptions[i] = ToNativeLit(literals[i], nameof(literals));
        }

        fixed (CryptoMiniSatNative.CLit* assumptionsPtr = assumptions)
        {
            return ToLbool(CryptoMiniSatNative.CmsatSolveWithAssumptions(solverHandle, assumptionsPtr, (nuint)assumptions.Length));
        }
    }

    public unsafe bool?[] GetModel()
    {
        EnsureNotDisposed();

        CryptoMiniSatNative.SliceLbool modelSlice = CryptoMiniSatNative.CmsatGetModel(solverHandle);
        int count = checked((int)modelSlice.NumVals);
        bool?[] model = new bool?[count];

        if (count == 0 || modelSlice.Vals == 0)
        {
            return model;
        }

        CryptoMiniSatNative.CLbool* values = (CryptoMiniSatNative.CLbool*)modelSlice.Vals;
        for (int i = 0; i < count; i++)
        {
            model[i] = ToNullableBool(values[i]);
        }

        return model;
    }

    public void SetThreadCount(int n)
    {
        EnsureNotDisposed();

        if (n <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n));
        }

        CryptoMiniSatNative.CmsatSetNumThreads(solverHandle, (uint)n);
    }

    public void SetVerbosity(int n)
    {
        EnsureNotDisposed();

        if (n < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n));
        }

        CryptoMiniSatNative.CmsatSetVerbosity(solverHandle, (uint)n);
    }

    public void SetMaxTime(double seconds)
    {
        EnsureNotDisposed();

        if (seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        CryptoMiniSatNative.CmsatSetMaxTime(solverHandle, seconds);
    }

    public void SetDefaultPolarity(bool polarity)
    {
        EnsureNotDisposed();
        CryptoMiniSatNative.CmsatSetDefaultPolarity(solverHandle, polarity ? 1 : 0);
    }

    public void SetNoBve()
    {
        EnsureNotDisposed();
        CryptoMiniSatNative.CmsatSetNoBve(solverHandle);
    }

    public void SetMaxConflicts(ulong maxConfl)
    {
        EnsureNotDisposed();
        CryptoMiniSatNative.CmsatSetMaxConfl(solverHandle, maxConfl);
    }

    public void SetTimeoutAllCalls(double seconds)
    {
        EnsureNotDisposed();
        CryptoMiniSatNative.CmsatSetTimeoutAllCalls(solverHandle, seconds);
    }

    /// <summary>
    /// Thread-safe. Signals the solver to abort as soon as possible.
    /// Safe to call from a different thread while Solve() is blocking.
    /// </summary>
    public void Interrupt()
    {
        nint handle = solverHandle;
        if (handle != 0)
            CryptoMiniSatNative.CmsatInterruptAsap(handle);
    }

    private static CryptoMiniSatNative.CLit ToNativeLit(int literal, string paramName)
    {
        if (literal == 0)
        {
            throw new ArgumentException("Literal cannot be zero.", paramName);
        }

        if (literal == int.MinValue)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }

        int variable = Math.Abs(literal) - 1;
        bool negated = literal < 0;
        return CryptoMiniSatNative.MakeLit(variable, negated);
    }

    private static CryptoMiniSatNative.Lbool ToLbool(CryptoMiniSatNative.CLbool value)
    {
        return value.x switch
        {
            0 => CryptoMiniSatNative.Lbool.True,
            1 => CryptoMiniSatNative.Lbool.False,
            2 => CryptoMiniSatNative.Lbool.Undef,
            _ => throw new InvalidDataException($"Invalid lbool value '{value.x}'."),
        };
    }

    private static bool? ToNullableBool(CryptoMiniSatNative.CLbool value)
    {
        return value.x switch
        {
            0 => true,
            1 => false,
            2 => null,
            _ => throw new InvalidDataException($"Invalid model value '{value.x}'."),
        };
    }

    private void EnsureNotDisposed()
    {
        if (solverHandle == 0)
        {
            throw new ObjectDisposedException(nameof(SatSolver));
        }
    }
}