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
    private static readonly int BottomAmmoIntakeBlr = BlockRotation.FindRotation(
        IntakePrimary, Vector3.UnitY, IntakeSecondary, -Vector3.UnitZ);
    private static readonly string BottomAmmoIntakeData = GameData.GetAmmoIntakeBlockData(Vector3.UnitY);

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
                EmitFiveClipExtraLayers(placements, grid, options, emittedBlocks);
            else
                EmitLoaderLengthExtraLayers(placements, grid, type, options, emittedBlocks);
        }

        return AssembleBlueprint(options.BlueprintName, options.TargetHeight, emittedBlocks);
    }

    private static void EmitFiveClipExtraLayers(
        IReadOnlyList<Placement> placements,
        Grid grid,
        ExportOptions options,
        Dictionary<(int X, int Y, int Z), EmittedBlock> emittedBlocks)
    {
        EmitFiveClipBlocks(placements, grid, options.TargetHeight, emittedBlocks);
        if (options.ExtraLayers != ExportExtraLayers.EjectorsIntakesCoolerSnake)
            return;

        if (options.CoolerSnakes is { Status: CoolerSnakeStatus.Sat } fiveCooler)
            ApplyCoolerDeckPlan(grid, options.TargetHeight, fiveCooler, emittedBlocks);
    }

    /// <summary>
    /// 3/4-clip path: variable-length loaders/clips, then mutually exclusive extra layers
    /// from <see cref="ExportExtraLayers"/>. Cooler modes fall back to bottom layer
    /// when no SAT cooler result is available.
    /// </summary>
    private static void EmitLoaderLengthExtraLayers(
        IReadOnlyList<Placement> placements,
        Grid grid,
        TetrisType type,
        ExportOptions options,
        Dictionary<(int X, int Y, int Z), EmittedBlock> emittedBlocks)
    {
        EmitLoaderLengthLoadersAndClips(placements, grid, type, options.TargetHeight, emittedBlocks);

        CoolerSnakeResult? cooler = options.CoolerSnakes is { Status: CoolerSnakeStatus.Sat } sat
            ? sat
            : null;

        switch (options.ExtraLayers)
        {
            case ExportExtraLayers.TetrisOnly:
                return;

            case ExportExtraLayers.EjectorsIntakesCoolerSnake:
                if (cooler is not null)
                    ApplyCoolerPlan(grid, options.TargetHeight, cooler, emittedBlocks);
                else
                    EmitBottomHardware(placements, grid, type, includeEjectors: true, emittedBlocks);
                return;

            case ExportExtraLayers.IntakesCoolerSnake:
                if (cooler is not null)
                {
                    EmitBottomAmmoIntakesUnderClusters(placements, grid, type, emittedBlocks);
                    ApplyCoolerDeckPlan(grid, options.TargetHeight, cooler, emittedBlocks);
                }
                else
                    EmitBottomHardware(placements, grid, type, includeEjectors: false, emittedBlocks);
                return;

            case ExportExtraLayers.EjectorsIntakes:
                EmitBottomHardware(placements, grid, type, includeEjectors: true, emittedBlocks);
                return;

            case ExportExtraLayers.IntakesOnly:
                EmitBottomHardware(placements, grid, type, includeEjectors: false, emittedBlocks);
                return;
        }
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

    private static void EmitLoaderLengthLoadersAndClips(
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
            EmitLoaderLengthLoaderAndClips(
                placement, shape, loaderOffset, loaderTarget, grid, targetHeight, emittedBlocks);
        }
    }

    private static void EmitBottomHardware(
        IReadOnlyList<Placement> placements,
        Grid grid,
        TetrisType type,
        bool includeEjectors,
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

            if (includeEjectors)
            {
                Vector3 ejectorTarget = type == TetrisType.ThreeClip
                    ? loaderTarget
                    : DefaultEjectorTarget;
                int ejectorBlr = BlockRotation.FindRotation(EjectorPrimary, ejectorTarget, EjectorSecondary, EjectorSecondaryTarget);
                EmitBlock(emittedBlocks, loaderX, -1, loaderZ, "Ejector_1", ejectorBlr);
                reservedIntakePositions.Add((loaderX - (int)ejectorTarget.X, loaderZ + (int)ejectorTarget.Z));
            }

            EmitClusterBottomIntakes(
                placement, shape, grid, emittedBlocks,
                reservedPositions: includeEjectors ? reservedIntakePositions : null,
                includeLoader: !includeEjectors);
        }
    }

    /// <summary>
    /// Emit bottom ammo intakes under loader and/or clip cells of one placement.
    /// When <paramref name="reservedPositions"/> is set, those XZ cells are skipped (ejector clearance).
    /// </summary>
    private static void EmitClusterBottomIntakes(
        Placement placement,
        ClusterShape shape,
        Grid grid,
        Dictionary<(int X, int Y, int Z), EmittedBlock> emittedBlocks,
        HashSet<(int X, int Z)>? reservedPositions,
        bool includeLoader)
    {
        foreach (CellOffset offset in shape.Offsets)
        {
            if (offset.Role == CellRole.Loader)
            {
                if (!includeLoader)
                    continue;
            }
            else if (offset.Role != CellRole.Clip)
            {
                continue;
            }

            int row = placement.Row + offset.DeltaRow;
            int col = placement.Col + offset.DeltaCol;
            (int gameX, int gameZ) = ToGameCoordinates(grid, row, col);
            if (reservedPositions is not null && reservedPositions.Contains((gameX, gameZ)))
                continue;

            EmitBottomAmmoIntake(emittedBlocks, gameX, gameZ);
        }
    }

    private static void ApplyCoolerPlan(
        Grid grid,
        int targetHeight,
        CoolerSnakeResult cooler,
        Dictionary<(int X, int Y, int Z), EmittedBlock> emittedBlocks)
    {
        int topY = targetHeight;

        foreach (var ejector in cooler.EjectorDirs)
            EmitCoolerEjector(grid, ejector, emittedBlocks);

        foreach (var intake in cooler.IntakeCells)
            EmitCoolerIntake(grid, intake, topY, emittedBlocks);

        ApplyCoolerDeckPlan(grid, topY, cooler, emittedBlocks);
    }

    private static void EmitCoolerIntake(
        Grid grid,
        IntakeCell intake,
        int topY,
        Dictionary<(int X, int Y, int Z), EmittedBlock> emittedBlocks)
    {
        (int x, int z) = ToGameCoordinates(grid, intake.Row, intake.Col);
        int y = intake.IsUnderneath ? -1 : topY;

        // Bottom intakes face up into the APS.
        // Top intakes always face down into the APS (ammo feed). Bridge coolers sit above
        // and open Down onto the intake body — intake facing does not aim at the cooler.
        Vector3 intakeFacing = intake.IsUnderneath ? Vector3.UnitY : -Vector3.UnitY;
        Vector3 intakeSecondaryTarget = intakeFacing.Y > 0 ? -Vector3.UnitZ : Vector3.UnitZ;
        int intakeBlr = BlockRotation.FindRotation(
            IntakePrimary, intakeFacing, IntakeSecondary, intakeSecondaryTarget);
        EmitBlock(
            emittedBlocks, x, y, z, "AmmoIntake_1", intakeBlr,
            GameData.GetAmmoIntakeBlockData(intakeFacing));
    }

    private static void EmitBottomAmmoIntake(
        Dictionary<(int X, int Y, int Z), EmittedBlock> emittedBlocks,
        int gameX,
        int gameZ) =>
        EmitBlock(emittedBlocks, gameX, -1, gameZ, "AmmoIntake_1", BottomAmmoIntakeBlr, BottomAmmoIntakeData);

    private static void EmitBottomAmmoIntakesUnderClusters(
        IReadOnlyList<Placement> placements,
        Grid grid,
        TetrisType type,
        Dictionary<(int X, int Y, int Z), EmittedBlock> emittedBlocks)
    {
        IReadOnlyList<ClusterShape> shapes = ClusterShape.GetShapes(type);

        foreach (Placement placement in placements)
        {
            ClusterShape shape = GetShape(shapes, placement, type);
            EmitClusterBottomIntakes(
                placement, shape, grid, emittedBlocks,
                reservedPositions: null,
                includeLoader: true);
        }
    }

    /// <summary>
    /// Emits horizontal cooler snake cells on the top deck (and bridge layer when Layer ≥ 1).
    /// </summary>
    private static void ApplyCoolerDeckPlan(
        Grid grid,
        int topY,
        CoolerSnakeResult cooler,
        Dictionary<(int X, int Y, int Z), EmittedBlock> emittedBlocks)
    {
        foreach (var cell in cooler.CoolerCells)
            EmitCoolerDeckCell(grid, cell, topY, emittedBlocks);
    }

    private static void EmitCoolerDeckCell(
        Grid grid,
        CoolerCell cell,
        int topY,
        Dictionary<(int X, int Y, int Z), EmittedBlock> emittedBlocks)
    {
        (int x, int z) = ToGameCoordinates(grid, cell.Row, cell.Col);
        var faces = CoolerBlockProfile.FacesFrom(cell.OpenFaces, cell.ConnectUp, cell.ConnectDown);
        bool onBridge = cell.Layer >= 1;
        int coolerY = onBridge ? topY + 1 : topY;

        (int blockId, int blr) = CoolerBlockProfile.SelectBlock(faces);
        string key = blockId switch
        {
            CoolerBlockProfile.Cooler4WayId => "Cooler_4Way",
            CoolerBlockProfile.Cooler5WayId => "Cooler_5Way",
            CoolerBlockProfile.CoolerCornerId => "Cooler_Corner",
            CoolerBlockProfile.CoolerSplitterId => "Cooler_Splitter",
            _ => "Cooler_1",
        };
        EmitBlock(emittedBlocks, x, coolerY, z, key, blr);
    }

    /// <summary>Emits loader + clips at Y=0 for one placement. Returns loader game XZ.</summary>
    private static (int LoaderX, int LoaderZ) EmitLoaderLengthLoaderAndClips(
        Placement placement,
        ClusterShape shape,
        CellOffset loaderOffset,
        Vector3 loaderTarget,
        Grid grid,
        int targetHeight,
        Dictionary<(int X, int Y, int Z), EmittedBlock> emittedBlocks)
    {
        int loaderRow = placement.Row + loaderOffset.DeltaRow;
        int loaderCol = placement.Col + loaderOffset.DeltaCol;
        (int loaderX, int loaderZ) = ToGameCoordinates(grid, loaderRow, loaderCol);

        string loaderKey = $"Loader_{targetHeight}";
        int loaderBlr = BlockRotation.FindRotation(LoaderPrimary, loaderTarget, LoaderSecondary, LoaderSecondaryTarget);
        EmitBlock(emittedBlocks, loaderX, 0, loaderZ, loaderKey, loaderBlr);

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
        }

        return (loaderX, loaderZ);
    }

    private static void EmitCoolerEjector(
        Grid grid,
        EjectorPlacement ejector,
        Dictionary<(int X, int Y, int Z), EmittedBlock> emittedBlocks)
    {
        if (ejector.Kind == EjectorKind.None)
            return;

        if (ejector.Kind == EjectorKind.VerticalOpenArmDown)
        {
            (int armX, int armZ) = ToGameCoordinates(grid, ejector.ProtrudeRow, ejector.ProtrudeCol);
            // Visual nozzle opposite local +Z → aim primary Up so shells go Down.
            // Local +Y (secondary) aims away from the loader so the connect face points at it.
            var awayFromLoader = new Vector3(ejector.DCol, 0, ejector.DRow);
            int blr = BlockRotation.FindRotation(
                EjectorPrimary, Vector3.UnitY, EjectorSecondary, awayFromLoader);
            EmitBlock(emittedBlocks, armX, 0, armZ, "Ejector_1", blr);
            return;
        }

        // Bottom ejector under the loader: same convention as classic APS export —
        // primary faces away from the cleared protrusion clip (for 3-clip that is the
        // open arm when protruding into the stem). Facing the protrusion points into a
        // neighboring bottom intake and fails to place in-game.
        (int loaderX, int loaderZ) = ToGameCoordinates(grid, ejector.LoaderRow, ejector.LoaderCol);
        var target = new Vector3(-ejector.DCol, 0, -ejector.DRow);
        int bottomBlr = BlockRotation.FindRotation(EjectorPrimary, target, EjectorSecondary, EjectorSecondaryTarget);
        EmitBlock(emittedBlocks, loaderX, -1, loaderZ, "Ejector_1", bottomBlr);
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

    private static ClusterShape GetShape(IReadOnlyList<ClusterShape> shapes, Placement placement, TetrisType type)
    {
        if ((uint)placement.ShapeIndex >= (uint)shapes.Count)
            throw new ArgumentException(
                $"Placement shape index {placement.ShapeIndex} is out of range for {type}.");

        return shapes[placement.ShapeIndex];
    }

    private static CellOffset GetLoaderOffset(ClusterShape shape) =>
        shape.Offsets.First(o => o.Role == CellRole.Loader);

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
        var (dRow, dCol) = ClusterOpenArm.Delta(shape);
        return new Vector3(dCol, 0, dRow);
    }

    private static Vector3 DetermineClipDirection(CellOffset fromOffset, CellOffset loaderOffset)
    {
        int deltaRow = loaderOffset.DeltaRow - fromOffset.DeltaRow;
        int deltaCol = loaderOffset.DeltaCol - fromOffset.DeltaCol;
        return new Vector3(deltaCol, 0, deltaRow);
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
