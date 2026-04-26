using ApsGenerator.Core.Models;

namespace ApsGenerator.Core;

public static class TemplateGenerator
{
    private const double RadiusEpsilon = 0.01;

    public static Grid Circle(int diameter, bool blockCenter)
    {
        var grid = new Grid(diameter, diameter);

        double centerX = (diameter - 1.0) / 2.0;
        double centerY = (diameter - 1.0) / 2.0;
        double radius = diameter / 2.0;
        double radiusSq = radius * radius;

        for (int r = 0; r < diameter; r++)
        for (int c = 0; c < diameter; c++)
        {
            double dy = r - centerY;
            double dx = c - centerX;
            double distSq = dy * dy + dx * dx;

            if (distSq >= radiusSq - RadiusEpsilon)
                grid[r, c] = CellState.Blocked;
        }

        if (blockCenter && diameter % 2 != 0)
            grid[(int)centerY, (int)centerX] = CellState.Blocked;

        return grid;
    }

    public static Grid Rectangle(int width, int height) => new(width, height);
}
