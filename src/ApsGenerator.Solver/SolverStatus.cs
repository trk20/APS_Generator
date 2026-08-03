namespace ApsGenerator.Solver;

public enum SolverStatus
{
    Optimal,
    LikelyOptimal,
    TargetDensityReached,
    TimedOut,
    NoSolution
}