using ApsGenerator.Core;
using ApsGenerator.Core.Models;

namespace ApsGenerator.Core.Tests;

public sealed class TemplateGeneratorTests
{
    [Fact]
    public void Circle_DiameterFive_NoCenterBlock_HasExpectedAvailableCellCount()
    {
        const int diameter = 5;
        var grid = TemplateGenerator.Circle(diameter, blockCenter: false);

        var expectedAvailable = CountExpectedCircleAvailable(diameter, blockCenter: false);

        Assert.Equal(expectedAvailable, grid.AvailableCellCount);
    }

    [Fact]
    public void Circle_DiameterFive_BlockCenter_HasCenterBlocked()
    {
        const int diameter = 5;
        var grid = TemplateGenerator.Circle(diameter, blockCenter: true);

        int center = diameter / 2;
        Assert.Equal(CellState.Blocked, grid[center, center]);
    }

    [Fact]
    public void Circle_DiameterEleven_BlockCenter_HasCenterBlockedAndExpectedShape()
    {
        const int diameter = 11;
        var grid = TemplateGenerator.Circle(diameter, blockCenter: true);

        int center = diameter / 2;
        Assert.Equal(CellState.Blocked, grid[center, center]);

        for (int row = 0; row < diameter; row++)
        for (int col = 0; col < diameter; col++)
        {
            var expected = IsBlockedByCircleFormula(diameter, row, col, blockCenter: true)
                ? CellState.Blocked
                : CellState.Available;

            Assert.Equal(expected, grid[row, col]);
        }
    }

    [Fact]
    public void Rectangle_CreatesAllAvailableGridWithCorrectDimensions()
    {
        var grid = TemplateGenerator.Rectangle(width: 8, height: 6);

        Assert.Equal(8, grid.Width);
        Assert.Equal(6, grid.Height);
        Assert.Equal(48, grid.AvailableCellCount);
    }

    [Fact]
    public void Circle_HasPointSymmetryForBlockedCells()
    {
        const int diameter = 11;
        var grid = TemplateGenerator.Circle(diameter, blockCenter: true);

        for (int row = 0; row < grid.Height; row++)
        for (int col = 0; col < grid.Width; col++)
        {
            int symmetricRow = grid.Height - 1 - row;
            int symmetricCol = grid.Width - 1 - col;

            bool isBlocked = grid[row, col] == CellState.Blocked;
            bool symmetricIsBlocked = grid[symmetricRow, symmetricCol] == CellState.Blocked;

            Assert.Equal(isBlocked, symmetricIsBlocked);
        }
    }

    private static int CountExpectedCircleAvailable(int diameter, bool blockCenter)
    {
        int available = 0;

        for (int row = 0; row < diameter; row++)
        for (int col = 0; col < diameter; col++)
        {
            if (!IsBlockedByCircleFormula(diameter, row, col, blockCenter))
                available++;
        }

        return available;
    }

    private static bool IsBlockedByCircleFormula(int diameter, int row, int col, bool blockCenter)
    {
        double centerX = (diameter - 1.0) / 2.0;
        double centerY = (diameter - 1.0) / 2.0;
        double radius = diameter / 2.0;
        double radiusSq = radius * radius;

        double dy = row - centerY;
        double dx = col - centerX;
        double distSq = dy * dy + dx * dx;

        bool blocked = distSq >= radiusSq - 0.01;
        if (!blocked && blockCenter && diameter % 2 != 0)
            blocked = row == (int)centerY && col == (int)centerX;

        return blocked;
    }
}