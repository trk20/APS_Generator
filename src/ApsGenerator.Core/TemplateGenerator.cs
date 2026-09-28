using ApsGenerator.Core.Models;

namespace ApsGenerator.Core;

public static class TemplateGenerator
{
    private const double RadiusEpsilon = 0.01;

    public static Grid Circle(int diameter, bool blockCenter)
    {
        int holeSize = blockCenter && diameter % 2 != 0 ? 1 : 0;
        return Circle(diameter, CenterHoleShape.Circle, holeSize);
    }

    public static Grid Circle(int diameter, CenterHoleShape holeShape, int holeSize)
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

            if (distSq >= radiusSq - RadiusEpsilon || IsInsideHole(dx, dy, holeShape, holeSize))
                grid[r, c] = CellState.Blocked;
        }

        return grid;
    }

    public static Grid Rectangle(int width, int height) => new(width, height);

    private static bool IsInsideHole(double x, double y, CenterHoleShape shape, int size)
    {
        if (size <= 0)
            return false;

        double radius = size / 2.0;
        return shape switch
        {
            CenterHoleShape.Circle => x * x + y * y < radius * radius - RadiusEpsilon,
            CenterHoleShape.Square => Math.Abs(x) < radius && Math.Abs(y) < radius,
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
        };
    }
}
