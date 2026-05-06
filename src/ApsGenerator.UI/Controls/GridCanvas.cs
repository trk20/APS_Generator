using ApsGenerator.Core.Models;
using ApsGenerator.Solver;
using ApsGenerator.UI.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GridModel = ApsGenerator.Core.Models.Grid;

namespace ApsGenerator.UI.Controls;

public sealed class GridCanvas : Control
{
    private const double CanvasRotationDegrees = 180;
    private const double DefaultZoomScale = 1.0;
    private const double MinZoomScale = 0.25;
    private const double MaxZoomScale = 8.0;
    private const double WheelZoomFactor = 1.15;
    private static readonly Color SymmetryAxisColor = Color.Parse("#8000E5FF");
    private static readonly Color RotationCenterColor = Color.Parse("#C0E53935");

    public static readonly StyledProperty<GridModel?> GridProperty =
        AvaloniaProperty.Register<GridCanvas, GridModel?>(nameof(Grid));

    public static readonly StyledProperty<SolverResult?> SolverResultProperty =
        AvaloniaProperty.Register<GridCanvas, SolverResult?>(nameof(SolverResult));

    public static readonly StyledProperty<TetrisType> TetrisTypeProperty =
        AvaloniaProperty.Register<GridCanvas, TetrisType>(nameof(TetrisType), TetrisType.FourClip);

    public static readonly StyledProperty<SymmetryType> SymmetryTypeProperty =
        AvaloniaProperty.Register<GridCanvas, SymmetryType>(nameof(SymmetryType), SymmetryType.None);

    public static readonly StyledProperty<PaintMode> PaintModeProperty =
        AvaloniaProperty.Register<GridCanvas, PaintMode>(nameof(PaintMode), PaintMode.Block);

    private static readonly SolidColorBrush BlockedBrush = new(Color.Parse("#000000"));
    private static readonly SolidColorBrush AvailableBrush = new(Color.Parse("#FFFFFF"));
    private static readonly Pen GridLinePen = new(new SolidColorBrush(Color.Parse("#9E9E9E")), 0.5);
    private static readonly SolidColorBrush SymmetryAxisBrush = new(SymmetryAxisColor);
    private static readonly Pen SymmetryAxisPen = new(
        SymmetryAxisBrush,
        2,
        new DashStyle(new double[] { 6, 4 }, 0));
    private static readonly SolidColorBrush RotationCenterBrush = new(RotationCenterColor);
    private static readonly Pen RotationCenterPen = new(RotationCenterBrush, 2);
    private static readonly Bitmap LoaderIcon = LoadBitmap("loader.png");
    private static readonly Bitmap ClipIcon = LoadBitmap("clip.png");
    private static readonly Bitmap ConnectorIcon = LoadBitmap("connector.png");

    private static readonly Color[] ClusterPalette =
    [
        Color.Parse("#E53935"),
        Color.Parse("#1E88E5"),
        Color.Parse("#43A047"),
        Color.Parse("#FB8C00"),
        Color.Parse("#8E24AA"),
        Color.Parse("#00ACC1"),
        Color.Parse("#F4511E"),
        Color.Parse("#3949AB"),
        Color.Parse("#7CB342"),
        Color.Parse("#C0CA33")
    ];

    static GridCanvas()
    {
        GridProperty.Changed.AddClassHandler<GridCanvas>((canvas, _) => canvas.HandleGridChanged());
        SolverResultProperty.Changed.AddClassHandler<GridCanvas>((canvas, _) => canvas.InvalidateVisual());
        TetrisTypeProperty.Changed.AddClassHandler<GridCanvas>((canvas, _) => canvas.InvalidateVisual());
        SymmetryTypeProperty.Changed.AddClassHandler<GridCanvas>((canvas, _) => canvas.InvalidateVisual());
    }

    public GridModel? Grid
    {
        get => GetValue(GridProperty);
        set => SetValue(GridProperty, value);
    }

    public SolverResult? SolverResult
    {
        get => GetValue(SolverResultProperty);
        set => SetValue(SolverResultProperty, value);
    }

    public TetrisType TetrisType
    {
        get => GetValue(TetrisTypeProperty);
        set => SetValue(TetrisTypeProperty, value);
    }

    public SymmetryType SymmetryType
    {
        get => GetValue(SymmetryTypeProperty);
        set => SetValue(SymmetryTypeProperty, value);
    }

    public PaintMode PaintMode
    {
        get => GetValue(PaintModeProperty);
        set => SetValue(PaintModeProperty, value);
    }

    private bool isPainting;
    private bool isPanning;
    private (int Row, int Col) lastPaintedCell = (-1, -1);
    private Point lastPanPosition;
    private Vector viewOffset;
    private double zoomScale = DefaultZoomScale;
    private int previousGridWidth = -1;
    private int previousGridHeight = -1;
    private readonly Dictionary<Color, IBrush> brushCache = new();

    public void NotifyGridChanged()
    {
        HandleGridChanged();
    }

    public event Action<int, int>? CellClicked;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var grid = Grid;
        if (grid is null)
            return;

        UpdateViewForGridDimensions(grid);

        if (!TryGetGridLayout(grid, out var cellSize, out var originX, out var originY, out var renderWidth, out var renderHeight))
            return;

        DrawBaseGrid(context, grid, cellSize, originX, originY);

        if (SolverResult is { } solverResult)
            DrawSolutionOverlay(context, grid, solverResult, cellSize, originX, originY);

        DrawGridLines(context, grid, cellSize, originX, originY, renderWidth, renderHeight);
        DrawSymmetryIndicators(context, cellSize, originX, originY, renderWidth, renderHeight);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            ResetViewToFit();
            e.Handled = true;
            return;
        }

        if (point.Properties.IsMiddleButtonPressed)
        {
            if (isPainting)
            {
                e.Handled = true;
                return;
            }

            isPanning = true;
            lastPanPosition = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
            return;

        if (isPanning)
        {
            e.Handled = true;
            return;
        }

        var grid = Grid;
        if (grid is null)
            return;

        if (!TryGetCellAtPosition(grid, e.GetPosition(this), out var row, out var col))
            return;

        isPainting = true;
        lastPaintedCell = (row, col);
        CellClicked?.Invoke(row, col);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (isPanning)
        {
            var position = e.GetPosition(this);
            var deltaX = position.X - lastPanPosition.X;
            var deltaY = position.Y - lastPanPosition.Y;
            if (Math.Abs(deltaX) > 0 || Math.Abs(deltaY) > 0)
            {
                viewOffset = new Vector(viewOffset.X + deltaX, viewOffset.Y + deltaY);
                lastPanPosition = position;
                InvalidateVisual();
            }

            e.Handled = true;
            return;
        }

        if (!isPainting)
            return;

        if (PaintMode == PaintMode.Toggle)
            return;

        var grid = Grid;
        if (grid is null)
            return;

        if (!TryGetCellAtPosition(grid, e.GetPosition(this), out var row, out var col))
            return;

        if (lastPaintedCell == (row, col))
            return;

        lastPaintedCell = (row, col);
        CellClicked?.Invoke(row, col);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var grid = Grid;
        if (grid is null)
            return;

        if (!TryGetFitGridLayout(grid, out var fitCellSize, out var fitOriginX, out var fitOriginY, out _, out _))
            return;

        var zoomStep = Math.Pow(WheelZoomFactor, e.Delta.Y);
        var nextZoomScale = Math.Clamp(zoomScale * zoomStep, MinZoomScale, MaxZoomScale);
        if (Math.Abs(nextZoomScale - zoomScale) < 0.0001)
            return;

        var pointerPosition = e.GetPosition(this);
        var currentCellSize = fitCellSize * zoomScale;
        if (currentCellSize <= 0)
            return;

        var currentOriginX = fitOriginX + viewOffset.X;
        var currentOriginY = fitOriginY + viewOffset.Y;

        var relativeX = (pointerPosition.X - currentOriginX) / currentCellSize;
        var relativeY = (pointerPosition.Y - currentOriginY) / currentCellSize;

        var nextCellSize = fitCellSize * nextZoomScale;
        var nextOriginX = pointerPosition.X - (relativeX * nextCellSize);
        var nextOriginY = pointerPosition.Y - (relativeY * nextCellSize);

        zoomScale = nextZoomScale;
        viewOffset = new Vector(nextOriginX - fitOriginX, nextOriginY - fitOriginY);

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        var changedState = false;

        if (e.InitialPressMouseButton == MouseButton.Middle && isPanning)
        {
            isPanning = false;
            changedState = true;
            e.Handled = true;
        }

        if (e.InitialPressMouseButton == MouseButton.Left && isPainting)
        {
            isPainting = false;
            lastPaintedCell = (-1, -1);
            changedState = true;
            e.Handled = true;
        }

        if (changedState && !isPanning && !isPainting)
            e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        isPainting = false;
        isPanning = false;
        lastPaintedCell = (-1, -1);
    }

    private bool TryGetCellAtPosition(GridModel grid, Point position, out int row, out int col)
    {
        row = 0;
        col = 0;

        if (!TryGetGridLayout(grid, out var cellSize, out var originX, out var originY, out var renderWidth, out var renderHeight))
            return false;

        if (position.X < originX || position.Y < originY
            || position.X >= originX + renderWidth || position.Y >= originY + renderHeight)
            return false;

        col = ScreenToCol(position.X, grid.Width, cellSize, originX);
        row = ScreenToRow(position.Y, grid.Height, cellSize, originY);

        return grid.IsInBounds(row, col);
    }

    private bool TryGetGridLayout(
        GridModel grid,
        out double cellSize,
        out double originX,
        out double originY,
        out double renderWidth,
        out double renderHeight)
    {
        if (!TryGetFitGridLayout(grid, out var fitCellSize, out var fitOriginX, out var fitOriginY, out var fitRenderWidth, out var fitRenderHeight))
        {
            cellSize = 0;
            originX = 0;
            originY = 0;
            renderWidth = 0;
            renderHeight = 0;
            return false;
        }

        cellSize = fitCellSize * zoomScale;
        renderWidth = fitRenderWidth * zoomScale;
        renderHeight = fitRenderHeight * zoomScale;
        originX = fitOriginX + viewOffset.X;
        originY = fitOriginY + viewOffset.Y;
        return true;
    }

    private bool TryGetFitGridLayout(
        GridModel grid,
        out double cellSize,
        out double originX,
        out double originY,
        out double renderWidth,
        out double renderHeight)
    {
        cellSize = 0;
        originX = 0;
        originY = 0;
        renderWidth = 0;
        renderHeight = 0;

        if (grid.Width <= 0 || grid.Height <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            return false;

        cellSize = Math.Min(Bounds.Width / grid.Width, Bounds.Height / grid.Height);
        if (double.IsNaN(cellSize) || double.IsInfinity(cellSize) || cellSize <= 0)
            return false;

        renderWidth = cellSize * grid.Width;
        renderHeight = cellSize * grid.Height;
        originX = (Bounds.Width - renderWidth) / 2.0;
        originY = (Bounds.Height - renderHeight) / 2.0;
        return true;
    }

    private void HandleGridChanged()
    {
        var grid = Grid;
        if (grid is not null)
            UpdateViewForGridDimensions(grid);

        InvalidateVisual();
    }

    private void UpdateViewForGridDimensions(GridModel grid)
    {
        if (grid.Width == previousGridWidth && grid.Height == previousGridHeight)
            return;

        previousGridWidth = grid.Width;
        previousGridHeight = grid.Height;
        zoomScale = DefaultZoomScale;
        viewOffset = default;
    }

    private void ResetViewToFit()
    {
        zoomScale = DefaultZoomScale;
        viewOffset = default;
        InvalidateVisual();
    }

    private static void DrawBaseGrid(DrawingContext context, GridModel grid, double cellSize, double originX, double originY)
    {
        for (var row = 0; row < grid.Height; row++)
        {
            for (var col = 0; col < grid.Width; col++)
            {
                var rect = CellRect(row, col, grid.Width, grid.Height, cellSize, originX, originY);
                var brush = grid[row, col] == CellState.Blocked ? BlockedBrush : AvailableBrush;
                context.FillRectangle(brush, rect);
            }
        }
    }

    private static Bitmap LoadBitmap(string name)
    {
        var uri = new Uri($"avares://ApsGenerator.UI/Resources/{name}");
        using var stream = AssetLoader.Open(uri);
        return new Bitmap(stream);
    }

    private void DrawSolutionOverlay(
        DrawingContext context,
        GridModel grid,
        SolverResult solverResult,
        double cellSize,
        double originX,
        double originY)
    {
        var placements = solverResult.Placements;
        if (placements.Count == 0)
            return;

        var shapes = ClusterShape.GetShapes(TetrisType);
        var clusterCells = new List<List<(int Row, int Col, CellRole Role, int DeltaRow, int DeltaCol)>>(placements.Count);
        var ownersByCell = new Dictionary<(int Row, int Col), List<int>>();

        for (var clusterIndex = 0; clusterIndex < placements.Count; clusterIndex++)
        {
            var placement = placements[clusterIndex];
            if (placement.ShapeIndex < 0 || placement.ShapeIndex >= shapes.Count)
            {
                clusterCells.Add([]);
                continue;
            }

            var shape = shapes[placement.ShapeIndex];
            var cells = new List<(int Row, int Col, CellRole Role, int DeltaRow, int DeltaCol)>(shape.Offsets.Count);

            foreach (var offset in shape.Offsets)
            {
                var row = placement.Row + offset.DeltaRow;
                var col = placement.Col + offset.DeltaCol;

                if (!grid.IsInBounds(row, col) || !grid.IsAvailable(row, col))
                    continue;

                cells.Add((row, col, offset.Role, offset.DeltaRow, offset.DeltaCol));

                var cellKey = (row, col);
                if (!ownersByCell.TryGetValue(cellKey, out var owners))
                {
                    owners = [];
                    ownersByCell[cellKey] = owners;
                }

                owners.Add(clusterIndex);
            }

            clusterCells.Add(cells);
        }

        var clusterColors = BuildClusterColorIndexes(placements.Count, ownersByCell);
        for (var clusterIndex = 0; clusterIndex < placements.Count; clusterIndex++)
        {
            var baseColor = ClusterPalette[clusterColors[clusterIndex]];
            var loaderBrush = GetOrCreateBrush(baseColor);
            var clipBrush = GetOrCreateBrush(BlendWithWhite(baseColor, 0.7));

            foreach (var (row, col, role, deltaRow, deltaCol) in clusterCells[clusterIndex])
            {
                var rect = CellRect(row, col, grid.Width, grid.Height, cellSize, originX, originY);

                switch (role)
                {
                    case CellRole.Loader:
                        context.FillRectangle(loaderBrush, rect);
                        DrawRotatedImage(context, LoaderIcon, rect, CanvasRotationDegrees);
                        break;
                    case CellRole.Clip:
                        context.FillRectangle(clipBrush, rect);
                        var angle = GetClipRotation(deltaRow, deltaCol) + CanvasRotationDegrees;
                        DrawRotatedImage(context, ClipIcon, rect, angle);
                        break;
                    case CellRole.Connection:
                        context.FillRectangle(clipBrush, rect);
                        DrawRotatedImage(context, ConnectorIcon, rect, CanvasRotationDegrees);
                        break;
                }
            }
        }
    }

    private static double GetClipRotation(int deltaRow, int deltaCol) =>
        (deltaRow, deltaCol) switch
        {
            (-1, 0) => 180,
            (1, 0) => 0,
            (0, -1) => 90,
            (0, 1) => -90,
            _ => 0
        };

    private static void DrawRotatedImage(DrawingContext context, Bitmap image, Rect rect, double angleDegrees)
    {
        if (angleDegrees == 0)
        {
            context.DrawImage(image, rect);
            return;
        }

        var center = new Point(rect.X + (rect.Width / 2.0), rect.Y + (rect.Height / 2.0));
        var transform = Matrix.CreateTranslation(-center.X, -center.Y)
            * Matrix.CreateRotation(Math.PI / 180.0 * angleDegrees)
            * Matrix.CreateTranslation(center.X, center.Y);

        using (context.PushTransform(transform))
        {
            context.DrawImage(image, rect);
        }
    }

    private static int[] BuildClusterColorIndexes(
        int clusterCount,
        IReadOnlyDictionary<(int Row, int Col), List<int>> ownersByCell)
    {
        var adjacency = new List<HashSet<int>>(clusterCount);
        for (var i = 0; i < clusterCount; i++)
            adjacency.Add([]);

        var deltas = new[]
        {
            (-1, 0),
            (1, 0),
            (0, -1),
            (0, 1)
        };

        foreach (var (cell, owners) in ownersByCell)
        {
            foreach (var (dr, dc) in deltas)
            {
                var neighbor = (cell.Row + dr, cell.Col + dc);
                if (!ownersByCell.TryGetValue(neighbor, out var neighborOwners))
                    continue;

                foreach (var owner in owners)
                {
                    foreach (var neighborOwner in neighborOwners)
                    {
                        if (owner == neighborOwner)
                            continue;

                        adjacency[owner].Add(neighborOwner);
                        adjacency[neighborOwner].Add(owner);
                    }
                }
            }
        }

        var orderedClusters = new List<int>(clusterCount);
        for (var i = 0; i < clusterCount; i++)
            orderedClusters.Add(i);

        orderedClusters.Sort((a, b) => adjacency[b].Count.CompareTo(adjacency[a].Count));

        var colorByCluster = new int[clusterCount];
        Array.Fill(colorByCluster, -1);

        foreach (var cluster in orderedClusters)
        {
            var usedColors = new bool[ClusterPalette.Length];

            foreach (var neighbor in adjacency[cluster])
            {
                var usedColor = colorByCluster[neighbor];
                if (usedColor >= 0 && usedColor < ClusterPalette.Length)
                    usedColors[usedColor] = true;
            }

            var selectedColor = cluster % ClusterPalette.Length;
            for (var i = 0; i < ClusterPalette.Length; i++)
            {
                if (!usedColors[i])
                {
                    selectedColor = i;
                    break;
                }
            }

            colorByCluster[cluster] = selectedColor;
        }

        return colorByCluster;
    }

    private static void DrawGridLines(
        DrawingContext context,
        GridModel grid,
        double cellSize,
        double originX,
        double originY,
        double renderWidth,
        double renderHeight)
    {
        for (var row = 0; row <= grid.Height; row++)
        {
            var y = originY + ((grid.Height - row) * cellSize);
            context.DrawLine(GridLinePen, new Point(originX, y), new Point(originX + renderWidth, y));
        }

        for (var col = 0; col <= grid.Width; col++)
        {
            var x = originX + ((grid.Width - col) * cellSize);
            context.DrawLine(GridLinePen, new Point(x, originY), new Point(x, originY + renderHeight));
        }
    }

    private void DrawSymmetryIndicators(
        DrawingContext context,
        double cellSize,
        double originX,
        double originY,
        double renderWidth,
        double renderHeight)
    {
        var symmetryType = SymmetryType;
        if (symmetryType == SymmetryType.None)
            return;

        var centerX = originX + (renderWidth / 2.0);
        var centerY = originY + (renderHeight / 2.0);

        switch (symmetryType)
        {
            case SymmetryType.HorizontalReflection:
                DrawHorizontalAxis(context, SymmetryAxisPen, originX, centerY, renderWidth);
                break;
            case SymmetryType.VerticalReflection:
                DrawVerticalAxis(context, SymmetryAxisPen, centerX, originY, renderHeight);
                break;
            case SymmetryType.BothReflection:
                DrawHorizontalAxis(context, SymmetryAxisPen, originX, centerY, renderWidth);
                DrawVerticalAxis(context, SymmetryAxisPen, centerX, originY, renderHeight);
                break;
            case SymmetryType.Rotation180:
                DrawRotationArrows(context, RotationCenterPen, centerX, centerY, cellSize, 2);
                break;
            case SymmetryType.Rotation90:
                DrawRotationArrows(context, RotationCenterPen, centerX, centerY, cellSize, 4);
                break;
        }
    }

    private static void DrawHorizontalAxis(DrawingContext context, Pen pen, double originX, double y, double width)
    {
        context.DrawLine(pen, new Point(originX, y), new Point(originX + width, y));
    }

    private static void DrawVerticalAxis(DrawingContext context, Pen pen, double x, double originY, double height)
    {
        context.DrawLine(pen, new Point(x, originY), new Point(x, originY + height));
    }

    private static void DrawRotationArrows(
        DrawingContext context,
        Pen pen,
        double centerX,
        double centerY,
        double cellSize,
        int arrowCount)
    {
        var radius = cellSize * 0.8;
        var arrowSize = cellSize * 0.2;
        var segmentAngle = 360.0 / arrowCount;

        for (var i = 0; i < arrowCount; i++)
        {
            var startAngle = (i * segmentAngle) + 10;
            var endAngle = startAngle + segmentAngle - 20;

            var startRad = startAngle * Math.PI / 180.0;
            var endRad = endAngle * Math.PI / 180.0;

            var geometry = new StreamGeometry();
            using (var gc = geometry.Open())
            {
                var startPoint = new Point(
                    centerX + (radius * Math.Cos(startRad)),
                    centerY + (radius * Math.Sin(startRad)));
                var endPoint = new Point(
                    centerX + (radius * Math.Cos(endRad)),
                    centerY + (radius * Math.Sin(endRad)));

                gc.BeginFigure(startPoint, false);
                gc.ArcTo(endPoint, new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            }

            context.DrawGeometry(null, pen, geometry);

            var arrowTip = new Point(
                centerX + (radius * Math.Cos(endRad)),
                centerY + (radius * Math.Sin(endRad)));

            var tangentAngle = endRad + (Math.PI / 2.0);
            var wing1Angle = tangentAngle + 2.6;
            var wing2Angle = tangentAngle + 3.7;

            var wing1 = new Point(
                arrowTip.X + (arrowSize * Math.Cos(wing1Angle)),
                arrowTip.Y + (arrowSize * Math.Sin(wing1Angle)));
            var wing2 = new Point(
                arrowTip.X + (arrowSize * Math.Cos(wing2Angle)),
                arrowTip.Y + (arrowSize * Math.Sin(wing2Angle)));

            context.DrawLine(pen, arrowTip, wing1);
            context.DrawLine(pen, arrowTip, wing2);
        }
    }

    private IBrush GetOrCreateBrush(Color color)
    {
        if (brushCache.TryGetValue(color, out var brush))
            return brush;

        brush = new SolidColorBrush(color);
        brushCache[color] = brush;
        return brush;
    }

    private static double CellScreenX(int col, int gridWidth, double cellSize, double originX) =>
        originX + ((gridWidth - 1 - col) * cellSize);

    private static double CellScreenY(int row, int gridHeight, double cellSize, double originY) =>
        originY + ((gridHeight - 1 - row) * cellSize);

    private static int ScreenToCol(double screenX, int gridWidth, double cellSize, double originX) =>
        gridWidth - 1 - (int)((screenX - originX) / cellSize);

    private static int ScreenToRow(double screenY, int gridHeight, double cellSize, double originY) =>
        gridHeight - 1 - (int)((screenY - originY) / cellSize);

    private static Rect CellRect(
        int row,
        int col,
        int gridWidth,
        int gridHeight,
        double cellSize,
        double originX,
        double originY) =>
        new(
            CellScreenX(col, gridWidth, cellSize, originX),
            CellScreenY(row, gridHeight, cellSize, originY),
            cellSize,
            cellSize);

    private static Color BlendWithWhite(Color color, double colorWeight)
    {
        var clamped = Math.Clamp(colorWeight, 0.0, 1.0);

        static byte BlendChannel(byte channel, double weight)
        {
            var value = (channel * weight) + ((1.0 - weight) * 255.0);
            return (byte)Math.Clamp(Math.Round(value), 0, 255);
        }

        return Color.FromRgb(
            BlendChannel(color.R, clamped),
            BlendChannel(color.G, clamped),
            BlendChannel(color.B, clamped));
    }
}