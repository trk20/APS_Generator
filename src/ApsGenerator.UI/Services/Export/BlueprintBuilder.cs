using System.Globalization;
using System.Numerics;
using ApsGenerator.Core.Models;

namespace ApsGenerator.UI.Services.Export;

internal static class BlueprintBuilder
{
    private sealed record EmittedBlock(int BlockId, int RotationCode, string BlockData, int MaterialCost);

    // Block axis definitions (verified by BlockRotationTests)
    private static readonly Vector3 LoaderPrimary = Vector3.UnitY;
    private static readonly Vector3 LoaderSecondary = Vector3.UnitZ;
    private static readonly Vector3 LoaderSecondaryTarget = Vector3.UnitY;

    private static readonly Vector3 ClipPrimary = -Vector3.UnitY;
    private static readonly Vector3 ClipSecondary = Vector3.UnitZ;
    private static readonly Vector3 ClipHorizontalSecondaryTarget = Vector3.UnitY;
    private static readonly Vector3 ClipVerticalSecondaryTarget = Vector3.UnitZ;

    private static readonly Vector3 IntakePrimary = Vector3.UnitZ;
    private static readonly Vector3 IntakeSecondary = Vector3.UnitY;

    private static readonly Vector3 EjectorPrimary = Vector3.UnitZ;
    private static readonly Vector3 EjectorSecondary = Vector3.UnitY;
    private static readonly Vector3 EjectorSecondaryTarget = -Vector3.UnitY;

    // Cooler_1 at BLR=0 connects Forward(+Z) and Back(-Z).
    // For vertical stacking in 5-clip, orient to connect Up(+Y) and Down(-Y).
    private static readonly int Cooler1VerticalBlr = BlockRotation.FindRotation(
        Vector3.UnitZ, Vector3.UnitY);  // local Forward → world Up

    // Default facing directions
    private static readonly Vector3 DefaultLoaderTarget = -Vector3.UnitZ;
    private static readonly Vector3 DefaultEjectorTarget = Vector3.UnitZ;

    public static BlueprintFile Build(
        IReadOnlyList<Placement> placements,
        Grid grid,
        TetrisType type,
        ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(options);

        ValidateTargetHeight(type, options.TargetHeight);

        var emittedBlocks = new Dictionary<(int X, int Y, int Z), EmittedBlock>();

        if (placements.Count > 0)
        {
            if (type == TetrisType.FiveClip)
                EmitFiveClipBlocks(placements, grid, options.TargetHeight, emittedBlocks);
            else
                EmitScaleBasicBlocks(placements, grid, type, options.TargetHeight, emittedBlocks);
        }

        return AssembleBlueprint(options.BlueprintName, options.TargetHeight, emittedBlocks);
    }

    private static void ValidateTargetHeight(TetrisType type, int targetHeight)
    {
        if (type == TetrisType.FiveClip)
        {
            if (targetHeight < 3 || targetHeight % 3 != 0)
                throw new ArgumentException($"5-clip target height must be a positive multiple of 3 (got {targetHeight}).");

            return;
        }

        if (targetHeight < 1 || targetHeight > 8)
            throw new ArgumentException($"Target height must be between 1 and 8 for 3-clip and 4-clip exports (got {targetHeight}).");
    }

    private static void EmitScaleBasicBlocks(
        IReadOnlyList<Placement> placements,
        Grid grid,
        TetrisType type,
        int targetHeight,
        Dictionary<(int X, int Y, int Z), EmittedBlock> emittedBlocks)
    {
        IReadOnlyList<ClusterShape> shapes = ClusterShape.GetShapes(type);

        foreach (Placement placement in placements)
        {
            ClusterShape shape = GetShape(shapes, placement, type);
            CellOffset loaderOffset = GetLoaderOffset(shape);
            Vector3 loaderTarget = type == TetrisType.ThreeClip
                ? DetermineOpenDirection(shape)
                : DefaultLoaderTarget;

            int loaderRow = placement.Row + loaderOffset.DeltaRow;
            int loaderCol = placement.Col + loaderOffset.DeltaCol;
            (int loaderX, int loaderZ) = ToGameCoordinates(grid, loaderRow, loaderCol);
            var reservedIntakePositions = new HashSet<(int X, int Z)>();

            string loaderKey = $"Loader_{targetHeight}";
            int loaderBlr = BlockRotation.FindRotation(LoaderPrimary, loaderTarget, LoaderSecondary, LoaderSecondaryTarget);
            EmitBlock(emittedBlocks, loaderX, 0, loaderZ, loaderKey, loaderBlr);

            Vector3 ejectorTarget = type == TetrisType.ThreeClip
                ? loaderTarget
                : DefaultEjectorTarget;
            int ejectorBlr = BlockRotation.FindRotation(EjectorPrimary, ejectorTarget, EjectorSecondary, EjectorSecondaryTarget);
            EmitBlock(emittedBlocks, loaderX, -1, loaderZ, "Ejector_1", ejectorBlr);
            ReserveEjectorClearanceCell(reservedIntakePositions, loaderX, loaderZ, ejectorTarget);

            foreach (CellOffset offset in shape.Offsets)
            {
                if (offset.Role != CellRole.Clip)
                    continue;

                int row = placement.Row + offset.DeltaRow;
                int col = placement.Col + offset.DeltaCol;
                (int gameX, int gameZ) = ToGameCoordinates(grid, row, col);

                Vector3 clipDirection = DetermineClipDirection(offset, loaderOffset);
                string clipKey = $"Clip_{targetHeight}";
                int clipBlr = BlockRotation.FindRotation(ClipPrimary, clipDirection, ClipSecondary, ClipHorizontalSecondaryTarget);
                EmitBlock(emittedBlocks, gameX, 0, gameZ, clipKey, clipBlr, GameData.SharedClipBlockData);

                if (!reservedIntakePositions.Contains((gameX, gameZ)))
                {
                    int intakeBlr = BlockRotation.FindRotation(IntakePrimary, Vector3.UnitY, IntakeSecondary, -Vector3.UnitZ);
                    EmitBlock(emittedBlocks, gameX, -1, gameZ, "AmmoIntake_1", intakeBlr,
                        GameData.GetAmmoIntakeBlockData(Vector3.UnitY));
                }
            }
        }
    }

    private static void EmitFiveClipBlocks(
        IReadOnlyList<Placement> placements,
        Grid grid,
        int targetHeight,
        Dictionary<(int X, int Y, int Z), EmittedBlock> emittedBlocks)
    {
        int sectionCount = targetHeight / 3;
        IReadOnlyList<ClusterShape> shapes = ClusterShape.GetShapes(TetrisType.FiveClip);

        int clip1Up = BlockRotation.FindRotation(ClipPrimary, Vector3.UnitY, ClipSecondary, ClipVerticalSecondaryTarget);
        int clip1Down = BlockRotation.FindRotation(ClipPrimary, -Vector3.UnitY, ClipSecondary, ClipVerticalSecondaryTarget);
        int loaderSouth = BlockRotation.FindRotation(LoaderPrimary, Vector3.UnitZ, LoaderSecondary, LoaderSecondaryTarget);
        int intakeUp = BlockRotation.FindRotation(IntakePrimary, Vector3.UnitY, IntakeSecondary, -Vector3.UnitZ);
        int intakeDown = BlockRotation.FindRotation(IntakePrimary, -Vector3.UnitY, IntakeSecondary, Vector3.UnitZ);
        string intakeUpBlockData = GameData.GetAmmoIntakeBlockData(Vector3.UnitY);
        string intakeDownBlockData = GameData.GetAmmoIntakeBlockData(-Vector3.UnitY);

        foreach (Placement placement in placements)
        {
            ClusterShape shape = GetShape(shapes, placement, TetrisType.FiveClip);
            CellOffset loaderOffset = GetLoaderOffset(shape);

            foreach (CellOffset offset in shape.Offsets)
            {
                int row = placement.Row + offset.DeltaRow;
                int col = placement.Col + offset.DeltaCol;
                (int gameX, int gameZ) = ToGameCoordinates(grid, row, col);

                for (int section = 0; section < sectionCount; section++)
                {
                    int baseY = section * 3;

                    if (offset.Role == CellRole.Loader)
                    {
                        EmitBlock(emittedBlocks, gameX, baseY, gameZ, "Clip_1", clip1Up, GameData.SharedClipBlockData);
                        EmitBlock(emittedBlocks, gameX, baseY + 1, gameZ, "Loader_1", loaderSouth);
                        EmitBlock(emittedBlocks, gameX, baseY + 2, gameZ, "Clip_1", clip1Down, GameData.SharedClipBlockData);
                        continue;
                    }

                    if (offset.Role == CellRole.Clip)
                    {
                        Vector3 clipDirection = DetermineClipDirection(offset, loaderOffset);
                        int clipBlr = BlockRotation.FindRotation(ClipPrimary, clipDirection, ClipSecondary, ClipHorizontalSecondaryTarget);

                        EmitBlock(emittedBlocks, gameX, baseY, gameZ, "AmmoIntake_1", intakeUp, intakeUpBlockData);
                        EmitBlock(emittedBlocks, gameX, baseY + 1, gameZ, "Clip_1", clipBlr, GameData.SharedClipBlockData);
                        EmitBlock(emittedBlocks, gameX, baseY + 2, gameZ, "AmmoIntake_1", intakeDown, intakeDownBlockData);
                        continue;
                    }

                    EmitBlock(emittedBlocks, gameX, baseY, gameZ, "Cooler_1", Cooler1VerticalBlr);
                    EmitBlock(emittedBlocks, gameX, baseY + 1, gameZ, "Cooler_1", Cooler1VerticalBlr);
                    EmitBlock(emittedBlocks, gameX, baseY + 2, gameZ, "Cooler_1", Cooler1VerticalBlr);
                }
            }
        }
    }

    // Cardinal direction indices: 0=row-1, 1=col+1, 2=row+1, 3=col-1
    // row-1 = North = Back, col+1 = East = Right, row+1 = South = Forward, col-1 = West = Left
    private static Face CardinalToFace(int cardinalIndex) => cardinalIndex switch
    {
        0 => Face.Back,    // row-1 = North = construct Back
        1 => Face.Right,   // col+1 = East = construct Right
        2 => Face.Forward, // row+1 = South = construct Forward
        3 => Face.Left,    // col-1 = West = construct Left
        _ => Face.Forward,
    };

    private static ClusterShape GetShape(IReadOnlyList<ClusterShape> shapes, Placement placement, TetrisType type)
    {
        if ((uint)placement.ShapeIndex >= (uint)shapes.Count)
            throw new ArgumentException(
                $"Placement shape index {placement.ShapeIndex} is out of range for {type}.");

        return shapes[placement.ShapeIndex];
    }

    private static CellOffset GetLoaderOffset(ClusterShape shape)
    {
        foreach (CellOffset offset in shape.Offsets)
        {
            if (offset.Role == CellRole.Loader)
                return offset;
        }

        throw new InvalidOperationException("Cluster shape does not define a loader cell.");
    }

    private static (int X, int Z) ToGameCoordinates(Grid grid, int row, int col)
    {
        int gameX = col;
        int gameZ = grid.Height - 1 - row;
        return (gameX, gameZ);
    }

    private static void EmitBlock(
        Dictionary<(int X, int Y, int Z), EmittedBlock> emittedBlocks,
        int x,
        int y,
        int z,
        string blockKey,
        int rotationCode,
        string blockData = "")
    {
        if (!GameData.Blocks.TryGetValue(blockKey, out BlockDefinition? blockDefinition))
            throw new InvalidOperationException($"Unknown block definition '{blockKey}'.");

        emittedBlocks[(x, y, z)] =
            new EmittedBlock(blockDefinition.BlockId, rotationCode, blockData, blockDefinition.MaterialCost);
    }

    private static Vector3 DetermineOpenDirection(ClusterShape shape)
    {
        CellOffset loader = GetLoaderOffset(shape);
        var occupiedDirections = new HashSet<Vector3>();

        foreach (CellOffset offset in shape.Offsets)
        {
            if (offset.Role != CellRole.Clip)
                continue;

            int deltaRow = offset.DeltaRow - loader.DeltaRow;
            int deltaCol = offset.DeltaCol - loader.DeltaCol;
            occupiedDirections.Add(new Vector3(deltaCol, 0, deltaRow));
        }

        Vector3[] allDirections = [new(0, 0, -1), new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0)];
        foreach (Vector3 dir in allDirections)
        {
            if (!occupiedDirections.Contains(dir))
                return dir;
        }

        throw new InvalidOperationException("Unable to determine open direction for 3-clip shape.");
    }

    private static Vector3 DetermineClipDirection(CellOffset fromOffset, CellOffset loaderOffset)
    {
        int deltaRow = loaderOffset.DeltaRow - fromOffset.DeltaRow;
        int deltaCol = loaderOffset.DeltaCol - fromOffset.DeltaCol;
        return new Vector3(deltaCol, 0, deltaRow);
    }

    private static void ReserveEjectorClearanceCell(
        HashSet<(int X, int Z)> reservedPositions,
        int ejectorX,
        int ejectorZ,
        Vector3 ejectorTarget)
    {
        reservedPositions.Add((ejectorX - (int)ejectorTarget.X, ejectorZ + (int)ejectorTarget.Z));
    }

    private static BlueprintFile AssembleBlueprint(
        string blueprintName,
        int targetHeight,
        Dictionary<(int X, int Y, int Z), EmittedBlock> emittedBlocks)
    {
        var itemDictionary = new Dictionary<string, string>
        {
            ["0"] = ResolveItemGuid(0)
        };

        if (emittedBlocks.Count == 0)
            return CreateEmptyBlueprint(blueprintName, itemDictionary);

        var sortedCoordinates = emittedBlocks.Keys
            .OrderBy(coord => coord.Z)
            .ThenBy(coord => coord.Y)
            .ThenBy(coord => coord.X)
            .ToList();

        int minX = sortedCoordinates.Min(coord => coord.X);
        int minY = sortedCoordinates.Min(coord => coord.Y);
        int minZ = sortedCoordinates.Min(coord => coord.Z);
        int maxX = sortedCoordinates.Max(coord => coord.X);
        int maxZ = sortedCoordinates.Max(coord => coord.Z);
        int maxY = Math.Max(targetHeight - 1, sortedCoordinates.Max(coord => coord.Y));

        int sizeX = maxX - minX + 1;
        int sizeY = maxY - minY + 1;
        int sizeZ = maxZ - minZ + 1;

        var blockPositions = new List<string>(sortedCoordinates.Count);
        var blockRotations = new List<int>(sortedCoordinates.Count);
        var blockColorIndices = new List<int>(sortedCoordinates.Count);
        var blockIds = new List<int>(sortedCoordinates.Count);
        var usedBlockIds = new HashSet<int>();
        int totalMaterialCost = 0;

        using var blockDataStream = new MemoryStream();

        for (int index = 0; index < sortedCoordinates.Count; index++)
        {
            (int x, int y, int z) = sortedCoordinates[index];
            EmittedBlock emitted = emittedBlocks[(x, y, z)];

            blockPositions.Add(FormatRelativePosition(x, y, z, minX, minY, maxX, maxZ));
            blockRotations.Add(emitted.RotationCode);
            blockColorIndices.Add(GameData.DefaultBCI);
            blockIds.Add(emitted.BlockId);
            usedBlockIds.Add(emitted.BlockId);
            totalMaterialCost += emitted.MaterialCost;

            AppendBlockDataSegment(blockDataStream, emitted, index);
        }

        foreach (int blockId in usedBlockIds.OrderBy(id => id))
        {
            string key = blockId.ToString(CultureInfo.InvariantCulture);
            itemDictionary[key] = ResolveItemGuid(blockId);
        }

        string blockData = Convert.ToBase64String(blockDataStream.ToArray());
        int totalBlockCount = sortedCoordinates.Count;

        return new BlueprintFile
        {
            Name = blueprintName,
            SavedTotalBlockCount = totalBlockCount,
            SavedMaterialCost = totalMaterialCost,
            ContainedMaterialCost = totalMaterialCost,
            ItemDictionary = itemDictionary,
            Blueprint = new BlueprintBody
            {
                BLP = blockPositions,
                BLR = blockRotations,
                BCI = blockColorIndices,
                BlockIds = blockIds,
                BlockData = blockData,
                ContainedMaterialCost = totalMaterialCost,
                VehicleData = GameData.VehicleData,
                BlueprintName = blueprintName,
                GameVersion = GameData.GameVersion,
                MinCords = "0,0,0",
                MaxCords = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{sizeX},{sizeY},{sizeZ}"),
                TotalBlockCount = totalBlockCount,
                AliveCount = totalBlockCount,
                BlockCount = totalBlockCount,
                AuthorDetails = new BlueprintAuthorDetails
                {
                    CreatorId = GameData.CreatorId,
                    CreatorReadableName = GameData.CreatorReadableName,
                    ObjectId = Guid.NewGuid().ToString()
                }
            }
        };
    }

    private static BlueprintFile CreateEmptyBlueprint(string blueprintName, Dictionary<string, string> itemDictionary)
    {
        return new BlueprintFile
        {
            Name = blueprintName,
            SavedTotalBlockCount = 0,
            SavedMaterialCost = 0,
            ContainedMaterialCost = 0,
            ItemDictionary = itemDictionary,
            Blueprint = new BlueprintBody
            {
                BlueprintName = blueprintName,
                MinCords = "0,0,0",
                MaxCords = "0,0,0",
                TotalBlockCount = 0,
                AliveCount = 0,
                BlockCount = 0,
                AuthorDetails = new BlueprintAuthorDetails
                {
                    CreatorId = GameData.CreatorId,
                    CreatorReadableName = GameData.CreatorReadableName,
                    ObjectId = Guid.NewGuid().ToString()
                }
            }
        };
    }

    private static string ResolveItemGuid(int blockId)
    {
        if (GameData.ItemGuids.TryGetValue(blockId, out string? guid))
            return guid;

        throw new InvalidOperationException($"No item GUID mapping found for block id {blockId}.");
    }

    private static string FormatRelativePosition(
        int gameX,
        int gameY,
        int gameZ,
        int minX,
        int minY,
        int maxX,
        int maxZ)
    {
        int relX = gameX - minX - ((maxX - minX) / 2);
        int relY = gameY - minY;
        int relZ = maxZ - gameZ;

        return string.Create(CultureInfo.InvariantCulture, $"{relX},{relY},{relZ}");
    }

    private static void AppendBlockDataSegment(MemoryStream blockDataStream, EmittedBlock emittedBlock, int index)
    {
        byte[] segmentBytes;

        try
        {
            segmentBytes = Convert.FromBase64String(emittedBlock.BlockData);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Invalid Base64 block data for block id {emittedBlock.BlockId}.",
                ex);
        }

        if (segmentBytes.Length < 3)
            return;

        segmentBytes[0] = (byte)(index & 0xFF);
        segmentBytes[1] = (byte)((index >> 8) & 0xFF);
        segmentBytes[2] = (byte)((index >> 16) & 0xFF);
        blockDataStream.Write(segmentBytes, 0, segmentBytes.Length);
    }
}
