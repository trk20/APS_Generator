using System.Runtime.InteropServices;

namespace ApsGenerator.Solver.Interop;

internal static class CryptoMiniSatNative
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
    // DllImport rather than LibraryImport for compatibility with netstandard2.0 for the mod version.
    [DllImport(LibraryName, EntryPoint = "cmsat_new", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint CmsatNew();

    [DllImport(LibraryName, EntryPoint = "cmsat_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CmsatFree(nint solver);

    [DllImport(LibraryName, EntryPoint = "cmsat_nvars", CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint CmsatNvars(nint solver);

    [DllImport(LibraryName, EntryPoint = "cmsat_add_clause", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern unsafe bool CmsatAddClause(nint solver, CLit* lits, nuint numLits);

    [DllImport(LibraryName, EntryPoint = "cmsat_new_vars", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CmsatNewVars(nint solver, nuint n);

    [DllImport(LibraryName, EntryPoint = "cmsat_solve", CallingConvention = CallingConvention.Cdecl)]
    internal static extern CLbool CmsatSolve(nint solver);

    [DllImport(LibraryName, EntryPoint = "cmsat_solve_with_assumptions", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe CLbool CmsatSolveWithAssumptions(nint solver, CLit* assumptions, nuint numAssumptions);

    [DllImport(LibraryName, EntryPoint = "cmsat_get_model", CallingConvention = CallingConvention.Cdecl)]
    internal static extern SliceLbool CmsatGetModel(nint solver);

    [DllImport(LibraryName, EntryPoint = "cmsat_set_num_threads", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CmsatSetNumThreads(nint solver, uint n);

    [DllImport(LibraryName, EntryPoint = "cmsat_set_verbosity", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CmsatSetVerbosity(nint solver, uint n);

    [DllImport(LibraryName, EntryPoint = "cmsat_set_max_time", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CmsatSetMaxTime(nint solver, double maxTime);

    [DllImport(LibraryName, EntryPoint = "cmsat_interrupt_asap", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CmsatInterruptAsap(nint solver);

    [DllImport(LibraryName, EntryPoint = "cmsat_set_default_polarity", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CmsatSetDefaultPolarity(nint solver, int polarity);

    [DllImport(LibraryName, EntryPoint = "cmsat_set_no_bve", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CmsatSetNoBve(nint solver);

    [DllImport(LibraryName, EntryPoint = "cmsat_set_max_confl", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CmsatSetMaxConfl(nint solver, ulong maxConfl);

    [DllImport(LibraryName, EntryPoint = "cmsat_set_timeout_all_calls", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CmsatSetTimeoutAllCalls(nint solver, double secs);

}
