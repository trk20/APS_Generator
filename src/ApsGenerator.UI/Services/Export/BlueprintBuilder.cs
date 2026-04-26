using System.Globalization;
using ApsGenerator.Core.Models;

namespace ApsGenerator.UI.Services.Export;

internal static class BlueprintBuilder
{
    private sealed record EmittedBlock(int BlockId, int RotationCode, string BlockData, int MaterialCost);

    public static BlueprintFile Build(IReadOnlyList<Placement> placements, Grid grid, TetrisType type, ExportOptions options)
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
            LogicalOrientation loaderOrientation =
                type == TetrisType.ThreeClip
                    ? DetermineThreeClipOpenDirection(shape)
                    : LogicalOrientation.North;

            int loaderRow = placement.Row + loaderOffset.DeltaRow;
            int loaderCol = placement.Col + loaderOffset.DeltaCol;
            (int loaderX, int loaderZ) = ToGameCoordinates(grid, loaderRow, loaderCol);
            var reservedIntakePositions = new HashSet<(int X, int Z)>();

            string loaderKey = $"Loader_{targetHeight}";
            EmitBlock(emittedBlocks, loaderX, 0, loaderZ, loaderKey, loaderOrientation);

            LogicalOrientation ejectorOrientation =
                type == TetrisType.ThreeClip
                    ? DetermineThreeClipEjectorOrientation(loaderOrientation)
                    : LogicalOrientation.North;

            EmitBlock(emittedBlocks, loaderX, -1, loaderZ, "Ejector_1", ejectorOrientation);
            ReserveEjectorClearanceCell(reservedIntakePositions, loaderX, loaderZ, ejectorOrientation);

            foreach (CellOffset offset in shape.Offsets)
            {
                if (offset.Role != CellRole.Clip)
                    continue;

                int row = placement.Row + offset.DeltaRow;
                int col = placement.Col + offset.DeltaCol;
                (int gameX, int gameZ) = ToGameCoordinates(grid, row, col);

                LogicalOrientation clipOrientation = DetermineOrientationTowardLoader(offset, loaderOffset);
                string clipKey = $"Clip_{targetHeight}";
                EmitBlock(emittedBlocks, gameX, 0, gameZ, clipKey, clipOrientation);

                if (!reservedIntakePositions.Contains((gameX, gameZ)))
                    EmitBlock(emittedBlocks, gameX, -1, gameZ, "AmmoIntake_1", LogicalOrientation.Up);
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
                        EmitBlock(emittedBlocks, gameX, baseY, gameZ, "Clip_1", LogicalOrientation.Up);
                        EmitBlock(emittedBlocks, gameX, baseY + 1, gameZ, "Loader_1", LogicalOrientation.South);
                        EmitBlock(emittedBlocks, gameX, baseY + 2, gameZ, "Clip_1", LogicalOrientation.Down);
                        continue;
                    }

                    if (offset.Role == CellRole.Clip)
                    {
                        LogicalOrientation towardLoader = DetermineOrientationTowardLoader(offset, loaderOffset);

                        EmitBlock(emittedBlocks, gameX, baseY, gameZ, "AmmoIntake_1", LogicalOrientation.Up);
                        EmitBlock(emittedBlocks, gameX, baseY + 1, gameZ, "Clip_1", towardLoader);
                        EmitBlock(emittedBlocks, gameX, baseY + 2, gameZ, "AmmoIntake_1", LogicalOrientation.Down);
                        continue;
                    }

                    EmitBlock(emittedBlocks, gameX, baseY, gameZ, "Cooler_1", LogicalOrientation.West);
                    EmitBlock(emittedBlocks, gameX, baseY + 1, gameZ, "Cooler_1", LogicalOrientation.West);
                    EmitBlock(emittedBlocks, gameX, baseY + 2, gameZ, "Cooler_1", LogicalOrientation.West);
                }
            }
        }
    }

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
        LogicalOrientation orientation)
    {
        if (!GameData.Blocks.TryGetValue(blockKey, out BasicBlockDefinition? blockDefinition))
            throw new InvalidOperationException($"Unknown block definition '{blockKey}'.");

        int rotationCode = ResolveRotationCode(blockDefinition, orientation);
        string blockData = ResolveBlockData(blockDefinition, orientation);

        emittedBlocks[(x, y, z)] =
            new EmittedBlock(blockDefinition.BlockId, rotationCode, blockData, blockDefinition.MaterialCost);
    }

    private static int ResolveRotationCode(BasicBlockDefinition blockDefinition, LogicalOrientation orientation)
    {
        if (blockDefinition.RotationMap.TryGetValue(orientation, out int rotationCode))
            return rotationCode;

        return blockDefinition.DefaultRotationCode;
    }

    private static string ResolveBlockData(BasicBlockDefinition blockDefinition, LogicalOrientation orientation)
    {
        if (blockDefinition.BlockDataMap is not null &&
            blockDefinition.BlockDataMap.TryGetValue(orientation, out string? blockDataByOrientation))
        {
            return blockDataByOrientation;
        }

        return blockDefinition.DefaultBlockData;
    }

    private static LogicalOrientation DetermineThreeClipOpenDirection(ClusterShape shape)
    {
        CellOffset loader = GetLoaderOffset(shape);
        var occupiedDirections = new HashSet<LogicalOrientation>();

        foreach (CellOffset offset in shape.Offsets)
        {
            if (offset.Role != CellRole.Clip)
                continue;

            int deltaRow = offset.DeltaRow - loader.DeltaRow;
            int deltaCol = offset.DeltaCol - loader.DeltaCol;
            occupiedDirections.Add(ToHorizontalOrientation(deltaRow, deltaCol));
        }

        LogicalOrientation[] allDirections =
        [
            LogicalOrientation.North,
            LogicalOrientation.East,
            LogicalOrientation.South,
            LogicalOrientation.West
        ];

        foreach (LogicalOrientation direction in allDirections)
        {
            if (!occupiedDirections.Contains(direction))
                return direction;
        }

        throw new InvalidOperationException("Unable to determine open direction for 3-clip shape.");
    }

    private static LogicalOrientation DetermineOrientationTowardLoader(CellOffset fromOffset, CellOffset loaderOffset)
    {
        int deltaRow = loaderOffset.DeltaRow - fromOffset.DeltaRow;
        int deltaCol = loaderOffset.DeltaCol - fromOffset.DeltaCol;
        return ToHorizontalOrientation(deltaRow, deltaCol);
    }

    private static LogicalOrientation OppositeHorizontalOrientation(LogicalOrientation orientation)
    {
        return orientation switch
        {
            LogicalOrientation.North => LogicalOrientation.South,
            LogicalOrientation.South => LogicalOrientation.North,
            LogicalOrientation.East => LogicalOrientation.West,
            LogicalOrientation.West => LogicalOrientation.East,
            _ => throw new InvalidOperationException($"Unsupported horizontal orientation '{orientation}'.")
        };
    }

    private static LogicalOrientation DetermineThreeClipEjectorOrientation(LogicalOrientation loaderOrientation)
    {
        if (loaderOrientation is LogicalOrientation.East or LogicalOrientation.West)
            return loaderOrientation;

        return OppositeHorizontalOrientation(loaderOrientation);
    }

    private static void ReserveEjectorClearanceCell(
        HashSet<(int X, int Z)> reservedPositions,
        int ejectorX,
        int ejectorZ,
        LogicalOrientation ejectorOrientation)
    {
        LogicalOrientation clearanceDirection = OppositeHorizontalOrientation(ejectorOrientation);
        (int deltaX, int deltaZ) = ToHorizontalOffset(clearanceDirection);
        reservedPositions.Add((ejectorX + deltaX, ejectorZ + deltaZ));
    }

    private static (int DeltaX, int DeltaZ) ToHorizontalOffset(LogicalOrientation orientation)
    {
        return orientation switch
        {
            LogicalOrientation.North => (0, -1),
            LogicalOrientation.South => (0, 1),
            LogicalOrientation.East => (1, 0),
            LogicalOrientation.West => (-1, 0),
            _ => throw new InvalidOperationException($"Unsupported horizontal orientation '{orientation}'.")
        };
    }

    private static LogicalOrientation ToHorizontalOrientation(int deltaRow, int deltaCol)
    {
        if (deltaRow == -1 && deltaCol == 0)
            return LogicalOrientation.North;

        if (deltaRow == 1 && deltaCol == 0)
            return LogicalOrientation.South;

        if (deltaRow == 0 && deltaCol == 1)
            return LogicalOrientation.East;

        if (deltaRow == 0 && deltaCol == -1)
            return LogicalOrientation.West;

        throw new InvalidOperationException($"Unsupported directional delta ({deltaRow}, {deltaCol}).");
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
        int maxY = targetHeight - 1;

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
