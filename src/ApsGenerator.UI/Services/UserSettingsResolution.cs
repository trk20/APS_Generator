using ApsGenerator.Core.Models;
using ApsGenerator.UI.Models;
using ApsGenerator.UI.ViewModels;

namespace ApsGenerator.UI.Services;

/// <summary>Clamps persisted settings and maps them to/from the main window VM.</summary>
internal static class UserSettingsResolution
{
    private const int MinTemplateDimension = 3;
    private const int MaxTemplateDimension = 50;
    private const double MinSolverSeconds = 1;
    private const double MaxSolverSeconds = 600;

    public static UserSettings Validate(UserSettings settings, int maxThreadCount)
    {
        var defaults = new UserSettings();

        int width = ClampInt(
            settings.TemplateWidth, defaults.TemplateWidth, MinTemplateDimension, MaxTemplateDimension);
        int height = ClampInt(
            settings.TemplateHeight, defaults.TemplateHeight, MinTemplateDimension, MaxTemplateDimension);
        bool heightLocked = settings.IsHeightLocked;
        if (heightLocked)
            height = width;

        return new UserSettings
        {
            TemplateShape = DefinedOr(settings.TemplateShape, defaults.TemplateShape),
            TemplateWidth = width,
            TemplateHeight = height,
            IsHeightLocked = heightLocked,
            SelectedTetrisType = DefinedOr(settings.SelectedTetrisType, defaults.SelectedTetrisType),
            SelectedSymmetryType = DefinedOr(settings.SelectedSymmetryType, defaults.SelectedSymmetryType),
            IsHardSymmetry = settings.IsHardSymmetry,
            EarlyStopEnabled = settings.EarlyStopEnabled,
            GenerateCoolerSnake = settings.GenerateCoolerSnake,
            ShowCoolerOverlay = settings.ShowCoolerOverlay,
            MaxTimeSeconds = ClampDouble(
                settings.MaxTimeSeconds, defaults.MaxTimeSeconds, MinSolverSeconds, MaxSolverSeconds),
            UiScale = settings.UiScale,
            AutoUpdate = settings.AutoUpdate,
            ReceiveExperimentalUpdates = settings.ReceiveExperimentalUpdates,
            ShowReleaseNotesAfterUpdate = settings.ShowReleaseNotesAfterUpdate,
            PendingReleaseNotesVersion = settings.PendingReleaseNotesVersion,
            PendingReleaseNotesContent = settings.PendingReleaseNotesContent,
            LastSeenUpdateVersion = settings.LastSeenUpdateVersion,
            TargetPlacementCount = settings.TargetPlacementCount >= 0
                ? settings.TargetPlacementCount
                : defaults.TargetPlacementCount,
            IsMaximize = settings.IsMaximize,
            PaintMode = DefinedOr(settings.PaintMode, defaults.PaintMode),
            LastExportFolder = settings.LastExportFolder,
            ThreadCount = ClampInt(settings.ThreadCount, defaults.ThreadCount, 1, maxThreadCount),
            DefaultExportHeightBasic = ClampInt(
                settings.DefaultExportHeightBasic, defaults.DefaultExportHeightBasic, 1, 8),
            DefaultExportHeightFiveClip = FiveClipExportHeight(
                settings.DefaultExportHeightFiveClip, defaults.DefaultExportHeightFiveClip),
            ExportExtraLayersBasic = DefinedOr(
                settings.ExportExtraLayersBasic, defaults.ExportExtraLayersBasic),
            ExportExtraLayersFiveClip = settings.ExportExtraLayersFiveClip.ClampFor(TetrisType.FiveClip),
            ExportNameTemplate = string.IsNullOrWhiteSpace(settings.ExportNameTemplate)
                ? defaults.ExportNameTemplate
                : settings.ExportNameTemplate,
            NumSolutions = ClampInt(settings.NumSolutions, defaults.NumSolutions, 1, 50),
        };
    }

    public static UserSettings ApplyTo(MainWindowViewModel vm, UserSettings settings, int maxThreadCount)
    {
        var validated = Validate(settings, maxThreadCount);

        vm.TemplateShape = FindDisplayItem(
            TemplateShapeValues.All, validated.TemplateShape, TemplateShapeValues.All[1]);
        vm.TemplateWidth = validated.TemplateWidth;
        vm.TemplateHeight = validated.TemplateHeight;
        vm.IsHeightLocked = validated.IsHeightLocked;
        vm.SelectedTetrisType = FindDisplayItem(
            EnumValues.TetrisTypes, validated.SelectedTetrisType, EnumValues.TetrisTypes[0]);
        vm.SelectedSymmetryType = FindDisplayItem(
            EnumValues.SymmetryTypes, validated.SelectedSymmetryType, EnumValues.SymmetryTypes[0]);
        vm.IsHardSymmetry = validated.IsHardSymmetry;
        vm.EarlyStopEnabled = validated.EarlyStopEnabled;
        vm.GenerateCoolerSnake = validated.GenerateCoolerSnake;
        vm.ShowCoolerOverlay = validated.ShowCoolerOverlay;
        vm.MaxTimeSeconds = validated.MaxTimeSeconds;
        vm.UiScale = validated.UiScale;
        vm.AutoUpdate = validated.AutoUpdate;
        vm.ReceiveExperimentalUpdates = validated.ReceiveExperimentalUpdates;
        vm.ShowReleaseNotesAfterUpdate = validated.ShowReleaseNotesAfterUpdate;
        vm.PendingReleaseNotesVersion = validated.PendingReleaseNotesVersion;
        vm.PendingReleaseNotesContent = validated.PendingReleaseNotesContent;
        vm.LastSeenUpdateVersion = validated.LastSeenUpdateVersion;
        vm.TargetPlacementCount = validated.TargetPlacementCount;
        vm.PaintMode = validated.PaintMode;
        vm.LastExportFolder = validated.LastExportFolder;
        vm.ThreadCount = validated.ThreadCount;
        vm.DefaultExportHeightBasic = validated.DefaultExportHeightBasic;
        vm.DefaultExportHeightFiveClip = validated.DefaultExportHeightFiveClip;
        vm.ExportExtraLayersBasic = validated.ExportExtraLayersBasic;
        vm.ExportExtraLayersFiveClip = validated.ExportExtraLayersFiveClip;
        vm.ExportNameTemplate = validated.ExportNameTemplate;
        vm.NumSolutions = validated.NumSolutions;

        return validated;
    }

    public static UserSettings FromViewModel(MainWindowViewModel vm) =>
        Validate(DraftFromViewModel(vm), vm.MaxThreadCount);

    private static UserSettings DraftFromViewModel(MainWindowViewModel vm) => new()
    {
        TemplateShape = vm.TemplateShape.Value,
        TemplateWidth = vm.TemplateWidth,
        TemplateHeight = vm.TemplateHeight,
        IsHeightLocked = vm.IsHeightLocked,
        SelectedTetrisType = vm.SelectedTetrisType.Value,
        SelectedSymmetryType = vm.SelectedSymmetryType.Value,
        IsHardSymmetry = vm.IsHardSymmetry,
        EarlyStopEnabled = vm.EarlyStopEnabled,
        GenerateCoolerSnake = vm.GenerateCoolerSnake,
        ShowCoolerOverlay = vm.ShowCoolerOverlay,
        MaxTimeSeconds = vm.MaxTimeSeconds,
        IsMaximize = vm.IsMaximize,
        TargetPlacementCount = vm.TargetPlacementCount,
        PaintMode = vm.PaintMode,
        LastExportFolder = vm.LastExportFolder,
        ThreadCount = vm.ThreadCount,
        DefaultExportHeightBasic = vm.DefaultExportHeightBasic,
        DefaultExportHeightFiveClip = vm.DefaultExportHeightFiveClip,
        ExportExtraLayersBasic = vm.ExportExtraLayersBasic,
        ExportExtraLayersFiveClip = vm.ExportExtraLayersFiveClip,
        ExportNameTemplate = vm.ExportNameTemplate,
        NumSolutions = vm.NumSolutions,
        UiScale = vm.UiScale,
        AutoUpdate = vm.AutoUpdate,
        ReceiveExperimentalUpdates = vm.ReceiveExperimentalUpdates,
        ShowReleaseNotesAfterUpdate = vm.ShowReleaseNotesAfterUpdate,
        PendingReleaseNotesVersion = vm.PendingReleaseNotesVersion,
        PendingReleaseNotesContent = vm.PendingReleaseNotesContent,
        LastSeenUpdateVersion = vm.LastSeenUpdateVersion,
    };

    private static EnumDisplayItem<TEnum> FindDisplayItem<TEnum>(
        IReadOnlyList<EnumDisplayItem<TEnum>> values,
        TEnum value,
        EnumDisplayItem<TEnum> fallback)
        where TEnum : struct, Enum
    {
        foreach (var item in values)
        {
            if (EqualityComparer<TEnum>.Default.Equals(item.Value, value))
                return item;
        }

        return fallback;
    }

    private static TEnum DefinedOr<TEnum>(TEnum settingValue, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.IsDefined(settingValue) ? settingValue : fallback;

    private static int ClampInt(int settingValue, int fallback, int min, int max)
    {
        if (settingValue < min || settingValue > max)
            return fallback;

        return settingValue;
    }

    private static double ClampDouble(double settingValue, double fallback, double min, double max)
    {
        if (double.IsNaN(settingValue) || double.IsInfinity(settingValue))
            return fallback;

        if (settingValue < min || settingValue > max)
            return fallback;

        return settingValue;
    }

    private static int FiveClipExportHeight(int settingValue, int fallback)
    {
        int value = ClampInt(settingValue, fallback, FiveClipHeight.MinHeight, FiveClipHeight.MaxHeight);
        return FiveClipHeight.RoundToMultipleOf3(value);
    }
}
