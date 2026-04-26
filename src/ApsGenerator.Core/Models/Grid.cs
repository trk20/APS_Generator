namespace ApsGenerator.Core.Models;

public sealed class Grid(int width, int height)
{
    public CellState[,] Cells { get; } = new CellState[height, width];
    public int Width { get; } = width;
    public int Height { get; } = height;

    public CellState this[int row, int col]
    {
        get => Cells[row, col];
        set => Cells[row, col] = value;
    }

    public Grid Clone()
    {
        var clone = new Grid(Width, Height);
        Array.Copy(Cells, clone.Cells, Cells.Length);
        return clone;
    }

    public int AvailableCellCount
    {
        get
        {
            int count = 0;
            for (int r = 0; r < Height; r++)
            for (int c = 0; c < Width; c++)
            {
                if (Cells[r, c] == CellState.Available)
                    count++;
            }
            return count;
        }
    }

    public bool IsInBounds(int row, int col) =>
        row >= 0 && row < Height && col >= 0 && col < Width;

    public bool IsAvailable(int row, int col) =>
        IsInBounds(row, col) && Cells[row, col] == CellState.Available;
}
