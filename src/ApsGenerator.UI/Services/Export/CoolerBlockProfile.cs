namespace ApsGenerator.UI.Services.Export;

/// <summary>
/// Unused for now, will be used for future cooler snake export support
/// 
/// Defines the local-space connection profile for a cooler block type.
/// At BLR=0, these faces are the "connected" (open) faces of the block.
/// To orient the block, use BlockRotation to find a BLR that maps
/// these local faces to the desired world faces.
/// </summary>
internal static class CoolerBlockProfile
{
    // Block IDs
    public const int Cooler4WayId = 228;
    public const int Cooler5WayId = 229;
    public const int CoolerCornerId = 230;
    public const int CoolerSplitterId = 232;
    public const int CoolerMaterialCost = 50;

    public static readonly Face NonConnectingFace5Way = Face.Forward;

    public static readonly Face CornerFace1 = Face.Down;
    public static readonly Face CornerFace2 = Face.Back;

    public static readonly Face SplitterLateral1 = Face.Left;
    public static readonly Face SplitterLateral2 = Face.Right;
    public static readonly Face SplitterBranch = Face.Back;

    public static readonly Face FourWayNonConn1 = Face.Forward;
    public static readonly Face FourWayNonConn2 = Face.Up;

    /// <summary>
    /// Selects the appropriate cooler block ID and BLR for a snake cell
    /// based on which world directions need connections.
    /// </summary>
    public static (int BlockId, int Blr) SelectBlock(IReadOnlyList<Face> connectedWorldFaces)
    {
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
        // Non-connecting face points Down (away from useful connections above)
        return (Cooler5WayId, BlockRotation.FindRotationOrDefault(NonConnectingFace5Way, Face.Down));
    }

    private static (int BlockId, int Blr) SelectCorner(Face connectedWorldFace)
    {
        // Corner at BLR=0 connects Down + Back.
        // Map Back → neighbor direction, Down → Up (connects toward cooler layers above).
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
            // Splitter: straight-through pair + branch pointing Up.
            // Map splitter's lateral axis to the connected pair, branch → Up.
            int blr = BlockRotation.TryFindRotation(SplitterLateral1, a, SplitterBranch, Face.Up);
            if (blr < 0)
                blr = BlockRotation.TryFindRotation(SplitterLateral1, b, SplitterBranch, Face.Up);
            if (blr < 0)
                blr = 0;

            return (CoolerSplitterId, blr);
        }

        // Adjacent pair (90° turn): use 4-way block
        // Both non-connecting faces (Forward+Up at BLR=0) map to the 2 missing horizontal directions.
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
        // 5-way with non-connecting = the missing lateral direction
        Face missingFace = FindMissingFace(connectedWorldFaces);
        return (Cooler5WayId, BlockRotation.FindRotationOrDefault(NonConnectingFace5Way, missingFace));
    }

    private static Face FindMissingFace(IReadOnlyList<Face> connectedFaces)
    {
        Face[] laterals = [Face.Forward, Face.Back, Face.Right, Face.Left];
        foreach (Face f in laterals)
        {
            if (!connectedFaces.Contains(f))
                return f;
        }

        return Face.Down;
    }

    private static Face FindMissingLateral(Face connected1, Face connected2, bool first)
    {
        Face[] laterals = [Face.Forward, Face.Back, Face.Right, Face.Left];
        var missing = new List<Face>(2);
        foreach (Face f in laterals)
        {
            if (f != connected1 && f != connected2)
                missing.Add(f);
        }

        return first ? missing[0] : missing[1];
    }
}
