using System.Globalization;
using System.Numerics;
using System.Text.Json;
using ApsGenerator.Core;
using ApsGenerator.Core.Models;
using ApsGenerator.UI.Services.Export;

namespace ApsGenerator.UI.Tests;

public sealed class BlueprintExportTests
{
    [Fact]
    public void BuildJson_EmptyPlacements_EmitsValidEmptyBlueprint()
    {
        var grid = TemplateGenerator.Rectangle(width: 10, height: 10);

        string json = BlueprintExporter.BuildJson(
            Array.Empty<Placement>(),
            grid,
            TetrisType.FourClip,
            new ExportOptions("empty", TargetHeight: 2));

        BlueprintFile blueprint = ParseBlueprint(json);

        Assert.Single(blueprint.ItemDictionary);
        Assert.True(blueprint.ItemDictionary.ContainsKey("0"));
        Assert.Empty(blueprint.Blueprint.BLP);
        Assert.Equal("0,0,0", blueprint.Blueprint.MinCords);
        Assert.Equal("0,0,0", blueprint.Blueprint.MaxCords);
    }

    [Fact]
    public void BuildJson_SingleFourClipHeight2_EmitsTopAndBottomLayersAndExpectedItems()
    {
        var grid = TemplateGenerator.Rectangle(width: 10, height: 10);
        var placements = new List<Placement> { new(Row: 1, Col: 1, ShapeIndex: 0) };

        string json = BlueprintExporter.BuildJson(
            placements,
            grid,
            TetrisType.FourClip,
            new ExportOptions("single-four-clip-h2", TargetHeight: 2));

        BlueprintFile blueprint = ParseBlueprint(json);

        Assert.Equal(9, blueprint.Blueprint.BLP.Count);
        Assert.Equal(9, blueprint.Blueprint.BlockIds.Count);
        Assert.Equal(5, blueprint.ItemDictionary.Count);

        int topLayerCount = 0;
        int bottomLayerCount = 0;
        for (int i = 0; i < blueprint.Blueprint.BLP.Count; i++)
        {
            (_, int relY, _) = ParseCoords(blueprint.Blueprint.BLP[i]);

            if (relY == 1)
                topLayerCount++;

            if (relY == 0)
                bottomLayerCount++;
        }

        Assert.Equal(5, topLayerCount);
        Assert.Equal(4, bottomLayerCount);
        Assert.Equal("0,0,0", blueprint.Blueprint.MinCords);
        Assert.Equal("3,3,3", blueprint.Blueprint.MaxCords);

        Assert.Equal(GameData.ItemGuids[0], blueprint.ItemDictionary["0"]);
        Assert.Equal(GameData.ItemGuids[GameData.Blocks["Ejector_1"].BlockId], blueprint.ItemDictionary["231"]);
        Assert.Equal(GameData.ItemGuids[GameData.Blocks["AmmoIntake_1"].BlockId], blueprint.ItemDictionary["364"]);
        Assert.Equal(GameData.ItemGuids[GameData.Blocks["Loader_2"].BlockId], blueprint.ItemDictionary["366"]);
        Assert.Equal(GameData.ItemGuids[GameData.Blocks["Clip_2"].BlockId], blueprint.ItemDictionary["420"]);
    }

    [Fact]
    public void BuildJson_SingleFourClipHeight1WithBottom_EmitsExpectedTopAndBottomBlocksAtCorrectLayers()
    {
        var grid = TemplateGenerator.Rectangle(width: 10, height: 10);
        var placements = new List<Placement> { new(Row: 1, Col: 1, ShapeIndex: 0) };

        string json = BlueprintExporter.BuildJson(
            placements,
            grid,
            TetrisType.FourClip,
            new ExportOptions("single-four-clip-h1-bottom", TargetHeight: 1));

        BlueprintFile blueprint = ParseBlueprint(json);

        Assert.Equal(9, blueprint.Blueprint.BlockIds.Count);

        var topLayerIds = new List<int>();
        var bottomLayerIds = new List<int>();

        for (int i = 0; i < blueprint.Blueprint.BLP.Count; i++)
        {
            (_, int relY, _) = ParseCoords(blueprint.Blueprint.BLP[i]);

            if (relY == 1)
                topLayerIds.Add(blueprint.Blueprint.BlockIds[i]);

            if (relY == 0)
                bottomLayerIds.Add(blueprint.Blueprint.BlockIds[i]);
        }

        Assert.Equal(5, topLayerIds.Count);
        Assert.Equal(4, bottomLayerIds.Count);

        Assert.Equal(1, topLayerIds.Count(id => id == GameData.Blocks["Loader_1"].BlockId));
        Assert.Equal(4, topLayerIds.Count(id => id == GameData.Blocks["Clip_1"].BlockId));
        Assert.Equal(1, bottomLayerIds.Count(id => id == GameData.Blocks["Ejector_1"].BlockId));
        Assert.Equal(3, bottomLayerIds.Count(id => id == GameData.Blocks["AmmoIntake_1"].BlockId));
        Assert.Equal("0,0,0", blueprint.Blueprint.MinCords);
        Assert.Equal("3,2,3", blueprint.Blueprint.MaxCords);
    }

    [Fact]
    public void BuildJson_SingleThreeClipWithBottom_EjectorRotationFacesOppositeOfLoaderDirection()
    {
        var grid = TemplateGenerator.Rectangle(width: 10, height: 10);
        var placements = new List<Placement> { new(Row: 1, Col: 1, ShapeIndex: 0) };

        string json = BlueprintExporter.BuildJson(
            placements,
            grid,
            TetrisType.ThreeClip,
            new ExportOptions("single-three-clip-h1-bottom", TargetHeight: 1));

        BlueprintFile blueprint = ParseBlueprint(json);

        int ejectorId = GameData.Blocks["Ejector_1"].BlockId;
        var shape = ClusterShape.GetShapes(TetrisType.ThreeClip)[placements[0].ShapeIndex];
        CellOffset loaderOffset = shape.Offsets.Single(offset => offset.Role == CellRole.Loader);
        var occupiedDirections = new HashSet<Vector3>();

        foreach (CellOffset offset in shape.Offsets)
        {
            if (offset.Role != CellRole.Clip)
                continue;

            int deltaRow = offset.DeltaRow - loaderOffset.DeltaRow;
            int deltaCol = offset.DeltaCol - loaderOffset.DeltaCol;
            occupiedDirections.Add(new Vector3(deltaCol, 0, deltaRow));
        }

        Vector3[] allDirections =
        [
            new(0, 0, -1),
            new(1, 0, 0),
            new(0, 0, 1),
            new(-1, 0, 0)
        ];

        Vector3 loaderDirection = allDirections.Single(direction => !occupiedDirections.Contains(direction));
        int expectedRotation = BlockRotation.FindRotation(Vector3.UnitZ, loaderDirection, Vector3.UnitY, -Vector3.UnitY);

        int ejectorIndex = blueprint.Blueprint.BlockIds.FindIndex(blockId => blockId == ejectorId);

        Assert.NotEqual(-1, ejectorIndex);
        Assert.Equal(expectedRotation, blueprint.Blueprint.BLR[ejectorIndex]);
    }

    [Fact]
    public void BuildJson_SingleThreeClipLeftOpenWithBottom_EjectorRotationMatchesLeftFacingMiddle()
    {
        var grid = TemplateGenerator.Rectangle(width: 10, height: 10);
        var placements = new List<Placement> { new(Row: 1, Col: 1, ShapeIndex: 1) };

        string json = BlueprintExporter.BuildJson(
            placements,
            grid,
            TetrisType.ThreeClip,
            new ExportOptions("single-three-clip-left-bottom", TargetHeight: 1));

        BlueprintFile blueprint = ParseBlueprint(json);

        int ammoIntakeId = GameData.Blocks["AmmoIntake_1"].BlockId;
        Assert.Equal(7, blueprint.Blueprint.BlockIds.Count);
        Assert.Equal(2, blueprint.Blueprint.BlockIds.Count(blockId => blockId == ammoIntakeId));

        int ejectorId = GameData.Blocks["Ejector_1"].BlockId;
        int expectedRotation = BlockRotation.FindRotation(Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY);
        int oppositeRotation = BlockRotation.FindRotation(Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY);
        int ejectorIndex = blueprint.Blueprint.BlockIds.FindIndex(blockId => blockId == ejectorId);

        Assert.NotEqual(-1, ejectorIndex);
        Assert.Equal(expectedRotation, blueprint.Blueprint.BLR[ejectorIndex]);
        Assert.NotEqual(oppositeRotation, blueprint.Blueprint.BLR[ejectorIndex]);
    }

    [Fact]
    public void BuildJson_SingleThreeClipHeight2_PopulatesSavedAndContainedMaterialCost()
    {
        var grid = TemplateGenerator.Rectangle(width: 7, height: 7);
        var placements = new List<Placement> { new(Row: 2, Col: 2, ShapeIndex: 0) };

        string json = BlueprintExporter.BuildJson(
            placements,
            grid,
            TetrisType.ThreeClip,
            new ExportOptions("single-three-clip-h2-cost", TargetHeight: 2));

        BlueprintFile blueprint = ParseBlueprint(json);

        var materialCostByBlockId = GameData.Blocks.Values
            .GroupBy(block => block.BlockId)
            .ToDictionary(group => group.Key, group => group.First().MaterialCost);

        int expectedMaterialCost = blueprint.Blueprint.BlockIds
            .Sum(blockId => materialCostByBlockId[blockId]);

        Assert.Equal((double)expectedMaterialCost, blueprint.SavedMaterialCost);
        Assert.Equal((double)expectedMaterialCost, blueprint.ContainedMaterialCost);
        Assert.Equal((double)expectedMaterialCost, blueprint.Blueprint.ContainedMaterialCost);
    }

    [Fact]
    public void BuildJson_TwoFiveClipPlacementsSharingConnector_EmitsTwentySevenBlocks()
    {
        var grid = TemplateGenerator.Rectangle(width: 7, height: 7);
        var placements = new List<Placement>
        {
            new(Row: 2, Col: 2, ShapeIndex: 0),
            new(Row: 2, Col: 4, ShapeIndex: 2),
        };

        string json = BlueprintExporter.BuildJson(
            placements,
            grid,
            TetrisType.FiveClip,
            new ExportOptions("shared-connector", TargetHeight: 3));

        BlueprintFile blueprint = ParseBlueprint(json);

        Assert.Equal(27, blueprint.Blueprint.BlockIds.Count);
    }

    [Fact]
    public void BuildJson_InvalidFiveClipHeight_ThrowsArgumentException()
    {
        var grid = TemplateGenerator.Rectangle(width: 7, height: 7);
        var placements = new List<Placement> { new(Row: 2, Col: 2, ShapeIndex: 0) };

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            BlueprintExporter.BuildJson(
                placements,
                grid,
                TetrisType.FiveClip,
                new ExportOptions("invalid-five", TargetHeight: 4)));

        Assert.Contains("5-clip target height must be a positive multiple of 3 (got 4).", ex.Message);
    }

    [Fact]
    public void BuildJson_BlockDataSegments_HavePatchedSortedIndices()
    {
        var grid = TemplateGenerator.Rectangle(width: 10, height: 10);
        var placements = new List<Placement> { new(Row: 1, Col: 1, ShapeIndex: 0) };

        string json = BlueprintExporter.BuildJson(
            placements,
            grid,
            TetrisType.FourClip,
            new ExportOptions("block-data-index", TargetHeight: 1));

        BlueprintFile blueprint = ParseBlueprint(json);
        Assert.NotEmpty(blueprint.Blueprint.BlockData);

        byte[] combinedBlockData = Convert.FromBase64String(blueprint.Blueprint.BlockData);
        var blockById = GameData.Blocks.Values
            .GroupBy(definition => definition.BlockId)
            .ToDictionary(group => group.Key, group => group.First());
        int ammoIntakeBlockId = GameData.Blocks["AmmoIntake_1"].BlockId;

        int cursor = 0;

        for (int sortedIndex = 0; sortedIndex < blueprint.Blueprint.BlockIds.Count; sortedIndex++)
        {
            int blockId = blueprint.Blueprint.BlockIds[sortedIndex];
            int rotationCode = blueprint.Blueprint.BLR[sortedIndex];
            BlockDefinition definition = blockById[blockId];
            string rawBlockData = definition.DefaultBlockData;

            if (blockId == ammoIntakeBlockId)
            {
                Vector3 direction = BlockRotation.TransformDirection(rotationCode, Vector3.UnitZ);
                rawBlockData = GameData.GetAmmoIntakeBlockData(direction);
            }

            if (string.IsNullOrEmpty(rawBlockData))
                continue;

            byte[] segment = Convert.FromBase64String(rawBlockData);

            if (segment.Length < 3)
                continue;

            Assert.True(cursor + segment.Length <= combinedBlockData.Length);

            int patchedIndex =
                combinedBlockData[cursor]
                | (combinedBlockData[cursor + 1] << 8)
                | (combinedBlockData[cursor + 2] << 16);

            Assert.Equal(sortedIndex, patchedIndex);
            cursor += segment.Length;
        }

        Assert.Equal(cursor, combinedBlockData.Length);
    }

    private static BlueprintFile ParseBlueprint(string json)
    {
        BlueprintFile? blueprint = JsonSerializer.Deserialize<BlueprintFile>(json);
        Assert.NotNull(blueprint);
        return blueprint;
    }

    private static (int X, int Y, int Z) ParseCoords(string value)
    {
        string[] parts = value.Split(',');
        Assert.Equal(3, parts.Length);

        return (
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(parts[2], CultureInfo.InvariantCulture));
    }
}
