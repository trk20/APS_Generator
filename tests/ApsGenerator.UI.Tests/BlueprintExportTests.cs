using System.Globalization;
using System.Numerics;
using System.Text.Json;
using ApsGenerator.Core;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver.Cooler;
using ApsGenerator.UI.Services.Export;

namespace ApsGenerator.UI.Tests;

public sealed class BlueprintExportTests
{
    [Fact]
    public void BuildJson_EmptyPlacements_EmitsValidEmptyBlueprint()
    {
        var grid = TemplateGenerator.Rectangle(width: 10, height: 10);

        string json = BlueprintExporter.BuildJson(
            [],
            grid,
            TetrisType.FourClip,
            new ExportOptions("empty", TargetHeight: 2, ExportExtraLayers.EjectorsIntakes));

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
            new ExportOptions("single-four-clip-h2", TargetHeight: 2, ExportExtraLayers.EjectorsIntakes));

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
            new ExportOptions("single-four-clip-h1-bottom", TargetHeight: 1, ExportExtraLayers.EjectorsIntakes));

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
            new ExportOptions("single-three-clip-h1-bottom", TargetHeight: 1, ExportExtraLayers.EjectorsIntakes));

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
            new ExportOptions("single-three-clip-left-bottom", TargetHeight: 1, ExportExtraLayers.EjectorsIntakes));

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
            new ExportOptions("single-three-clip-h2-cost", TargetHeight: 2, ExportExtraLayers.EjectorsIntakes));

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
            new ExportOptions("shared-connector", TargetHeight: 3, ExportExtraLayers.EjectorsIntakes));

        BlueprintFile blueprint = ParseBlueprint(json);

        Assert.Equal(27, blueprint.Blueprint.BlockIds.Count);
    }

    [Fact]
    public void BuildJson_FiveClipWithTopSnake_EmitsCoolersAboveShafts()
    {
        var grid = TemplateGenerator.Rectangle(width: 7, height: 7);
        var placements = new List<Placement>
        {
            new(Row: 2, Col: 2, ShapeIndex: 0),
            new(Row: 2, Col: 4, ShapeIndex: 2),
        };

        // Shared connection at (2,3); Steiner is a single shaft-top cell with ConnectDown.
        var cooler = new CoolerSnakeResult
        {
            Status = CoolerSnakeStatus.Sat,
            Detail = "5-clip",
            RequiredIntakesPerCluster = 0,
            CoolerCells =
            [
                new CoolerCell(2, 3, 0, Layer: 0, IsGap: false, OpenFaces: CoolerFaceFlags.None, ConnectDown: true),
            ],
        };

        string json = BlueprintExporter.BuildJson(
            placements,
            grid,
            TetrisType.FiveClip,
            new ExportOptions("five-top-snake", TargetHeight: 3, ExtraLayers: ExportExtraLayers.EjectorsIntakesCoolerSnake, cooler));

        BlueprintFile blueprint = ParseBlueprint(json);

        Assert.Equal(28, blueprint.Blueprint.BlockIds.Count);

        var coolerVariantIds = new HashSet<int>
        {
            CoolerBlockProfile.Cooler4WayId,
            CoolerBlockProfile.Cooler5WayId,
            CoolerBlockProfile.CoolerCornerId,
            CoolerBlockProfile.CoolerSplitterId,
            GameData.Blocks["Cooler_1"].BlockId,
        };

        // World Y=3 top snake → relative Y = worldY - minY; five-clip minY is 0 → relY=3.
        int topSnakeCoolers = 0;
        for (int i = 0; i < blueprint.Blueprint.BlockIds.Count; i++)
        {
            if (!coolerVariantIds.Contains(blueprint.Blueprint.BlockIds[i]))
                continue;
            (_, int relY, _) = ParseCoords(blueprint.Blueprint.BLP[i]);
            if (relY == 3)
                topSnakeCoolers++;
        }

        Assert.Equal(1, topSnakeCoolers);
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
                new ExportOptions("invalid-five", TargetHeight: 4, ExportExtraLayers.EjectorsIntakesCoolerSnake)));

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
            new ExportOptions("block-data-index", TargetHeight: 1, ExportExtraLayers.EjectorsIntakes));

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

    [Fact]
    public void BuildJson_ThreeClipWithCoolerSnakes_EmitsCoolersOnTopDeckAndVerticalOrBottomEjector()
    {
        var grid = TemplateGenerator.Rectangle(width: 3, height: 3);
        var placements = new List<Placement> { new(Row: 1, Col: 1, ShapeIndex: 3) };
        var cooler = new CoolerSnakeSolver().Solve(
            grid,
            TetrisType.ThreeClip,
            placements,
            new CoolerSnakeOptions { MaxTimeSeconds = 10 });

        Assert.Equal(CoolerSnakeStatus.Sat, cooler.Status);

        string json = BlueprintExporter.BuildJson(
            placements,
            grid,
            TetrisType.ThreeClip,
            new ExportOptions("cooler-snake", TargetHeight: 1, ExtraLayers: ExportExtraLayers.EjectorsIntakesCoolerSnake, cooler));

        BlueprintFile blueprint = ParseBlueprint(json);
        var coolerIds = new HashSet<int>
        {
            CoolerBlockProfile.Cooler4WayId,
            CoolerBlockProfile.Cooler5WayId,
            CoolerBlockProfile.CoolerCornerId,
            CoolerBlockProfile.CoolerSplitterId,
            GameData.Blocks["Cooler_1"].BlockId,
        };

        Assert.Contains(blueprint.Blueprint.BlockIds, id => coolerIds.Contains(id));
        Assert.Contains(blueprint.Blueprint.BlockIds, id => id == GameData.Blocks["Ejector_1"].BlockId);
        Assert.True(blueprint.Blueprint.BlockIds.Count(id => id == GameData.Blocks["AmmoIntake_1"].BlockId) >= 4);

        int intakeId = GameData.Blocks["AmmoIntake_1"].BlockId;
        int ejectorId = GameData.Blocks["Ejector_1"].BlockId;
        int expectedTopIntakeBlr = BlockRotation.FindRotation(
            Vector3.UnitZ, -Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ);
        int expectedBottomIntakeBlr = BlockRotation.FindRotation(
            Vector3.UnitZ, Vector3.UnitY, Vector3.UnitY, -Vector3.UnitZ);

        for (int i = 0; i < blueprint.Blueprint.BlockIds.Count; i++)
        {
            if (blueprint.Blueprint.BlockIds[i] != intakeId)
                continue;

            (_, int relY, _) = ParseCoords(blueprint.Blueprint.BLP[i]);
            // Relative Y: bottom intakes at 0 (world -1), top deck at targetHeight+1 after minY shift.
            if (relY >= 2)
                Assert.Equal(expectedTopIntakeBlr, blueprint.Blueprint.BLR[i]);
            else if (relY == 0)
                Assert.Equal(expectedBottomIntakeBlr, blueprint.Blueprint.BLR[i]);
        }

        foreach (var ejector in cooler.EjectorDirs.Where(e => e.Kind == EjectorKind.Bottom))
        {
            int expectedEjectorBlr = BlockRotation.FindRotation(
                Vector3.UnitZ,
                new Vector3(-ejector.DCol, 0, -ejector.DRow),
                Vector3.UnitY,
                -Vector3.UnitY);

            bool found = false;
            for (int i = 0; i < blueprint.Blueprint.BlockIds.Count; i++)
            {
                if (blueprint.Blueprint.BlockIds[i] == ejectorId
                    && blueprint.Blueprint.BLR[i] == expectedEjectorBlr)
                {
                    found = true;
                    break;
                }
            }

            Assert.True(found, $"Bottom ejector BLR {expectedEjectorBlr} not found for protrusion Δ=({ejector.DRow},{ejector.DCol}).");
        }
    }

    [Fact]
    public void BuildJson_ThreeClipVerticalEjector_FacesDownOnOpenArm()
    {
        var grid = TemplateGenerator.Rectangle(7, 7);
        var placements = new List<Placement> { new(Row: 3, Col: 3, ShapeIndex: 0) };
        var cooler = new CoolerSnakeSolver().Solve(
            grid, TetrisType.ThreeClip, placements,
            new CoolerSnakeOptions { MaxTimeSeconds = 10 });

        Assert.Equal(CoolerSnakeStatus.Sat, cooler.Status);
        Assert.Contains(cooler.EjectorDirs, e => e.Kind == EjectorKind.VerticalOpenArmDown);

        var vertical = cooler.EjectorDirs.First(e => e.Kind == EjectorKind.VerticalOpenArmDown);
        string json = BlueprintExporter.BuildJson(
            placements, grid, TetrisType.ThreeClip,
            new ExportOptions("cooler-vertical", TargetHeight: 2, ExtraLayers: ExportExtraLayers.EjectorsIntakesCoolerSnake, cooler));
        BlueprintFile blueprint = ParseBlueprint(json);

        int ejectorId = GameData.Blocks["Ejector_1"].BlockId;
        var awayFromLoader = new Vector3(vertical.DCol, 0, vertical.DRow);
        int expectedBlr = BlockRotation.FindRotation(
            Vector3.UnitZ, Vector3.UnitY, Vector3.UnitY, awayFromLoader);

        Assert.Contains(
            Enumerable.Range(0, blueprint.Blueprint.BlockIds.Count)
                .Where(i => blueprint.Blueprint.BlockIds[i] == ejectorId),
            i => blueprint.Blueprint.BLR[i] == expectedBlr);

        Assert.DoesNotContain(
            cooler.IntakeCells.Where(i => i.IsUnderneath),
            i => i.Row == vertical.ProtrudeRow && i.Col == vertical.ProtrudeCol);
    }

    [Fact]
    public void BuildJson_FirstLayerCoolersSitAtTargetHeight()
    {
        var grid = TemplateGenerator.Rectangle(width: 3, height: 3);
        var placements = new List<Placement> { new(Row: 1, Col: 1, ShapeIndex: 3) };
        var cooler = new CoolerSnakeSolver().Solve(
            grid, TetrisType.ThreeClip, placements,
            new CoolerSnakeOptions { MaxTimeSeconds = 10 });
        Assert.Equal(CoolerSnakeStatus.Sat, cooler.Status);

        string json = BlueprintExporter.BuildJson(
            placements, grid, TetrisType.ThreeClip,
            new ExportOptions("cooler-h2-deck", TargetHeight: 2, ExtraLayers: ExportExtraLayers.EjectorsIntakesCoolerSnake, cooler));
        BlueprintFile blueprint = ParseBlueprint(json);

        var coolerIds = new HashSet<int>
        {
            CoolerBlockProfile.Cooler4WayId,
            CoolerBlockProfile.Cooler5WayId,
            CoolerBlockProfile.CoolerCornerId,
            CoolerBlockProfile.CoolerSplitterId,
            GameData.Blocks["Cooler_1"].BlockId,
        };

        foreach (int i in Enumerable.Range(0, blueprint.Blueprint.BlockIds.Count))
        {
            if (!coolerIds.Contains(blueprint.Blueprint.BlockIds[i]))
                continue;
            (_, int relY, _) = ParseCoords(blueprint.Blueprint.BLP[i]);
            // world Y=targetHeight(2), minY=-1 → relY=3
            Assert.Equal(3, relY);
        }
    }

    [Fact]
    public void BuildJson_LocalBridge_EmitsElevatedCoolerOverIntakeOnly()
    {
        var grid = TemplateGenerator.Rectangle(width: 3, height: 3);
        var placements = new List<Placement> { new(Row: 1, Col: 1, ShapeIndex: 3) };
        var cooler = new CoolerSnakeResult
        {
            Status = CoolerSnakeStatus.Sat,
            LayersUsed = 2,
            Detail = "elevated bridges",
            RequiredIntakesPerCluster = 4,
            IntakesPerCluster = [4],
            EjectorDirs =
            [
                new EjectorPlacement(0, EjectorKind.Bottom, 1, 1, 0, 1, 2),
            ],
            IntakeCells =
            [
                new IntakeCell(1, 1, 0, IsUnderneath: false),
                new IntakeCell(0, 1, 0, IsUnderneath: true),
                new IntakeCell(2, 1, 0, IsUnderneath: true),
                new IntakeCell(1, 0, 0, IsUnderneath: true),
            ],
            CoolerCells =
            [
                // Deck snake around the intake, plus one elevated bridge over the intake.
                new CoolerCell(0, 1, 0, Layer: 0, false, CoolerFaceFlags.East | CoolerFaceFlags.South, ConnectUp: true),
                new CoolerCell(0, 1, 0, Layer: 1, false, CoolerFaceFlags.East, ConnectDown: true),
                new CoolerCell(1, 1, 0, Layer: 1, false, CoolerFaceFlags.North | CoolerFaceFlags.South, ConnectDown: true),
                new CoolerCell(2, 1, 0, Layer: 1, false, CoolerFaceFlags.West, ConnectDown: true),
                new CoolerCell(2, 1, 0, Layer: 0, false, CoolerFaceFlags.North | CoolerFaceFlags.West),
            ],
        };

        string json = BlueprintExporter.BuildJson(
            placements, grid, TetrisType.ThreeClip,
            new ExportOptions("cooler-local-bridge", TargetHeight: 2, ExtraLayers: ExportExtraLayers.EjectorsIntakesCoolerSnake, cooler));
        BlueprintFile blueprint = ParseBlueprint(json);

        var coolerVariantIds = new HashSet<int>
        {
            CoolerBlockProfile.Cooler4WayId,
            CoolerBlockProfile.Cooler5WayId,
            CoolerBlockProfile.CoolerCornerId,
            CoolerBlockProfile.CoolerSplitterId,
            GameData.Blocks["Cooler_1"].BlockId,
        };
        int intakeId = GameData.Blocks["AmmoIntake_1"].BlockId;
        int deckCoolers = 0;
        int bridgeCoolers = 0;

        for (int i = 0; i < blueprint.Blueprint.BlockIds.Count; i++)
        {
            int id = blueprint.Blueprint.BlockIds[i];
            (_, int relY, _) = ParseCoords(blueprint.Blueprint.BLP[i]);
            // minY=-1 → deck world2→rel3, bridge world3→rel4
            if (coolerVariantIds.Contains(id) && relY == 3)
                deckCoolers++;
            if (coolerVariantIds.Contains(id) && relY == 4)
                bridgeCoolers++;
        }

        Assert.Equal(2, deckCoolers);
        Assert.Equal(3, bridgeCoolers);
        // Top intakes under bridges still face down into the APS (ammo), not up at the cooler.
        int expectedDownIntakeBlr = BlockRotation.FindRotation(
            Vector3.UnitZ, -Vector3.UnitY, Vector3.UnitY, Vector3.UnitZ);
        int downFacingTopIntakes = 0;
        for (int i = 0; i < blueprint.Blueprint.BlockIds.Count; i++)
        {
            if (blueprint.Blueprint.BlockIds[i] != intakeId)
                continue;
            (_, int relY, _) = ParseCoords(blueprint.Blueprint.BLP[i]);
            if (relY == 3 && blueprint.Blueprint.BLR[i] == expectedDownIntakeBlr)
                downFacingTopIntakes++;
        }

        Assert.Equal(1, downFacingTopIntakes);
    }

    [Fact]
    public void BuildJson_ThreeClipTetrisOnly_OmitsBottomHardwareAndCoolers()
    {
        var grid = TemplateGenerator.Rectangle(width: 10, height: 10);
        var placements = new List<Placement> { new(Row: 1, Col: 1, ShapeIndex: 0) };

        string json = BlueprintExporter.BuildJson(
            placements,
            grid,
            TetrisType.ThreeClip,
            new ExportOptions("tetris-only", TargetHeight: 1, ExportExtraLayers.TetrisOnly));

        BlueprintFile blueprint = ParseBlueprint(json);
        int ejectorId = GameData.Blocks["Ejector_1"].BlockId;
        int intakeId = GameData.Blocks["AmmoIntake_1"].BlockId;

        Assert.DoesNotContain(blueprint.Blueprint.BlockIds, id => id == ejectorId);
        Assert.DoesNotContain(blueprint.Blueprint.BlockIds, id => id == intakeId);
        Assert.Contains(blueprint.Blueprint.BlockIds, id => id == GameData.Blocks["Loader_1"].BlockId);
        Assert.Contains(blueprint.Blueprint.BlockIds, id => id == GameData.Blocks["Clip_1"].BlockId);
    }

    [Fact]
    public void BuildJson_ThreeClipIntakesOnly_EmitsBottomIntakesWithoutEjectors()
    {
        var grid = TemplateGenerator.Rectangle(width: 10, height: 10);
        var placements = new List<Placement> { new(Row: 1, Col: 1, ShapeIndex: 0) };

        string json = BlueprintExporter.BuildJson(
            placements,
            grid,
            TetrisType.ThreeClip,
            new ExportOptions("intakes-only", TargetHeight: 1, ExportExtraLayers.IntakesOnly));

        BlueprintFile blueprint = ParseBlueprint(json);
        int ejectorId = GameData.Blocks["Ejector_1"].BlockId;
        int intakeId = GameData.Blocks["AmmoIntake_1"].BlockId;

        Assert.DoesNotContain(blueprint.Blueprint.BlockIds, id => id == ejectorId);
        Assert.Equal(4, blueprint.Blueprint.BlockIds.Count(id => id == intakeId));
    }

    [Fact]
    public void BuildJson_ThreeClipIntakesCoolerSnake_EmitsBottomsAndCoolersWithoutTopIntakesOrEjectors()
    {
        var grid = TemplateGenerator.Rectangle(width: 3, height: 3);
        var placements = new List<Placement> { new(Row: 1, Col: 1, ShapeIndex: 3) };
        var cooler = new CoolerSnakeSolver().Solve(
            grid,
            TetrisType.ThreeClip,
            placements,
            new CoolerSnakeOptions { MaxTimeSeconds = 10, OmitEjectors = true });

        Assert.Equal(CoolerSnakeStatus.Sat, cooler.Status);
        Assert.Empty(cooler.EjectorDirs);
        Assert.DoesNotContain(cooler.IntakeCells, i => !i.IsUnderneath);

        const int targetHeight = 1;
        string json = BlueprintExporter.BuildJson(
            placements,
            grid,
            TetrisType.ThreeClip,
            new ExportOptions(
                "intakes-cooler",
                TargetHeight: targetHeight,
                ExportExtraLayers.IntakesCoolerSnake,
                cooler));

        BlueprintFile blueprint = ParseBlueprint(json);
        var coolerIds = new HashSet<int>
        {
            CoolerBlockProfile.Cooler4WayId,
            CoolerBlockProfile.Cooler5WayId,
            CoolerBlockProfile.CoolerCornerId,
            CoolerBlockProfile.CoolerSplitterId,
            GameData.Blocks["Cooler_1"].BlockId,
        };
        int ejectorId = GameData.Blocks["Ejector_1"].BlockId;
        int intakeId = GameData.Blocks["AmmoIntake_1"].BlockId;

        Assert.Contains(blueprint.Blueprint.BlockIds, id => coolerIds.Contains(id));
        Assert.DoesNotContain(blueprint.Blueprint.BlockIds, id => id == ejectorId);
        Assert.Equal(4, blueprint.Blueprint.BlockIds.Count(id => id == intakeId));

        for (int i = 0; i < blueprint.Blueprint.BlockIds.Count; i++)
        {
            if (blueprint.Blueprint.BlockIds[i] != intakeId)
                continue;

            (_, int relY, _) = ParseCoords(blueprint.Blueprint.BLP[i]);
            // Relative Y: bottom intakes at 0 after minY shift; top deck would be >= 2.
            Assert.Equal(0, relY);
        }
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
