using System.Collections.Concurrent;
using ApsGenerator.Core;
using ApsGenerator.Core.Models;

namespace ApsGenerator.Mod.Generation;

internal enum ApsSolveSize
{
    Normal,
    /// <summary>The solver is unlikely to finish within the default time; the layout may be less dense.</summary>
    Large,
    /// <summary>Default-time results fall well behind what the solver manages at smaller sizes or with symmetry.</summary>
    TooLarge
}

/// <summary>
/// Rough size limits per Tetris configuration measured as the diameter of the default center-hole
/// template and applied based on available cell count.
/// </summary>
internal static class ApsSolveSizeLimits
{
    internal enum Limit { Recommended, Maximum }

    private const int NoLimit = ApsGenerationSettings.MaximumDimension;

    private static readonly ConcurrentDictionary<int, int> CircleCells = new();

    internal static ApsSolveSize Classify(int availableCells, TetrisType type, SymmetryType symmetry, SymmetryMode mode)
    {
        if (availableCells > CellBudget(type, symmetry, mode, Limit.Maximum))
            return ApsSolveSize.TooLarge;
        return availableCells > CellBudget(type, symmetry, mode, Limit.Recommended)
            ? ApsSolveSize.Large
            : ApsSolveSize.Normal;
    }

    internal static int CellBudget(TetrisType type, SymmetryType symmetry, SymmetryMode mode, Limit limit)
    {
        (int recommended, int maximum) = Diameters(type, mode == SymmetryMode.Soft ? SymmetryType.None : symmetry);
        int diameter = limit == Limit.Recommended ? recommended : maximum;
        if (diameter >= NoLimit)
            return int.MaxValue;
        return CircleCells.GetOrAdd(diameter, size => TemplateGenerator.Circle(
            size, ApsGenerationDefaults.DefaultCenterHoleShape, size % 2 == 0 ? 2 : 1).AvailableCellCount);
    }

    /// <summary>Recommended and maximum center-hole diameters.</summary>
    internal static (int Recommended, int Maximum) Diameters(TetrisType type, SymmetryType symmetry) => type switch
    {
        TetrisType.FiveClip => symmetry switch
        {
            SymmetryType.None => (13, 17),
            SymmetryType.HorizontalReflection or SymmetryType.VerticalReflection => (17, 29),
            SymmetryType.BothReflection => (25, 39),
            SymmetryType.Rotation180 => (13, 25),
            _ => (21, NoLimit)
        },
        TetrisType.FourClip => symmetry switch
        {
            SymmetryType.None => (25, 39),
            SymmetryType.HorizontalReflection or SymmetryType.VerticalReflection => (33, NoLimit),
            SymmetryType.BothReflection => (39, NoLimit),
            SymmetryType.Rotation180 => (25, NoLimit),
            _ => (45, NoLimit)
        },
        _ => symmetry == SymmetryType.None ? (41, NoLimit) : (NoLimit, NoLimit)
    };
}
