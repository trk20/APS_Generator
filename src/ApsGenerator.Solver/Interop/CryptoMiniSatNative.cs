using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ApsGenerator.Solver.Interop;

internal static partial class CryptoMiniSatNative
{
    private const string LibraryName = "cryptominisat5";

    [StructLayout(LayoutKind.Sequential)]
    internal struct CLit
    {
        public uint x;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CLbool
    {
        public byte x;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SliceLbool
    {
        public nint Vals;
        public nuint NumVals;
    }

    internal enum Lbool : byte
    {
        True = 0,
        False = 1,
        Undef = 2,
    }

    internal static CLit MakeLit(int variable, bool negated)
    {
        if (variable < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(variable));
        }

        return new CLit { x = (uint)(variable * 2 + (negated ? 1 : 0)) };
    }

    [LibraryImport(LibraryName, EntryPoint = "cmsat_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint CmsatNew();

    [LibraryImport(LibraryName, EntryPoint = "cmsat_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void CmsatFree(nint solver);

    [LibraryImport(LibraryName, EntryPoint = "cmsat_nvars")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint CmsatNvars(nint solver);

    [LibraryImport(LibraryName, EntryPoint = "cmsat_add_clause")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static unsafe partial bool CmsatAddClause(nint solver, CLit* lits, nuint numLits);

    [LibraryImport(LibraryName, EntryPoint = "cmsat_new_vars")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void CmsatNewVars(nint solver, nuint n);

    [LibraryImport(LibraryName, EntryPoint = "cmsat_solve")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial CLbool CmsatSolve(nint solver);

    [LibraryImport(LibraryName, EntryPoint = "cmsat_solve_with_assumptions")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial CLbool CmsatSolveWithAssumptions(nint solver, CLit* assumptions, nuint numAssumptions);

    [LibraryImport(LibraryName, EntryPoint = "cmsat_get_model")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial SliceLbool CmsatGetModel(nint solver);

    [LibraryImport(LibraryName, EntryPoint = "cmsat_set_num_threads")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void CmsatSetNumThreads(nint solver, uint n);

    [LibraryImport(LibraryName, EntryPoint = "cmsat_set_verbosity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void CmsatSetVerbosity(nint solver, uint n);

    [LibraryImport(LibraryName, EntryPoint = "cmsat_set_max_time")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void CmsatSetMaxTime(nint solver, double maxTime);

    [LibraryImport(LibraryName, EntryPoint = "cmsat_interrupt_asap")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void CmsatInterruptAsap(nint solver);

    [LibraryImport(LibraryName, EntryPoint = "cmsat_set_default_polarity")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void CmsatSetDefaultPolarity(nint solver, int polarity);

    [LibraryImport(LibraryName, EntryPoint = "cmsat_set_no_bve")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void CmsatSetNoBve(nint solver);

    [LibraryImport(LibraryName, EntryPoint = "cmsat_set_max_confl")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void CmsatSetMaxConfl(nint solver, ulong maxConfl);

    [LibraryImport(LibraryName, EntryPoint = "cmsat_set_timeout_all_calls")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void CmsatSetTimeoutAllCalls(nint solver, double secs);

}