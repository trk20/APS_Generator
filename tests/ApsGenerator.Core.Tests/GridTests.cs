using ApsGenerator.Core.Models;

namespace ApsGenerator.Core.Tests;

public sealed class GridTests
{
    [Fact]
    public void Constructor_CreatesGridWithCorrectDimensions()
    {
        var grid = new Grid(width: 7, height: 4);

        Assert.Equal(7, grid.Width);
        Assert.Equal(4, grid.Height);
        Assert.Equal(4, grid.Cells.GetLength(0));
        Assert.Equal(7, grid.Cells.GetLength(1));
    }

    [Fact]
    public void Constructor_InitializesAllCellsAsAvailable()
    {
        var grid = new Grid(width: 5, height: 3);

        for (int row = 0; row < grid.Height; row++)
        for (int col = 0; col < grid.Width; col++)
            Assert.Equal(CellState.Available, grid[row, col]);
    }

    [Fact]
    public void Indexer_GetSet_Works()
    {
        var grid = new Grid(width: 3, height: 2);

        grid[1, 2] = CellState.Blocked;

        Assert.Equal(CellState.Blocked, grid[1, 2]);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(2, 3, true)]
    [InlineData(-1, 0, false)]
    [InlineData(0, -1, false)]
    [InlineData(3, 0, false)]
    [InlineData(0, 4, false)]
    [InlineData(3, 4, false)]
    public void IsInBounds_ReturnsExpectedResult(int row, int col, bool expected)
    {
        var grid = new Grid(width: 4, height: 3);

        var actual = grid.IsInBounds(row, col);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsAvailable_ReturnsFalseForBlockedCell()
    {
        var grid = new Grid(width: 3, height: 3);
        grid[1, 1] = CellState.Blocked;

        Assert.False(grid.IsAvailable(1, 1));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(3, 1)]
    [InlineData(1, 3)]
    public void IsAvailable_ReturnsFalseForOutOfBounds(int row, int col)
    {
        var grid = new Grid(width: 3, height: 3);

        Assert.False(grid.IsAvailable(row, col));
    }

    [Fact]
    public void AvailableCellCount_DecreasesWhenCellsAreBlocked()
    {
        var grid = new Grid(width: 4, height: 4);
        var initialCount = grid.AvailableCellCount;

        grid[0, 0] = CellState.Blocked;
        grid[1, 2] = CellState.Blocked;
        grid[3, 3] = CellState.Blocked;

        Assert.Equal(16, initialCount);
        Assert.Equal(13, grid.AvailableCellCount);
    }
}