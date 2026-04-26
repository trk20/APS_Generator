using ApsGenerator.Core;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver;
using Xunit.Abstractions;

namespace ApsGenerator.Solver.Tests;

internal static class SolutionVisualizer
{
    /// <summary>
    /// Renders a solution grid showing cluster assignments.
    /// Each cluster gets a letter A-Z. Connection cells shared between clusters show '+'.
    /// Blocked cells show '#'. Empty available cells show '.'.
    /// </summary>
    public static string Render(Grid grid, TetrisType type, SolverResult result)
    {
        var shapes = ClusterShape.GetShapes(type);
        var cellOwners = new Dictionary<(int, int), List<char>>();
        char label = 'A';

        foreach (var p in result.Placements)
        {
            var offsets = shapes[p.ShapeIndex].Offsets;
            foreach (var offset in offsets)
            {
                var cell = (p.Row + offset.DeltaRow, p.Col + offset.DeltaCol);
                if (!cellOwners.ContainsKey(cell))
                    cellOwners[cell] = [];
                cellOwners[cell].Add(label);
            }

            label = (char)(label + 1);
            if (label > 'Z')
                label = 'a';
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Grid {grid.Width}x{grid.Height}, {type}, {result.Placements.Count} clusters, {result.EmptyCells} empty");
        sb.AppendLine(new string('-', grid.Width * 2 + 1));

        for (int r = 0; r < grid.Height; r++)
        {
            sb.Append('|');
            for (int c = 0; c < grid.Width; c++)
            {
                if (!grid.IsAvailable(r, c))
                    sb.Append('#');
                else if (cellOwners.TryGetValue((r, c), out var owners))
                {
                    if (owners.Count > 1)
                        sb.Append('+');
                    else
                        sb.Append(owners[0]);
                }
                else
                    sb.Append('.');

                sb.Append(' ');
            }

            sb.AppendLine("|");
        }

        sb.AppendLine(new string('-', grid.Width * 2 + 1));
        return sb.ToString();
    }

    public static void Print(ITestOutputHelper output, Grid grid, TetrisType type, SolverResult result)
    {
        output.WriteLine(Render(grid, type, result));
    }
}