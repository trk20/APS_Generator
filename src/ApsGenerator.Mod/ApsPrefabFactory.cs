using ApsGenerator.Core.Export;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Core.JsonPlus.Converters;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Modding;
using BrilliantSkies.Modding.Containers;
using BrilliantSkies.Modding.Types;
using UnityEngine;
using ApsGenerator.Mod.Generation;

namespace ApsGenerator.Mod;

internal static class ApsPrefabFactory
{
    internal const string AnalyticsName = "ApsGenerator.Mod";

    internal static ApsGeneratedPrefab Create(AllConstruct construct, ApsSolution solution)
    {
        var geometry = solution.Request.Geometry;
        var layout = ApsBlockLayout.Build(solution.Result.Placements, solution.Grid, solution.Type,
            new BlockLayoutOptions(geometry.ComponentHeight, geometry.Layers, solution.Cooler),
            BlockOrientationConvention.NativePrefab);
        if (layout.Count == 0)
            throw new InvalidOperationException("The solver result contains no placeable blocks.");

        var definitions = layout.Values.Select(block => block.BlockId).Distinct()
            .ToDictionary(id => id, ResolveDefinition);
        Vector3[] positions = layout.Keys.Select(position => new Vector3(
            position.X + geometry.MinimumX, position.Y + geometry.TetrisBottom, position.Z)).ToArray();
        int height = Math.Max(geometry.IntendedHeight,
            Mathf.RoundToInt(positions.Max(position => position.y) - positions.Min(position => position.y)) + 1);
        int count = layout.Count;
        var blueprint = new Blueprint(construct.CreatorDetails)
        {
            blueprintName = "Generated APS",
            GameVersion = Plugin.GameVersion,
            BLP = positions,
            BlockIds = layout.Values.Select(block => definitions[block.BlockId].ComponentId.RuntimeId).ToArray(),
            BCI = new int[count],
            BlockData = Array.Empty<byte>(),
            BlockState = new DeadState[count],
            AliveCount = count,
            TotalBlockCount = count,
            MinCords = Vector3.zero,
            MaxCords = new Vector3(geometry.Width, height, geometry.Depth)
        };
        blueprint.BLR = layout.Values.Select(block => block.RotationCode).ToArray();
        var prefab = new SavedSubObject(blueprint) { AnalyticsName = AnalyticsName };
        float cost = layout.Values.Sum(block => definitions[block.BlockId].Cost.Material);
        return new ApsGeneratedPrefab(prefab, cost);
    }

    private static ItemDefinition ResolveDefinition(int blockId)
    {
        var guid = new Guid(GameData.ItemGuids[blockId]);
        return Configured.i.Get<ModificationComponentContainerItem>().Find(guid)
            ?? throw new InvalidOperationException($"FtD did not provide block {blockId} ({guid}).");
    }
}

internal sealed record ApsGeneratedPrefab(SavedSubObject Prefab, float MaterialCost);
