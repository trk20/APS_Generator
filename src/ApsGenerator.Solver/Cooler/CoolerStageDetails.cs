namespace ApsGenerator.Solver.Cooler;

/// <summary>
/// Human-readable cooler solve stage labels for <see cref="CoolerStageResult.Detail"/>.
/// </summary>
internal static class CoolerStageDetails
{
    public const string DeckOnly = "deck only";
    public const string ElevatedBridges = "elevated bridges";
    public const string LocalBridgeFailed = "local bridge failed";

    public const string Constructive = "constructive";
    public const string Undirected = "undirected";
    public const string UndirectedEmpty = "undirected: no ejector assignment";
    public const string UndirectedTimeout = "undirected: timeout";
    public const string UndirectedUnsat = "undirected: unsatisfiable";

    public const string FiveClip = "5-clip";
    public const string FiveClipWithFootprint = "5-clip with footprint";
    public const string FiveClipNoConnections = "5-clip: no connection shafts";
    public const string FiveClipDisconnected = "5-clip: disconnected";
}
