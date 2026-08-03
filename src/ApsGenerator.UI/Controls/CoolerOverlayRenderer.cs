using ApsGenerator.Core.Models;
using Avalonia;
using Avalonia.Media;
using GridModel = ApsGenerator.Core.Models.Grid;

namespace ApsGenerator.UI.Controls;

/// <summary>Procedural cooler-snake overlay drawing for <see cref="GridCanvas"/>.</summary>
internal sealed class CoolerOverlayRenderer
{
    private const double CoolerBodyFrac = 45.0 / 150.0;
    private const double CoolerCoreFrac = 13.0 / 150.0;
    private const double ShaftMarkerFrac = 0.22;
    private const int DeckLayer = 0;

    private static readonly SolidColorBrush BodyBrush = new(Color.Parse("#232324"));
    private static readonly SolidColorBrush CoreBrush = new(Color.Parse("#98999B"));
    private static readonly SolidColorBrush BridgeCoreBrush = new(Color.Parse("#4FC3F7"));
    private static readonly SolidColorBrush ShaftFillBrush = new(Color.Parse("#4FC3F7"));
    private static readonly Pen ShaftRingPen = new(new SolidColorBrush(Color.Parse("#0277BD")), 1.5);

    private CoolerSnakeResult? cachedOverlay;
    private LayerIndex? cachedIndex;
    private double cachedCellSize = -1;
    private double cachedBodyWidth;
    private double cachedCoreWidth;
    private double cachedShaftSize;

    public void Draw(
        DrawingContext context,
        GridModel grid,
        CoolerSnakeResult cooler,
        double cellSize,
        double originX,
        double originY,
        Func<int, int, int, int, double, double, double, Rect> cellRect)
    {
        if (cooler.Status != CoolerSnakeStatus.Sat)
            return;

        var index = GetLayerIndex(grid, cooler);
        if (index.DeckOpen.Count == 0 && index.BridgeOpen.Count == 0)
            return;

        EnsureGeometryCache(cellSize);

        DrawLayer(
            context, grid, index.DeckOpen, index.DeckCells,
            cellSize, originX, originY, cellRect,
            BodyBrush, CoreBrush);
        DrawShaftMarkers(context, grid, index.ShaftCells, cellSize, originX, originY, cellRect);
        DrawLayer(
            context, grid, index.BridgeOpen, index.BridgeCells,
            cellSize, originX, originY, cellRect,
            BodyBrush, BridgeCoreBrush);
    }

    private void EnsureGeometryCache(double cellSize)
    {
        if (cachedCellSize == cellSize)
            return;

        cachedCellSize = cellSize;
        cachedBodyWidth = Math.Max(2.0, cellSize * CoolerBodyFrac);
        cachedCoreWidth = Math.Max(1.0, cellSize * CoolerCoreFrac);
        cachedShaftSize = Math.Max(3.0, cellSize * ShaftMarkerFrac);
    }

    private void DrawLayer(
        DrawingContext context,
        GridModel grid,
        Dictionary<(int Row, int Col), CoolerFaceFlags> openByCell,
        HashSet<(int Row, int Col)> layerCells,
        double cellSize,
        double originX,
        double originY,
        Func<int, int, int, int, double, double, double, Rect> cellRect,
        IBrush bodyBrush,
        IBrush coreBrush)
    {
        foreach (var (row, col) in layerCells)
        {
            if (!openByCell.TryGetValue((row, col), out var faces))
                continue;

            var (North, East, South, West) = ResolveFaceLinks(row, col, faces, layerCells, openByCell);
            if (!(North || East || South || West))
                continue;

            var rect = cellRect(row, col, grid.Width, grid.Height, cellSize, originX, originY);
            DrawCoolerTile(
                context, rect,
                North, East, South, West,
                cachedBodyWidth, cachedCoreWidth, bodyBrush, coreBrush);
        }
    }

    private void DrawShaftMarkers(
        DrawingContext context,
        GridModel grid,
        HashSet<(int Row, int Col)> shaftCells,
        double cellSize,
        double originX,
        double originY,
        Func<int, int, int, int, double, double, double, Rect> cellRect)
    {
        double size = cachedShaftSize;
        double half = size * 0.5;

        foreach (var (row, col) in shaftCells)
        {
            var rect = cellRect(row, col, grid.Width, grid.Height, cellSize, originX, originY);
            double cx = rect.X + rect.Width * 0.5;
            double cy = rect.Y + rect.Height * 0.5;
            var marker = new Rect(cx - half, cy - half, size, size);
            context.FillRectangle(ShaftFillBrush, marker);
            context.DrawRectangle(ShaftRingPen, marker);
        }
    }

    private LayerIndex GetLayerIndex(GridModel grid, CoolerSnakeResult cooler)
    {
        if (ReferenceEquals(cachedOverlay, cooler) && cachedIndex is not null)
            return cachedIndex;

        var deckOpen = new Dictionary<(int Row, int Col), CoolerFaceFlags>();
        var bridgeOpen = new Dictionary<(int Row, int Col), CoolerFaceFlags>();
        var deckCells = new HashSet<(int Row, int Col)>();
        var bridgeCells = new HashSet<(int Row, int Col)>();
        var shaftCells = new HashSet<(int Row, int Col)>();

        foreach (var cell in cooler.CoolerCells)
        {
            if (!grid.IsInBounds(cell.Row, cell.Col))
                continue;

            var key = (cell.Row, cell.Col);
            if (cell.Layer > DeckLayer)
            {
                bridgeCells.Add(key);
                MergeFaces(bridgeOpen, key, cell.OpenFaces);
                continue;
            }

            deckCells.Add(key);
            MergeFaces(deckOpen, key, cell.OpenFaces);
            if (cell.ConnectUp)
                shaftCells.Add(key);
        }

        var index = new LayerIndex(deckOpen, bridgeOpen, deckCells, bridgeCells, shaftCells);
        cachedOverlay = cooler;
        cachedIndex = index;
        return index;
    }

    private static void MergeFaces(
        Dictionary<(int Row, int Col), CoolerFaceFlags> openByCell,
        (int Row, int Col) key,
        CoolerFaceFlags faces)
    {
        if (openByCell.TryGetValue(key, out var existing))
        {
            openByCell[key] = existing | faces;
            return;
        }

        openByCell[key] = faces;
    }

    private static (bool North, bool East, bool South, bool West) ResolveFaceLinks(
        int row,
        int col,
        CoolerFaceFlags faces,
        HashSet<(int Row, int Col)> coolerCells,
        Dictionary<(int Row, int Col), CoolerFaceFlags> openByCell)
    {
        var linked = CoolerFaceFlags.None;
        for (var d = 0; d < CoolerCardinals.Offsets.Length; d++)
        {
            if (!faces.Has(d))
                continue;
            var (dr, dc) = CoolerCardinals.Offset(d);
            int nr = row + dr, nc = col + dc;
            if (!coolerCells.Contains((nr, nc)))
                continue;
            if (!openByCell.TryGetValue((nr, nc), out var nb)
                || !nb.Has(CoolerCardinals.OppositeOf(d)))
                continue;

            linked |= CoolerCardinals.FlagFor(d);
        }

        return (
            (linked & CoolerFaceFlags.North) != 0,
            (linked & CoolerFaceFlags.East) != 0,
            (linked & CoolerFaceFlags.South) != 0,
            (linked & CoolerFaceFlags.West) != 0);
    }

    private static void DrawCoolerTile(
        DrawingContext context,
        Rect cell,
        bool n,
        bool e,
        bool s,
        bool w,
        double bodyW,
        double coreW,
        IBrush bodyBrush,
        IBrush coreBrush)
    {
        double cx = cell.X + cell.Width * 0.5;
        double cy = cell.Y + cell.Height * 0.5;
        double bh = bodyW * 0.5;
        double ch = coreW * 0.5;

        if (n || s)
        {
            double top = s ? cell.Y : cy - bh;
            double bottom = n ? cell.Bottom : cy + bh;
            context.FillRectangle(bodyBrush, new Rect(cx - bh, top, bodyW, bottom - top));
        }

        if (e || w)
        {
            double left = e ? cell.X : cx - bh;
            double right = w ? cell.Right : cx + bh;
            context.FillRectangle(bodyBrush, new Rect(left, cy - bh, right - left, bodyW));
        }

        if (n || s)
        {
            double top = s ? cell.Y : cy - ch;
            double bottom = n ? cell.Bottom : cy + ch;
            context.FillRectangle(coreBrush, new Rect(cx - ch, top, coreW, bottom - top));
        }

        if (e || w)
        {
            double left = e ? cell.X : cx - ch;
            double right = w ? cell.Right : cx + ch;
            context.FillRectangle(coreBrush, new Rect(left, cy - ch, right - left, coreW));
        }
    }

    private sealed record LayerIndex(
        Dictionary<(int Row, int Col), CoolerFaceFlags> DeckOpen,
        Dictionary<(int Row, int Col), CoolerFaceFlags> BridgeOpen,
        HashSet<(int Row, int Col)> DeckCells,
        HashSet<(int Row, int Col)> BridgeCells,
        HashSet<(int Row, int Col)> ShaftCells);
}
