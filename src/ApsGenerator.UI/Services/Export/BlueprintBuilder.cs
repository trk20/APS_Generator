using System.Globalization;
using ApsGenerator.Core.Export;
using ApsGenerator.Core.Models;

namespace ApsGenerator.UI.Services.Export;

internal static class BlueprintBuilder
{
    public static BlueprintFile Build(
        IReadOnlyList<Placement> placements,
        Grid grid,
        TetrisType type,
        ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var layout = ApsBlockLayout.Build(placements, grid, type,
            new BlockLayoutOptions(options.TargetHeight, options.ExtraLayers, options.CoolerSnakes),
            BlockOrientationConvention.BlueprintFile);
        return AssembleBlueprint(options.BlueprintName, options.TargetHeight, layout);
    }

    private static BlueprintFile AssembleBlueprint(
        string blueprintName,
        int targetHeight,
        IReadOnlyDictionary<(int X, int Y, int Z), LayoutBlock> emittedBlocks)
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
            LayoutBlock emitted = emittedBlocks[(x, y, z)];

            int relX = x - minX - ((maxX - minX) / 2);
            int relY = y - minY;
            int relZ = maxZ - z;
            blockPositions.Add(string.Create(CultureInfo.InvariantCulture, $"{relX},{relY},{relZ}"));
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

    private static void AppendBlockDataSegment(MemoryStream blockDataStream, LayoutBlock emittedBlock, int index)
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
