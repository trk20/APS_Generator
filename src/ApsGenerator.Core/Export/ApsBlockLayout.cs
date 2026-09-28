using System.Collections.ObjectModel;
using System.Numerics;
using ApsGenerator.Core.Models;

namespace ApsGenerator.Core.Export;

public sealed record BlockLayoutOptions(int TargetHeight, ExportExtraLayers ExtraLayers, CoolerSnakeResult? Cooler = null);
public sealed record LayoutBlock(int BlockId, int RotationCode, string BlockData, int MaterialCost);

public sealed class ApsBlockLayout
{
    private readonly Dictionary<(int X, int Y, int Z), LayoutBlock> blocks = new();
    private readonly ApsLayoutGeometry geometry;
    private readonly BlockOrientation orientation;
    private readonly IReadOnlyList<ClusterShape> shapes;
    private readonly BlockLayoutOptions options;

    private ApsBlockLayout(Grid grid, TetrisType type, BlockLayoutOptions options, BlockOrientationConvention convention)
    {
        ValidateHeight(type, options.TargetHeight);
        geometry = new(grid.Width, grid.Height, options.TargetHeight, type, options.ExtraLayers);
        shapes = ClusterShape.GetShapes(type);
        orientation = new(convention);
        this.options = options;
    }

    public static IReadOnlyDictionary<(int X, int Y, int Z), LayoutBlock> Build(
        IReadOnlyList<Placement> placements, Grid grid, TetrisType type,
        BlockLayoutOptions options, BlockOrientationConvention convention)
    {
        if (placements is null) throw new ArgumentNullException(nameof(placements));
        if (grid is null) throw new ArgumentNullException(nameof(grid));
        if (options is null) throw new ArgumentNullException(nameof(options));
        var builder = new ApsBlockLayout(grid, type, options, convention);
        builder.Emit(placements);
        return new ReadOnlyDictionary<(int X, int Y, int Z), LayoutBlock>(builder.blocks);
    }

    private void Emit(IReadOnlyList<Placement> placements)
    {
        if (placements.Count == 0) return;
        foreach (Placement placement in placements)
        {
            ClusterShape shape = GetShape(placement);
            if (geometry.Type == TetrisType.FiveClip)
                EmitFiveClipStack(placement, shape);
            else
                EmitLoadersAndClips(placement, shape);
        }
        EmitExtraLayers(placements);
    }

    private void EmitExtraLayers(IReadOnlyList<Placement> placements)
    {
        if (options.ExtraLayers == ExportExtraLayers.TetrisOnly) return;
        CoolerSnakeResult? cooler = options.Cooler is { Status: CoolerSnakeStatus.Sat } sat ? sat : null;
        if (geometry.Type == TetrisType.FiveClip)
        {
            if (geometry.HasCoolerLayer && cooler is not null) EmitCoolerDeck(cooler);
            return;
        }
        bool includeEjectors = options.ExtraLayers is ExportExtraLayers.EjectorsIntakes or ExportExtraLayers.EjectorsIntakesCoolerSnake;
        if (geometry.HasCoolerLayer && cooler is not null)
        {
            if (includeEjectors)
                EmitSolvedHardware(cooler);
            else
                EmitBottomHardware(placements, includeEjectors: false);
            EmitCoolerDeck(cooler);
            return;
        }
        EmitBottomHardware(placements, includeEjectors);
    }

    private void EmitLoadersAndClips(Placement placement, ClusterShape shape)
    {
        CellOffset loader = LoaderOffset(shape);
        var position = Position(placement, loader);
        Vector3 facing = geometry.Type == TetrisType.ThreeClip ? OpenDirection(shape) : -Vector3.UnitZ;
        Add((position.X, 0, position.Z), $"Loader_{options.TargetHeight}", orientation.Loader(facing));
        foreach (CellOffset offset in shape.Offsets.Where(offset => offset.Role == CellRole.Clip))
        {
            position = Position(placement, offset);
            Add((position.X, 0, position.Z), $"Clip_{options.TargetHeight}",
                orientation.Clip(ClipDirection(offset, loader)), GameData.SharedClipBlockData);
        }
    }

    private void EmitBottomHardware(IReadOnlyList<Placement> placements, bool includeEjectors)
    {
        foreach (Placement placement in placements)
        {
            ClusterShape shape = GetShape(placement);
            var loaderPosition = Position(placement, LoaderOffset(shape));
            (int X, int Z)? reserved = null;
            if (includeEjectors)
            {
                Vector3 facing = geometry.Type == TetrisType.ThreeClip ? OpenDirection(shape) : Vector3.UnitZ;
                Add((loaderPosition.X, -1, loaderPosition.Z), "Ejector_1", orientation.Ejector(facing, -Vector3.UnitY));
                reserved = (loaderPosition.X - (int)facing.X, loaderPosition.Z + (int)facing.Z);
            }
            foreach (CellOffset offset in shape.Offsets)
            {
                if (offset.Role != CellRole.Clip && (includeEjectors || offset.Role != CellRole.Loader)) continue;
                var position = Position(placement, offset);
                if (reserved == position) continue;
                AddIntake((position.X, -1, position.Z), Vector3.UnitY);
            }
        }
    }

    private void EmitSolvedHardware(CoolerSnakeResult cooler)
    {
        foreach (EjectorPlacement ejector in cooler.EjectorDirs)
        {
            if (ejector.Kind == EjectorKind.None) continue;
            bool vertical = ejector.Kind == EjectorKind.VerticalOpenArmDown;
            var position = vertical
                ? geometry.Position(ejector.ProtrudeRow, ejector.ProtrudeCol)
                : geometry.Position(ejector.LoaderRow, ejector.LoaderCol);
            Vector3 direction = new(ejector.DCol, 0, ejector.DRow);
            Add((position.X, vertical ? 0 : -1, position.Z), "Ejector_1",
                orientation.Ejector(vertical ? Vector3.UnitY : -direction, vertical ? direction : -Vector3.UnitY));
        }
        foreach (IntakeCell intake in cooler.IntakeCells)
        {
            var position = geometry.Position(intake.Row, intake.Col);
            AddIntake((position.X, intake.IsUnderneath ? -1 : options.TargetHeight, position.Z),
                intake.IsUnderneath ? Vector3.UnitY : -Vector3.UnitY);
        }
    }

    private void EmitCoolerDeck(CoolerSnakeResult cooler)
    {
        foreach (CoolerCell cell in cooler.CoolerCells)
        {
            var position = geometry.Position(cell.Row, cell.Col);
            var (blockId, rotation) = orientation.Cooler(cell);
            string key = blockId switch
            {
                CoolerBlockProfile.Cooler4WayId => "Cooler_4Way",
                CoolerBlockProfile.Cooler5WayId => "Cooler_5Way",
                CoolerBlockProfile.CoolerCornerId => "Cooler_Corner",
                CoolerBlockProfile.CoolerSplitterId => "Cooler_Splitter",
                _ => throw new InvalidOperationException($"Unsupported cooler block ID {blockId}.")
            };
            Add((position.X, options.TargetHeight + (cell.Layer >= 1 ? 1 : 0), position.Z), key, rotation);
        }
    }

    private void EmitFiveClipStack(Placement placement, ClusterShape shape)
    {
        CellOffset loader = LoaderOffset(shape);
        foreach (CellOffset offset in shape.Offsets)
        {
            var position = Position(placement, offset);
            for (int y = 0; y < options.TargetHeight; y += ApsLayoutGeometry.FiveClipSectionHeight)
                EmitFiveClipSection((position.X, y, position.Z), offset, loader);
        }
    }

    private void EmitFiveClipSection((int X, int Y, int Z) position, CellOffset offset, CellOffset loader)
    {
        var (x, y, z) = position;
        if (offset.Role == CellRole.Loader)
        {
            Add(position, "Clip_1", orientation.VerticalClip(Vector3.UnitY), GameData.SharedClipBlockData);
            Add((x, y + 1, z), "Loader_1", orientation.Loader(Vector3.UnitZ));
            Add((x, y + 2, z), "Clip_1", orientation.VerticalClip(-Vector3.UnitY), GameData.SharedClipBlockData);
            return;
        }
        if (offset.Role == CellRole.Clip)
        {
            AddIntake(position, Vector3.UnitY, stacked: true);
            Add((x, y + 1, z), "Clip_1", orientation.Clip(ClipDirection(offset, loader)), GameData.SharedClipBlockData);
            AddIntake((x, y + 2, z), -Vector3.UnitY, stacked: true);
            return;
        }
        int rotation = BlockRotation.FindRotation(Vector3.UnitZ, Vector3.UnitY);
        for (int layer = 0; layer < ApsLayoutGeometry.FiveClipSectionHeight; layer++)
            Add((x, y + layer, z), "Cooler_1", rotation);
    }

    private void AddIntake((int X, int Y, int Z) position, Vector3 facing, bool stacked = false) =>
        Add(position, "AmmoIntake_1", orientation.Intake(facing, stacked), GameData.GetAmmoIntakeBlockData(facing));

    private void Add((int X, int Y, int Z) position, string key, int rotation, string data = "")
    {
        BlockDefinition definition = GameData.Blocks[key];
        blocks[position] = new LayoutBlock(definition.BlockId, rotation, data, definition.MaterialCost);
    }

    private ClusterShape GetShape(Placement placement) => (uint)placement.ShapeIndex < (uint)shapes.Count
        ? shapes[placement.ShapeIndex]
        : throw new ArgumentException($"Invalid shape index {placement.ShapeIndex} for {geometry.Type}.");

    private (int X, int Z) Position(Placement placement, CellOffset offset) =>
        geometry.Position(placement.Row + offset.DeltaRow, placement.Col + offset.DeltaCol);

    private static CellOffset LoaderOffset(ClusterShape shape) => shape.Offsets.First(offset => offset.Role == CellRole.Loader);
    private static Vector3 ClipDirection(CellOffset clip, CellOffset loader) => new(loader.DeltaCol - clip.DeltaCol, 0, loader.DeltaRow - clip.DeltaRow);
    private static Vector3 OpenDirection(ClusterShape shape)
    {
        var (row, column) = ClusterOpenArm.Delta(shape);
        return new Vector3(column, 0, row);
    }

    private static void ValidateHeight(TetrisType type, int height)
    {
        if (type == TetrisType.FiveClip)
        {
            if (height < ApsLayoutGeometry.FiveClipSectionHeight || height % ApsLayoutGeometry.FiveClipSectionHeight != 0)
                throw new ArgumentException($"5-clip target height must be a positive multiple of 3 (got {height}).");
            return;
        }
        if (height < 1 || height > 8)
            throw new ArgumentException($"Target height must be between 1 and 8 for 3-clip and 4-clip exports (got {height}).");
    }
}
