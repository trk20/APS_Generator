namespace ApsGenerator.Core.Models;

/// <summary>Which extra layers to include when exporting a blueprint.</summary>
public enum ExportExtraLayers
{
    /// <summary>Ejectors, bottom/top intakes, and cooler snake (game-ready APS).</summary>
    EjectorsIntakesCoolerSnake,

    /// <summary>Bottom ejectors and intakes only; no cooler snake.</summary>
    EjectorsIntakes,

    /// <summary>Full bottom intakes (zero top deficit) plus cooler snake; no ejectors.</summary>
    IntakesCoolerSnake,

    /// <summary>Bottom intakes under loader and clips; no cooler snake.</summary>
    IntakesOnly,

    /// <summary>Loaders and clips only; no bottom hardware or cooler snake.</summary>
    TetrisOnly,
}

public static class ExportExtraLayersExtensions
{
    public static bool NeedsCoolerSolve(this ExportExtraLayers layers, TetrisType type) =>
        type == TetrisType.FiveClip
            ? layers == ExportExtraLayers.EjectorsIntakesCoolerSnake
            : layers is ExportExtraLayers.EjectorsIntakesCoolerSnake
                or ExportExtraLayers.IntakesCoolerSnake;

    public static bool OmitEjectorsForCoolerSolve(this ExportExtraLayers layers) =>
        layers == ExportExtraLayers.IntakesCoolerSnake;

    public static bool IsValidFor(this ExportExtraLayers layers, TetrisType type)
    {
        if (type != TetrisType.FiveClip)
            return true;

        return layers is ExportExtraLayers.EjectorsIntakesCoolerSnake
            or ExportExtraLayers.TetrisOnly;
    }

    public static ExportExtraLayers ClampFor(this ExportExtraLayers layers, TetrisType type) =>
        layers.IsValidFor(type) ? layers : ExportExtraLayers.EjectorsIntakesCoolerSnake;

    public static string DisplayLabel(this ExportExtraLayers layers, TetrisType type)
    {
        if (type == TetrisType.FiveClip)
        {
            return layers switch
            {
                ExportExtraLayers.EjectorsIntakesCoolerSnake => "Cooler Snake",
                ExportExtraLayers.TetrisOnly => "Tetris only",
                _ => layers.DisplayLabel(TetrisType.ThreeClip),
            };
        }

        return layers switch
        {
            ExportExtraLayers.EjectorsIntakesCoolerSnake => "Ejectors + Intakes + Cooler Snake",
            ExportExtraLayers.EjectorsIntakes => "Ejectors + Intakes",
            ExportExtraLayers.IntakesCoolerSnake => "Intakes + Cooler Snake",
            ExportExtraLayers.IntakesOnly => "Intakes only",
            ExportExtraLayers.TetrisOnly => "Tetris only",
            _ => layers.ToString(),
        };
    }

    public static IReadOnlyList<ExportExtraLayers> OptionsFor(TetrisType type) =>
        type == TetrisType.FiveClip
            ?
            [
                ExportExtraLayers.EjectorsIntakesCoolerSnake,
                ExportExtraLayers.TetrisOnly,
            ]
            :
            [
                ExportExtraLayers.EjectorsIntakesCoolerSnake,
                ExportExtraLayers.EjectorsIntakes,
                ExportExtraLayers.IntakesCoolerSnake,
                ExportExtraLayers.IntakesOnly,
                ExportExtraLayers.TetrisOnly,
            ];
}
