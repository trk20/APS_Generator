namespace ApsGenerator.Solver;

internal sealed class PackingEarlyStop
{
    private const double BaseBudget = 5.13;
    private const double DecayRate = 0.75;
    private const double MinimumMilliseconds = 177;
    private const int MinimumHistory = 2;
    private int count;
    private double totalMilliseconds;

    public bool Record(double milliseconds)
    {
        bool stop = count >= MinimumHistory && milliseconds >= MinimumMilliseconds &&
            milliseconds >= BaseBudget / (1 + DecayRate * Math.Max(0, count - MinimumHistory)) * totalMilliseconds;
        count++;
        totalMilliseconds += milliseconds;
        return stop;
    }
}
