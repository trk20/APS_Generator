using ApsGenerator.Core.Models;

namespace ApsGenerator.UI.Services.Export;

/// <summary>
/// Local-space connection profiles for cooler block variants.
/// At BLR=0, these faces are the connected (open) faces of the block.
/// </summary>
internal static class CoolerBlockProfile
{
    public const int Cooler4WayId = 228;
    public const int Cooler5WayId = 229;
    public const int CoolerCornerId = 230;
    public const int CoolerSplitterId = 232;

    private static readonly Face NonConnectingFace5Way = Face.Forward;

    private static readonly Face CornerFace1 = Face.Down;
    private static readonly Face CornerFace2 = Face.Back;

    private static readonly Face SplitterLateral1 = Face.Left;
    private static readonly Face SplitterBranch = Face.Back;

    private static readonly Face FourWayNonConn1 = Face.Forward;
    private static readonly Face FourWayNonConn2 = Face.Up;

    /// <summary>
    /// Map cooler open faces (N,E,S,W) plus vertical links to construct-space <see cref="Face"/> set.
    /// N,E,S,W ↔ Back,Right,Forward,Left (CoolerCardinals order).
    /// </summary>
    public static List<Face> FacesFrom(
        CoolerFaceFlags open,
        bool connectUp = false,
        bool connectDown = false)
    {
        var faces = Enumerable.Range(0, CardinalGameFaces.Length)
            .Where(d => (open & CoolerCardinals.FlagFor(d)) != 0)
            .Select(d => CardinalGameFaces[d])
            .ToList();

        if (connectUp) faces.Add(Face.Up);
        if (connectDown) faces.Add(Face.Down);
        return faces;
    }

    /// <summary>Game faces matching CoolerCardinals N,E,S,W order.</summary>
    private static readonly Face[] CardinalGameFaces =
        [Face.Back, Face.Right, Face.Forward, Face.Left];

    /// <summary>
    /// Selects the appropriate cooler block ID and BLR for a snake cell
    /// based on which world directions need connections.
    /// </summary>
    public static (int BlockId, int Blr) SelectBlock(IReadOnlyList<Face> connectedWorldFaces)
    {
        if (connectedWorldFaces.Any(f => f is Face.Up or Face.Down))
            return Select5Way(connectedWorldFaces);

        int count = connectedWorldFaces.Count;

        return count switch
        {
            0 or 4 => Select5Way(connectedWorldFaces),
            1 => SelectCorner(connectedWorldFaces[0]),
            2 => SelectTwoNeighbor(connectedWorldFaces),
            3 => Select3Neighbor(connectedWorldFaces),
            _ => (Cooler5WayId, 0),
        };
    }

    private static (int BlockId, int Blr) Select5Way(IReadOnlyList<Face> connectedWorldFaces)
    {
        Face[] preference = [Face.Down, Face.Up, Face.Forward, Face.Back, Face.Left, Face.Right];
        Face missing = preference.FirstOrDefault(c => !connectedWorldFaces.Contains(c), Face.Down);
        return (Cooler5WayId, BlockRotation.FindRotationOrDefault(NonConnectingFace5Way, missing));
    }

    private static (int BlockId, int Blr) SelectCorner(Face connectedWorldFace)
    {
        int blr = BlockRotation.TryFindRotation(CornerFace2, connectedWorldFace, CornerFace1, Face.Up);
        if (blr < 0)
            blr = 0;

        return (CoolerCornerId, blr);
    }

    private static (int BlockId, int Blr) SelectTwoNeighbor(IReadOnlyList<Face> connectedWorldFaces)
    {
        Face a = connectedWorldFaces[0];
        Face b = connectedWorldFaces[1];
        bool isOpposite = BlockRotation.OppositeFace(a) == b;

        if (isOpposite)
        {
            int blr = BlockRotation.TryFindRotation(SplitterLateral1, a, SplitterBranch, Face.Up);
            if (blr < 0)
                blr = BlockRotation.TryFindRotation(SplitterLateral1, b, SplitterBranch, Face.Up);
            if (blr < 0)
                blr = 0;

            return (CoolerSplitterId, blr);
        }

        Face nonConn1 = FindMissingLateral(a, b, first: true);
        Face nonConn2 = FindMissingLateral(a, b, first: false);

        int blr4 = BlockRotation.TryFindRotation(FourWayNonConn1, nonConn1, FourWayNonConn2, nonConn2);
        if (blr4 < 0)
            blr4 = BlockRotation.TryFindRotation(FourWayNonConn1, nonConn2, FourWayNonConn2, nonConn1);
        if (blr4 < 0)
            blr4 = 0;

        return (Cooler4WayId, blr4);
    }

    private static (int BlockId, int Blr) Select3Neighbor(IReadOnlyList<Face> connectedWorldFaces)
    {
        Face[] laterals = [Face.Forward, Face.Back, Face.Right, Face.Left];
        Face missingFace = laterals.FirstOrDefault(f => !connectedWorldFaces.Contains(f), Face.Down);
        return (Cooler5WayId, BlockRotation.FindRotationOrDefault(NonConnectingFace5Way, missingFace));
    }

    private static Face FindMissingLateral(Face connected1, Face connected2, bool first)
    {
        Face[] laterals = [Face.Forward, Face.Back, Face.Right, Face.Left];
        var missing = laterals.Where(f => f != connected1 && f != connected2).ToArray();
        return first ? missing[0] : missing[1];
    }
}
