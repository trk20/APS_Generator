using ApsGenerator.Core.Models;
using UnityEngine;
using ApsGenerator.Mod.Generation;

namespace ApsGenerator.Mod.UI;

internal interface IApsGeneratorHudController
{
    ApsGenerationSettings Settings { get; }
    string Status { get; }
    bool IsSolving { get; }
    bool HasCurrentSolution { get; }
    bool CanShowPrevious { get; }
    bool HasActivePrefab { get; }
    float SolveElapsedSeconds { get; }
    void Generate();
    void NextLayout();
    void PreviousSolution();
    void Cancel();
    void HideHud();
    void ResetDefaults();
    void Save();
    void Clear();
    void ChangeSettings(Action change);
    void ClaimHudInput();
}

/// <summary>
/// Immediate-mode HUD. Layout is written in logical units and scaled at draw time so the
/// panel grows with screen resolution and user's size preference. Text is
/// rendered at the scaled font size.
/// </summary>
internal sealed class ApsGeneratorHud : IDisposable
{
    /// <summary>Logical-to-pixel scale at 1080 lines before the user's size preference.</summary>
    private const float BaseScaleAt1080 = 1.25f;
    private const float MinimumScale = 0.8f;
    private const float MaximumScale = 4f;
    private const float MaximumPanelWidth = 866;
    /// <summary>Narrowest logical width before the scale is reduced to fit the screen.</summary>
    private const float MinimumLogicalWidth = 780;
    private const float PanelMargin = 12;
    private const float Padding = 12;
    private const float Gap = 8;
    private const float HeaderHeight = 43;
    private const float BodyHeight = 150;
    private const float ActionHeight = 34;
    private const float SelectorHeight = 26;
    private const float StepperRowHeight = 28;
    private const float StepperButtonWidth = 27;
    private const float StepperControlGap = 1;
    private const float DropdownRowHeight = 22;
    private const float CompactStepperWidth = 82;
    private const float CompactControlGap = 16;
    private const float TemplateCardWidth = 248;
    private const float TetrisOptionsCardWidth = 172;
    private const float PrefabCardWidth = 200;
    private const float SolveSettingsCardWidth = 196;
    private const float SymmetryModeWidth = 54;

    private static readonly Color WarningColor = new(1f, 0.78f, 0.12f);
    private static readonly Color DangerColor = new(1f, 0.38f, 0.32f);
    private static readonly Color FastColor = new(0.42f, 0.90f, 0.55f);
    private static readonly Color MutedColor = new(0.68f, 0.78f, 0.82f);

    private readonly List<Texture2D> textures = [];
    private SelectorKind expandedSelector;
    private float scale = 1;
    private float stylesScale = -1;
    private Texture2D? warningChip;
    private Texture2D? dangerChip;
    private Texture2D? sliderTrack;
    private Texture2D? sliderFill;
    private GUIStyle? panelStyle;
    private GUIStyle? cardStyle;
    private GUIStyle? buttonStyle;
    private GUIStyle? selectedButtonStyle;
    private GUIStyle? primaryButtonStyle;
    private GUIStyle? titleStyle;
    private GUIStyle? valueStyle;
    private GUIStyle? bodyStyle;
    private GUIStyle? mutedStyle;
    private GUIStyle? detailStyle;
    private GUIStyle? chipStyle;
    private GUIStyle? speedStyle;
    private Texture2D? zoomOutIcon;
    private Texture2D? zoomInIcon;
    private bool draggingDensity;

    internal Rect Draw(IApsGeneratorHudController controller)
    {
        ApsGenerationSettings settings = controller.Settings;
        ApsSolveSize size = settings.SolveSize;
        float logicalHeight = PanelHeight;
        scale = CalculateScale(settings.HudScalePercent);
        EnsureStyles();
        Rect rect = CalculatePanelRect(logicalHeight);
        GuiState previous = GuiState.Capture();
        try
        {
            GuiState.ApplyForPanel();
            GUI.Box(rect, GUIContent.none, panelStyle!);
            GUI.BeginGroup(rect);
            try { DrawPanel(controller, size, rect.width / scale, rect.height / scale); }
            finally { GUI.EndGroup(); }
        }
        finally
        {
            previous.Restore();
        }
        return rect;
    }

    /// <summary>True while a HUD control owns the mouse, even outside the panel.</summary>
    internal bool IsCapturingPointer => draggingDensity;

    public void Dispose()
    {
        foreach (Texture2D texture in textures)
            UnityEngine.Object.Destroy(texture);
        textures.Clear();
    }

    /// <summary>Resolution-relative scale for HUD-adjacent controls such as the launcher button.</summary>
    internal static float ResolutionScale => Mathf.Clamp(Screen.height / 1080f * BaseScaleAt1080, MinimumScale, MaximumScale);

    private const float PanelHeight = HeaderHeight + BodyHeight + 6 + ActionHeight + Padding;

    private void DrawPanel(IApsGeneratorHudController controller, ApsSolveSize size, float width, float height)
    {
        const float bodyTop = HeaderHeight;
        Rect[] columns = CalculateColumns(width, bodyTop);
        var selectors = CreateSelectors(columns, controller.Settings, height);
        DismissDropdownOnOutsideClick(selectors, controller);
        DrawHeader(controller, width);

        bool previousEnabled = GUI.enabled;
        DrawTemplateCard(columns[0], selectors, controller, size);
        DrawTetrisOptionsCard(columns[1], selectors, controller);
        DrawPrefabCard(columns[2], selectors, controller);
        DrawSolveSettingsCard(columns[3], controller);
        DrawActions(controller, width, bodyTop + BodyHeight + 6, size);
        DrawSizeControls(controller, width, bodyTop + BodyHeight + 6);
        GUI.enabled = previousEnabled;
        DrawExpandedSelector(selectors, controller);
    }

    private void DrawActions(IApsGeneratorHudController controller, float width, float y, ApsSolveSize size)
    {
        const float primaryWidth = 210;
        const float previousWidth = 96;
        const float utilityWidth = 70;
        bool showPrevious = controller.HasCurrentSolution && size != ApsSolveSize.TooLarge;
        float groupWidth = primaryWidth +
                           (showPrevious ? previousWidth + Gap : 0) +
                           (controller.HasActivePrefab ? (utilityWidth + Gap) * 2 : 0);
        float x = (width - groupWidth) / 2;
        bool previousEnabled = GUI.enabled;
        bool idle = expandedSelector == SelectorKind.None;
        GUI.enabled = previousEnabled && idle;

        if (showPrevious)
        {
            GUI.enabled = previousEnabled && idle && !controller.IsSolving && controller.CanShowPrevious;
            DrawButton(new Rect(x, y, previousWidth, ActionHeight), new GUIContent("PREVIOUS"),
                controller.PreviousSolution, controller, buttonStyle!);
            GUI.enabled = previousEnabled && idle;
            x += previousWidth + Gap;
        }

        var primary = new Rect(x, y, primaryWidth, ActionHeight);
        if (controller.IsSolving)
            DrawButton(primary, new GUIContent("CANCEL"), controller.Cancel, controller, primaryButtonStyle!);
        else if (size == ApsSolveSize.TooLarge)
        {
            GUI.enabled = false;
            DrawButton(primary, new GUIContent("TOO LARGE"),
                () => { }, controller, primaryButtonStyle!);
            GUI.enabled = previousEnabled && idle;
        }
        else if (controller.HasCurrentSolution)
            DrawButton(primary, new GUIContent("NEXT SOLUTION", "Find a different solution at the same density"),
                controller.NextLayout, controller, primaryButtonStyle!);
        else
            DrawButton(primary, new GUIContent("GENERATE"), controller.Generate, controller, primaryButtonStyle!);

        if (controller.HasActivePrefab)
        {
            DrawButton(new Rect(x + primaryWidth + Gap, y, utilityWidth, ActionHeight),
                new GUIContent("SAVE"), controller.Save, controller, buttonStyle!);
            DrawButton(new Rect(x + primaryWidth + Gap * 2 + utilityWidth, y, utilityWidth, ActionHeight),
                new GUIContent("CLEAR"), controller.Clear, controller, buttonStyle!);
        }
        GUI.enabled = previousEnabled;
    }

    private void DrawHeader(IApsGeneratorHudController controller, float width)
    {
        const float closeWidth = 42;
        const float defaultsWidth = 76;
        float closeX = width - Padding - closeWidth;
        float defaultsX = closeX - Gap - defaultsWidth;
        Label(new Rect(Padding, 3, 170, 36), "APS GENERATOR", titleStyle!);
        float statusX = 164;
        if (controller.IsSolving)
        {
            DrawSpinner(new Rect(statusX, 9, 22, 22), controller.SolveElapsedSeconds);
            statusX += 28;
        }
        string status = controller.IsSolving
            ? $"{controller.Status} · {controller.SolveElapsedSeconds:0.0}s"
            : controller.Status;
        Label(new Rect(statusX, 3, defaultsX - statusX - Gap, 36), status, bodyStyle!);
        DrawButton(new Rect(defaultsX, 5, defaultsWidth, 30), new GUIContent("Defaults"), controller.ResetDefaults, controller, buttonStyle!);
        DrawButton(new Rect(closeX, 5, closeWidth, 30), new GUIContent("×"), controller.HideHud, controller, buttonStyle!);
    }

    private void DrawTemplateCard(
        Rect area,
        IReadOnlyDictionary<SelectorKind, Selector> selectors,
        IApsGeneratorHudController controller,
        ApsSolveSize size)
    {
        ApsGenerationSettings settings = controller.Settings;
        DrawCard(area, "TEMPLATE");
        DrawSizeChip(area, settings, size);
        Label(new Rect(area.x + 9, area.y + 29, 46, SelectorHeight), "Shape", mutedStyle!);
        DrawSelectorButton(SelectorKind.Template, selectors[SelectorKind.Template], controller);

        DrawDimensionPair(Row(area, 57, StepperRowHeight), settings, controller, size);
        float evenSizesY = 85;
        if (settings.TemplateShape == ApsTemplateShape.CircleCenterHole)
        {
            DrawHoleSizeRow(Row(area, 85, StepperRowHeight), settings, controller);
            evenSizesY = 113;
        }
        Rect evenSizesRect = CenteredAfterLabelRect(area, evenSizesY, 72, 92, SelectorHeight);
        Label(new Rect(area.x + 9, area.y + evenSizesY, 72, SelectorHeight), "Even sizes", mutedStyle!);
        DrawSegmented(
            evenSizesRect,
            [new Choice<bool>("Off", false), new Choice<bool>("On", true)],
            settings.AllowEvenDimensions,
            settings.SetAllowEvenDimensions,
            controller);
    }

    /// <summary>Filled tag beside the card title: SLOW above the recommended size, MAX n at the limit.</summary>
    /// <summary>HUD size controls, bottom right on the action row.</summary>
    private void DrawSizeControls(IApsGeneratorHudController controller, float width, float actionY)
    {
        const float buttonWidth = 30;
        const float valueWidth = 46;
        ApsGenerationSettings settings = controller.Settings;
        float y = actionY + (ActionHeight - 30) / 2;
        float largerX = width - Padding - buttonWidth;
        float valueX = largerX - valueWidth;
        float smallerX = valueX - buttonWidth;
        DrawZoomButton(new Rect(smallerX, y, buttonWidth, 30), zoomOutIcon!,
            settings.HudScalePercent > ApsGenerationSettings.MinimumHudScalePercent,
            () => settings.AdjustHudScale(-1), controller);
        Label(new Rect(valueX, y, valueWidth, 30), $"{settings.HudScalePercent}%", valueStyle!);
        DrawZoomButton(new Rect(largerX, y, buttonWidth, 30), zoomInIcon!,
            settings.HudScalePercent < ApsGenerationSettings.MaximumHudScalePercent,
            () => settings.AdjustHudScale(1), controller);
    }

    private void DrawZoomButton(Rect rect, Texture2D icon, bool enabled, Action change, IApsGeneratorHudController controller)
    {
        bool previousEnabled = GUI.enabled;
        GUI.enabled = previousEnabled && enabled;
        DrawSettingButton(rect, GUIContent.none, buttonStyle!, change, controller);
        Color previousColor = GUI.color;
        if (!GUI.enabled)
            GUI.color = new Color(1, 1, 1, 0.35f);
        GUI.DrawTexture(Scaled(new Rect(rect.center.x - 9, rect.center.y - 9, 18, 18)), icon, ScaleMode.ScaleToFit);
        GUI.color = previousColor;
        GUI.enabled = previousEnabled;
    }

    private void DrawSizeChip(Rect card, ApsGenerationSettings settings, ApsSolveSize size)
    {
        if (size == ApsSolveSize.Normal)
            return;
        bool tooLarge = size == ApsSolveSize.TooLarge;
        string text = "SLOW";
        if (tooLarge)
        {
            int maximum = settings.MaximumWidthFor(settings.SymmetryType);
            text = settings.IsDepthLocked ? $"MAX {maximum}×{maximum}" : $"MAX WIDTH {maximum}";
        }
        float width = chipStyle!.CalcSize(new GUIContent(text)).x / scale + 14;
        var rect = new Rect(card.xMax - 9 - width, card.y + 5, width, 20);
        GUI.DrawTexture(Scaled(rect), tooLarge ? dangerChip! : warningChip!);
        Label(rect, text, chipStyle);
    }

    private void DrawTetrisOptionsCard(
        Rect area,
        IReadOnlyDictionary<SelectorKind, Selector> selectors,
        IApsGeneratorHudController controller)
    {
        ApsGenerationSettings settings = controller.Settings;
        DrawCard(area, "TETRIS OPTIONS");

        Label(new Rect(area.x + 9, area.y + 27, area.width - 18, 20), "Tetris", mutedStyle!);
        DrawSelectorButton(SelectorKind.Tetris, selectors[SelectorKind.Tetris], controller);

        Label(new Rect(area.x + 9, area.y + 77, area.width - 18, 20), "Symmetry", mutedStyle!);
        Rect symmetrySelector = SymmetrySelectorRect(area);
        DrawSpeedTier(new Rect(symmetrySelector.x, symmetrySelector.yMax + 2, symmetrySelector.width, 20), settings);
        DrawSelectorButton(SelectorKind.Symmetry, selectors[SelectorKind.Symmetry], controller);
        if (settings.SymmetryType != SymmetryType.None)
        {
            Rect modeRect = SymmetryModeRect(area);
            string mode = settings.SymmetryMode == SymmetryMode.Hard ? "Hard" : "Soft";
            DrawSettingButton(modeRect, new GUIContent(mode),
                selectedButtonStyle!, () => settings.SetSymmetryMode(
                settings.SymmetryMode == SymmetryMode.Hard ? SymmetryMode.Soft : SymmetryMode.Hard), controller);
        }
    }

    /// <summary>
    /// Relative solve speed of the current symmetry, under its dropdown. Speed follows the number
    /// of symmetric copies each placement fixes; soft symmetry searches like no symmetry.
    /// </summary>
    private void DrawSpeedTier(Rect rect, ApsGenerationSettings settings)
    {
        (string text, Color color) = settings.SymmetryMode == SymmetryMode.Soft
            ? ("Slowest", WarningColor)
            : settings.SymmetryType switch
            {
                SymmetryType.None => ("Slowest", WarningColor),
                SymmetryType.BothReflection or SymmetryType.Rotation90 => ("Fastest", FastColor),
                _ => ("Faster", FastColor)
            };
        speedStyle!.normal.textColor = color;
        Label(rect, text, speedStyle);
    }

    private void DrawPrefabCard(Rect area, IReadOnlyDictionary<SelectorKind, Selector> selectors, IApsGeneratorHudController controller)
    {
        ApsGenerationSettings settings = controller.Settings;
        DrawCard(area, "PREFAB");
        bool fiveClip = settings.TetrisType == TetrisType.FiveClip;
        DrawStepper(Row(area, 29, StepperRowHeight), new Stepper(
            fiveClip ? "Segments" : "Loader length",
            fiveClip ? $"{settings.ComponentLength / ApsGenerationSettings.FiveClipSectionHeight}" : $"{settings.ComponentLength}m",
            settings.AdjustComponentLength), controller, StepperLayout.Default);
        Label(new Rect(area.x + 9, area.y + 71, 40, SelectorHeight), "Layers", mutedStyle!);
        DrawSelectorButton(SelectorKind.Layers, selectors[SelectorKind.Layers], controller);
        DrawDensityRow(Row(area, 111, SelectorHeight), settings, controller);
    }

    private void DrawSelectorButton(
        SelectorKind selector,
        Selector definition,
        IApsGeneratorHudController controller)
    {
        bool previousEnabled = GUI.enabled;
        GUI.enabled = previousEnabled &&
                      (expandedSelector == SelectorKind.None || expandedSelector == selector);
        bool clicked = Button(
            definition.Anchor,
            new GUIContent(definition.Label + (expandedSelector == selector ? "  ▴" : "  ▾")),
            buttonStyle!);
        GUI.enabled = previousEnabled;
        if (!clicked)
            return;
        controller.ClaimHudInput();
        expandedSelector = expandedSelector == selector ? SelectorKind.None : selector;
    }

    private sealed record SelectorChoice(string Label, string Detail, bool Selected, bool Enabled, Action Select);
    private sealed record Selector(Rect Anchor, float DropdownWidth, IReadOnlyList<SelectorChoice> Choices)
    {
        /// <summary>Opens above the anchor when the list would run past the bottom of the panel.</summary>
        internal bool OpensUp { get; init; }
        internal string Label => Choices.First(choice => choice.Selected).Label;
        internal float ListHeight => Choices.Count * DropdownRowHeight;
        internal float FirstRowY => OpensUp ? Anchor.y - 1 - ListHeight : Anchor.yMax + 1;
        internal Rect Bounds => new(Anchor.x, OpensUp ? FirstRowY : Anchor.y, Mathf.Max(Anchor.width, DropdownWidth),
            Anchor.height + 1 + ListHeight);

        internal Selector FitWithin(float panelHeight) =>
            this with { OpensUp = Anchor.yMax + 1 + ListHeight > panelHeight - 2 };
    }

    private static Selector CreateSelector<T>(
        Rect anchor,
        IReadOnlyList<Choice<T>> choices,
        T selected,
        Action<T> select,
        Func<T, string>? detail = null,
        float dropdownWidth = 0)
        where T : notnull => new(anchor, dropdownWidth, choices.Select(choice => new SelectorChoice(
            choice.Label, detail?.Invoke(choice.Value) ?? "", EqualityComparer<T>.Default.Equals(choice.Value, selected),
            choice.Enabled, () => select(choice.Value))).ToArray());

    private static IReadOnlyList<Choice<SymmetryType>> SymmetryChoices(ApsGenerationSettings settings) =>
    [
        new Choice<SymmetryType>("None", SymmetryType.None),
        new Choice<SymmetryType>("Mirror", SymmetryType.VerticalReflection),
        new Choice<SymmetryType>("2x Mirror", SymmetryType.BothReflection),
        new Choice<SymmetryType>("180°", SymmetryType.Rotation180),
        new Choice<SymmetryType>("90°", SymmetryType.Rotation90, settings.Width == settings.Depth)
    ];

    private static IReadOnlyDictionary<SelectorKind, Selector> CreateSelectors(
        Rect[] columns, ApsGenerationSettings settings, float panelHeight) =>
        new Dictionary<SelectorKind, Selector>
        {
            [SelectorKind.Template] = CreateSelector(
                TemplateShapeRect(columns[0]),
                [new Choice<ApsTemplateShape>("Center hole", ApsTemplateShape.CircleCenterHole),
                 new Choice<ApsTemplateShape>("Circle", ApsTemplateShape.Circle),
                 new Choice<ApsTemplateShape>("Rectangle", ApsTemplateShape.Rectangle)],
                settings.TemplateShape, settings.SetTemplateShape),
            // Each option quotes the largest size it allows, which shows how much faster
            // symmetric solves are without needing a separate explanation.
            [SelectorKind.Symmetry] = CreateSelector(
                SymmetrySelectorRect(columns[1]),
                SymmetryChoices(settings),
                settings.SymmetryType, settings.SetSymmetryType,
                symmetry => settings.MaximumWidthFor(symmetry) >= ApsGenerationSettings.MaximumDimension
                    ? "no limit"
                    : $"up to {settings.MaximumWidthFor(symmetry)}",
                columns[1].width - 18),
            [SelectorKind.Tetris] = CreateSelector(
                TetrisSelectorRect(columns[1]),
                [new Choice<TetrisType>("3-clip", TetrisType.ThreeClip),
                 new Choice<TetrisType>("4-clip", TetrisType.FourClip),
                 new Choice<TetrisType>("5-clip", TetrisType.FiveClip)],
                settings.TetrisType, settings.SetTetrisType),
            [SelectorKind.Layers] = CreateSelector(
                new Rect(columns[2].x + 54, columns[2].y + 71, columns[2].width - 63, SelectorHeight),
                ExportExtraLayersExtensions.OptionsFor(settings.TetrisType)
                    .Select(layers => new Choice<ExportExtraLayers>(LayerLabel(layers), layers)).ToArray(),
                settings.ExtraLayers, settings.SetExtraLayers)
        }.ToDictionary(pair => pair.Key, pair => pair.Value.FitWithin(panelHeight));

    private void DrawExpandedSelector(IReadOnlyDictionary<SelectorKind, Selector> selectors, IApsGeneratorHudController controller)
    {
        if (!selectors.TryGetValue(expandedSelector, out Selector? selector)) return;
        Rect bounds = selector.Bounds;
        float firstY = selector.FirstRowY;
        GUI.DrawTexture(Scaled(new Rect(bounds.x, firstY, bounds.width, selector.ListHeight)), panelTexture!);
        for (int index = 0; index < selector.Choices.Count; index++)
        {
            SelectorChoice choice = selector.Choices[index];
            var rect = new Rect(bounds.x, firstY + index * DropdownRowHeight, bounds.width, DropdownRowHeight);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && choice.Enabled;
            GUIStyle style = choice.Selected ? selectedButtonStyle! : buttonStyle!;
            TextAnchor alignment = style.alignment;
            if (choice.Detail.Length > 0)
                style.alignment = TextAnchor.MiddleLeft;
            bool clicked = Button(rect, new GUIContent(choice.Label), style);
            style.alignment = alignment;
            if (choice.Detail.Length > 0)
                Label(new Rect(rect.x, rect.y, rect.width - 8, rect.height), choice.Detail, detailStyle!);
            if (clicked)
            {
                controller.ClaimHudInput();
                if (!choice.Selected) controller.ChangeSettings(choice.Select);
                expandedSelector = SelectorKind.None;
            }
            GUI.enabled = previousEnabled;
        }
    }

    private void DismissDropdownOnOutsideClick(IReadOnlyDictionary<SelectorKind, Selector> selectors, IApsGeneratorHudController controller)
    {
        Event current = Event.current;
        if (current.type != EventType.MouseDown || !selectors.TryGetValue(expandedSelector, out Selector? selector) ||
            Scaled(selector.Bounds).Contains(current.mousePosition)) return;
        expandedSelector = SelectorKind.None;
        controller.ClaimHudInput();
    }

    private void DrawSolveSettingsCard(Rect area, IApsGeneratorHudController controller)
    {
        ApsGenerationSettings settings = controller.Settings;
        DrawCard(area, "SOLVE SETTINGS");
        DrawStepper(Row(area, 29, StepperRowHeight),
            new Stepper("Time", $"{settings.MaxTimeSeconds}s", delta => settings.AdjustMaxTime(delta * 5)), controller, StepperLayout.Solver);
        DrawStepper(Row(area, 65, StepperRowHeight), new Stepper("Threads",
            $"{settings.MaxThreads}", settings.AdjustThreads,
            CanIncrease: settings.MaxThreads < settings.MaximumThreads), controller, StepperLayout.Solver);
        Label(new Rect(area.x + 9, area.y + 101, 74, SelectorHeight), "Early stop", mutedStyle!);
        DrawSegmented(
            InlineControlRect(area, 101, 68, 128, SelectorHeight),
            [new Choice<bool>("Off", false), new Choice<bool>("On", true)],
            settings.EarlyStopEnabled,
            settings.SetEarlyStop,
            controller);
    }

    /// <summary>
    /// Density slider. It maps the absolute pointer position on every event while the button is
    /// held, rather than relying on IMGUI drag deltas, which the game drops on slow drags.
    /// </summary>
    private void DrawDensityRow(Rect rect, ApsGenerationSettings settings, IApsGeneratorHudController controller)
    {
        const float labelWidth = 60;
        const float valueWidth = 40;
        const float thumbWidth = 10;
        Label(new Rect(rect.x, rect.y, labelWidth, rect.height), "Density", mutedStyle!);
        string value = settings.IsMaximizingDensity ? "Max" : $"{settings.TargetDensityPercent}%";
        Label(new Rect(rect.xMax - valueWidth, rect.y, valueWidth, rect.height), value, valueStyle!);

        var slider = new Rect(rect.x + labelWidth + 4, rect.y, rect.width - labelWidth - valueWidth - 10, rect.height);
        float travel = slider.width - thumbWidth;
        float fraction = Mathf.InverseLerp(ApsGenerationSettings.MinimumDensityPercent,
            ApsGenerationSettings.MaximumDensityPercent, settings.TargetDensityPercent);
        var track = new Rect(slider.x, slider.center.y - 2.5f, slider.width, 5);
        GUI.DrawTexture(Scaled(track), sliderTrack!);
        GUI.DrawTexture(Scaled(new Rect(track.x, track.y, thumbWidth / 2 + travel * fraction, track.height)), sliderFill!);
        bool enabled = GUI.enabled && expandedSelector == SelectorKind.None;
        var thumb = new Rect(slider.x + travel * fraction, slider.center.y - 9, thumbWidth, 18);
        GUI.DrawTexture(Scaled(thumb), draggingDensity ? hoverAccentTexture! : accentTexture!);

        Event current = Event.current;
        Rect hitArea = Scaled(new Rect(slider.x, slider.y - 2, slider.width, slider.height + 4));
        if (enabled && current.type == EventType.MouseDown && current.button == 0 && hitArea.Contains(current.mousePosition))
            draggingDensity = true;
        if (!draggingDensity)
            return;
        if (!enabled || !Input.GetMouseButton(0) || current.type == EventType.MouseUp)
        {
            draggingDensity = false;
            if (current.type == EventType.MouseUp)
                controller.ClaimHudInput();
            return;
        }

        float pointer = (current.mousePosition.x / scale - slider.x - thumbWidth / 2) / travel;
        float percent = Mathf.Lerp(ApsGenerationSettings.MinimumDensityPercent,
            ApsGenerationSettings.MaximumDensityPercent, Mathf.Clamp01(pointer));
        int snapped = Mathf.RoundToInt(percent / ApsGenerationSettings.DensityPercentStep) *
                      ApsGenerationSettings.DensityPercentStep;
        if (current.type is EventType.MouseDown or EventType.MouseDrag)
            controller.ClaimHudInput();
        if (snapped != settings.TargetDensityPercent)
            controller.ChangeSettings(() => settings.SetTargetDensityPercent(snapped));
    }

    private void DrawDimensionPair(Rect rect, ApsGenerationSettings settings, IApsGeneratorHudController controller,
        ApsSolveSize size)
    {
        Color? valueColor = size switch
        {
            ApsSolveSize.TooLarge => DangerColor,
            ApsSolveSize.Large => WarningColor,
            _ => null
        };
        float controlsX = rect.xMax - CompactStepperWidth * 2 - CompactControlGap;
        DrawCompactStepper(new Rect(controlsX, rect.y, CompactStepperWidth, rect.height), settings.Width,
            settings.AdjustWidth, true, controller, settings.CanIncreaseWidth, valueColor);
        DrawCompactStepper(new Rect(controlsX + CompactStepperWidth + CompactControlGap, rect.y,
                CompactStepperWidth, rect.height),
            settings.Depth, settings.AdjustDepth, !settings.IsDepthLocked, controller, settings.CanIncreaseDepth, valueColor);
    }

    private void DrawHoleSizeRow(
        Rect rect,
        ApsGenerationSettings settings,
        IApsGeneratorHudController controller)
    {
        const float holeLabelWidth = 29;
        const float subLabelWidth = 36;
        const float holeShapeWidth = 25;
        const float subControlGap = 4;
        Label(new Rect(rect.x, rect.y, holeLabelWidth, rect.height), "Hole", mutedStyle!);
        var shapeRect = new Rect(rect.xMax - holeShapeWidth, rect.y + 3,
            holeShapeWidth, rect.height - 6);
        var shapeLabelRect = new Rect(shapeRect.x - subControlGap - subLabelWidth, rect.y,
            subLabelWidth, rect.height);
        var controlRect = new Rect(shapeLabelRect.x - 5 - CompactStepperWidth, rect.y,
            CompactStepperWidth, rect.height);
        var sizeLabelRect = new Rect(controlRect.x - 3 - 24, rect.y, 24, rect.height);
        Label(sizeLabelRect, "size", mutedStyle!);
        Label(shapeLabelRect, "shape", mutedStyle!);
        string icon = settings.HoleShape == CenterHoleShape.Circle ? "○" : "□";
        DrawSettingButton(
            shapeRect,
            new GUIContent(icon, "Toggle circular/square center hole"),
            buttonStyle!,
            () => settings.SetHoleShape(
                settings.HoleShape == CenterHoleShape.Circle ? CenterHoleShape.Square : CenterHoleShape.Circle),
            controller);
        DrawCompactStepper(controlRect, settings.HoleSize, settings.AdjustHoleSize, true, controller);
    }

    private void DrawCompactStepper(
        Rect rect,
        int value,
        Action<int> adjust,
        bool enabled,
        IApsGeneratorHudController controller,
        bool canIncrease = true,
        Color? valueColor = null)
    {
        const float buttonWidth = 25;
        float valueWidth = rect.width - buttonWidth * 2 - StepperControlGap * 2;
        bool previousEnabled = GUI.enabled;
        bool controlsEnabled = previousEnabled && expandedSelector == SelectorKind.None && enabled;
        GUI.enabled = controlsEnabled;
        if (Button(new Rect(rect.x, rect.y + 3, buttonWidth, rect.height - 6), new GUIContent("−"), buttonStyle!))
            ChangeSetting(() => adjust(-1), controller);
        float valueX = rect.x + buttonWidth + StepperControlGap;
        Color previousColor = valueStyle!.normal.textColor;
        if (valueColor is Color color)
            valueStyle.normal.textColor = color;
        Label(new Rect(valueX, rect.y + 3, valueWidth, rect.height - 6), $"{value}", valueStyle);
        valueStyle.normal.textColor = previousColor;
        GUI.enabled = controlsEnabled && canIncrease;
        if (Button(new Rect(valueX + valueWidth + StepperControlGap, rect.y + 3,
                buttonWidth, rect.height - 6),
                new GUIContent("+"), buttonStyle!))
            ChangeSetting(() => adjust(1), controller);
        GUI.enabled = previousEnabled;
    }

    private void DrawCard(Rect area, string title)
    {
        GUI.Box(Scaled(area), GUIContent.none, cardStyle!);
        Label(new Rect(area.x + 9, area.y + 3, area.width - 18, 24), title, titleStyle!);
    }

    private void DrawSegmented<T>(
        Rect rect,
        IReadOnlyList<Choice<T>> choices,
        T selected,
        Action<T> select,
        IApsGeneratorHudController controller)
        where T : notnull
    {
        float width = (rect.width - (choices.Count - 1) * 3) / choices.Count;
        bool previousEnabled = GUI.enabled;
        for (int index = 0; index < choices.Count; index++)
        {
            Choice<T> choice = choices[index];
            GUI.enabled = previousEnabled && expandedSelector == SelectorKind.None && choice.Enabled;
            bool isSelected = EqualityComparer<T>.Default.Equals(choice.Value, selected);
            if (Button(
                    new Rect(rect.x + index * (width + 3), rect.y, width, rect.height),
                    new GUIContent(choice.Label),
                    isSelected ? selectedButtonStyle! : buttonStyle!) && !isSelected)
                ChangeSetting(() => select(choice.Value), controller);
        }
        GUI.enabled = previousEnabled;
    }

    private sealed record Stepper(
        string Label,
        string Value,
        Action<int> Adjust,
        bool Enabled = true,
        bool CanDecrease = true,
        bool CanIncrease = true);
    private sealed record StepperLayout(float ValueWidth, float? ControlsOffset)
    {
        internal static readonly StepperLayout Default = new(34, null);
        internal static readonly StepperLayout Solver = new(44, 68);
    }

    private void DrawStepper(Rect rect, Stepper stepper, IApsGeneratorHudController controller, StepperLayout layout)
    {
        float controlsWidth = StepperButtonWidth * 2 + layout.ValueWidth + StepperControlGap * 2;
        float controlsX = layout.ControlsOffset.HasValue
            ? Mathf.Min(rect.x + layout.ControlsOffset.Value, rect.xMax - controlsWidth)
            : rect.xMax - controlsWidth;
        Label(new Rect(rect.x, rect.y, Mathf.Max(0, controlsX - rect.x - 4), rect.height), stepper.Label, mutedStyle!);
        bool previousEnabled = GUI.enabled;
        bool controlsEnabled = previousEnabled && expandedSelector == SelectorKind.None && stepper.Enabled;
        GUI.enabled = controlsEnabled && stepper.CanDecrease;
        if (Button(new Rect(controlsX, rect.y + 3, StepperButtonWidth, rect.height - 6), new GUIContent("−"), buttonStyle!))
            ChangeSetting(() => stepper.Adjust(-1), controller);
        GUI.enabled = controlsEnabled;
        Label(new Rect(controlsX + StepperButtonWidth + StepperControlGap, rect.y + 3,
            layout.ValueWidth, rect.height - 6), stepper.Value, valueStyle!);
        GUI.enabled = controlsEnabled && stepper.CanIncrease;
        if (Button(new Rect(controlsX + controlsWidth - StepperButtonWidth, rect.y + 3,
            StepperButtonWidth, rect.height - 6), new GUIContent("+"), buttonStyle!))
            ChangeSetting(() => stepper.Adjust(1), controller);
        GUI.enabled = previousEnabled;
    }

    private void DrawButton(Rect rect, GUIContent content, Action action, IApsGeneratorHudController controller, GUIStyle style)
    {
        if (!Button(rect, content, style))
            return;
        controller.ClaimHudInput();
        action();
    }

    private void DrawSettingButton(
        Rect rect,
        GUIContent content,
        GUIStyle style,
        Action change,
        IApsGeneratorHudController controller)
    {
        bool previousEnabled = GUI.enabled;
        GUI.enabled = previousEnabled && expandedSelector == SelectorKind.None;
        bool clicked = Button(rect, content, style);
        GUI.enabled = previousEnabled;
        if (clicked)
            ChangeSetting(change, controller);
    }

    private static void ChangeSetting(Action change, IApsGeneratorHudController controller)
    {
        controller.ClaimHudInput();
        controller.ChangeSettings(change);
    }

    /// <summary>Scales to pixels and snaps each edge, so adjacent controls share edges without gaps.</summary>
    private Rect Scaled(Rect logical) => Rect.MinMaxRect(
        Mathf.Round(logical.xMin * scale), Mathf.Round(logical.yMin * scale),
        Mathf.Round(logical.xMax * scale), Mathf.Round(logical.yMax * scale));

    private bool Button(Rect rect, GUIContent content, GUIStyle style) => GUI.Button(Scaled(rect), content, style);

    private void Label(Rect rect, string text, GUIStyle style) => GUI.Label(Scaled(rect), text, style);

    private void Label(Rect rect, GUIContent content, GUIStyle style) => GUI.Label(Scaled(rect), content, style);


    private static Rect[] CalculateColumns(float panelWidth, float bodyTop)
    {
        float contentWidth = panelWidth - Padding * 2 - Gap * 3;
        float desiredWidth = TemplateCardWidth + TetrisOptionsCardWidth + PrefabCardWidth + SolveSettingsCardWidth;
        float columnScale = Mathf.Min(1, contentWidth / desiredWidth);
        float tetrisOptionsWidth = TetrisOptionsCardWidth * columnScale;
        float prefabWidth = PrefabCardWidth * columnScale;
        float solveSettingsWidth = SolveSettingsCardWidth * columnScale;
        float templateWidth = contentWidth - tetrisOptionsWidth - prefabWidth - solveSettingsWidth;
        float x = Padding;
        return
        [
            new Rect(x, bodyTop, templateWidth, BodyHeight),
            new Rect(x += templateWidth + Gap, bodyTop, tetrisOptionsWidth, BodyHeight),
            new Rect(x += tetrisOptionsWidth + Gap, bodyTop, prefabWidth, BodyHeight),
            new Rect(x + prefabWidth + Gap, bodyTop, solveSettingsWidth, BodyHeight)
        ];
    }

    private static Rect TemplateShapeRect(Rect card)
    {
        float width = CompactStepperWidth * 2 + CompactControlGap;
        return new Rect(card.xMax - width - 9, card.y + 29, width, SelectorHeight);
    }

    private static Rect TetrisSelectorRect(Rect card) =>
        new(card.x + 9, card.y + 47, card.width - 18 - SymmetryModeWidth - 6, SelectorHeight);

    private static Rect SymmetrySelectorRect(Rect card) =>
        new(card.x + 9, card.y + 97, card.width - 18 - SymmetryModeWidth - 6, SelectorHeight);

    private static Rect SymmetryModeRect(Rect card) =>
        new(card.xMax - 9 - SymmetryModeWidth, card.y + 97, SymmetryModeWidth, SelectorHeight);

    private static Rect CenteredAfterLabelRect(
        Rect card,
        float y,
        float labelWidth,
        float width,
        float height)
    {
        float left = card.x + 9 + labelWidth;
        float right = card.xMax - 9;
        return new Rect(left + (right - left - width) * 0.5f, card.y + y, width, height);
    }

    private static Rect InlineControlRect(
        Rect card,
        float y,
        float controlsOffset,
        float width,
        float height)
    {
        float availableWidth = card.width - controlsOffset - 18;
        return new Rect(
            card.x + 9 + controlsOffset,
            card.y + y,
            Mathf.Max(0, Mathf.Min(width, availableWidth)),
            height);
    }

    private static string LayerLabel(ExportExtraLayers layers) => layers switch
    {
        ExportExtraLayers.EjectorsIntakesCoolerSnake => "Complete APS",
        ExportExtraLayers.EjectorsIntakes => "Ejectors + intakes",
        ExportExtraLayers.IntakesCoolerSnake => "Intakes + cooler",
        ExportExtraLayers.IntakesOnly => "Intakes only",
        _ => "Tetris only"
    };

    private static Rect Row(Rect card, float y, float height) => new(card.x + 9, card.y + y, card.width - 18, height);

    private static float CalculateScale(int hudScalePercent)
    {
        float desired = ResolutionScale * hudScalePercent / 100f;
        float fitsWidth = (Screen.width - 2 * PanelMargin) / MinimumLogicalWidth;
        return Mathf.Clamp(Mathf.Min(desired, fitsWidth), 0.5f, MaximumScale);
    }

    private Rect CalculatePanelRect(float logicalHeight)
    {
        float width = Mathf.Min(Screen.width - 2 * PanelMargin, MaximumPanelWidth * scale);
        float height = Mathf.Min(logicalHeight * scale, Screen.height - 2 * PanelMargin);
        return new Rect((Screen.width - width) / 2, Screen.height - height - PanelMargin, width, height);
    }

    private void EnsureStyles()
    {
        if (panelStyle is null)
            CreateTextures();
        if (Mathf.Approximately(stylesScale, scale))
            return;
        stylesScale = scale;

        panelStyle = CreateBoxStyle(panelTexture!);
        cardStyle = CreateBoxStyle(cardTexture!);
        buttonStyle = CreateButtonStyle(buttonTexture!, hoverTexture!, accentTexture!, Color.white);
        selectedButtonStyle = CreateButtonStyle(selectedTexture!, accentTexture!, accentTexture!, Color.white);
        primaryButtonStyle = CreateButtonStyle(accentTexture!, selectedTexture!, selectedTexture!, Color.black, 14, FontStyle.Bold);
        titleStyle = CreateLabelStyle(15, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        valueStyle = CreateLabelStyle(14, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        valueStyle.padding = new RectOffset(0, 0, 0, 0);
        bodyStyle = CreateLabelStyle(13, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white);
        mutedStyle = CreateLabelStyle(12, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.68f, 0.78f, 0.82f));
        speedStyle = CreateLabelStyle(12, FontStyle.Italic, TextAnchor.MiddleCenter, FastColor);
        detailStyle = CreateLabelStyle(11, FontStyle.Normal, TextAnchor.MiddleRight, new Color(0.80f, 0.88f, 0.90f));
        chipStyle = CreateLabelStyle(11, FontStyle.Bold, TextAnchor.MiddleCenter, Color.black);
        chipStyle.padding = new RectOffset(0, 0, 0, 0);
    }

    private Texture2D? panelTexture;
    private Texture2D? cardTexture;
    private Texture2D? buttonTexture;
    private Texture2D? hoverTexture;
    private Texture2D? accentTexture;
    private Texture2D? hoverAccentTexture;
    private Texture2D? selectedTexture;

    private void CreateTextures()
    {
        panelTexture = CreateTexture(new Color(0.025f, 0.035f, 0.045f, 0.97f));
        cardTexture = CreateTexture(new Color(0.065f, 0.085f, 0.10f, 1f));
        buttonTexture = CreateTexture(new Color(0.13f, 0.17f, 0.20f, 1f));
        hoverTexture = CreateTexture(new Color(0.17f, 0.28f, 0.32f, 1f));
        accentTexture = CreateTexture(new Color(0.10f, 0.72f, 0.80f, 1f));
        hoverAccentTexture = CreateTexture(new Color(0.45f, 0.88f, 0.94f, 1f));
        selectedTexture = CreateTexture(new Color(0.08f, 0.52f, 0.60f, 1f));
        warningChip = CreateTexture(WarningColor);
        dangerChip = CreateTexture(DangerColor);
        zoomOutIcon = CreateMagnifierIcon(plus: false);
        zoomInIcon = CreateMagnifierIcon(plus: true);
        sliderTrack = buttonTexture;
        sliderFill = selectedTexture;
    }

    private void DrawSpinner(Rect area, float elapsedSeconds)
    {
        const int dotCount = 10;
        Vector2 center = area.center;
        float radius = Mathf.Min(area.width, area.height) * 0.36f;
        int leadingDot = Mathf.FloorToInt(elapsedSeconds * 8f) % dotCount;
        Color previous = GUI.color;
        for (int index = 0; index < dotCount; index++)
        {
            float radians = index * Mathf.PI * 2f / dotCount;
            float distance = (leadingDot - index + dotCount) % dotCount;
            GUI.color = new Color(0.10f, 0.72f, 0.80f, Mathf.Lerp(0.18f, 1f, 1f - distance / dotCount));
            GUI.DrawTexture(
                Scaled(new Rect(
                    center.x + Mathf.Cos(radians) * radius - 2,
                    center.y + Mathf.Sin(radians) * radius - 2,
                    4,
                    4)),
                Texture2D.whiteTexture);
        }
        GUI.color = previous;
    }

    private static GUIStyle CreateBoxStyle(Texture2D background)
    {
        var style = new GUIStyle(GUI.skin.box);
        style.normal.background = background;
        return style;
    }

    private GUIStyle CreateButtonStyle(Texture2D normal, Texture2D hover, Texture2D active, Color textColor,
        int fontSize = 13, FontStyle fontStyle = FontStyle.Normal)
    {
        var style = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = ScaledFont(fontSize),
            fontStyle = fontStyle,
            padding = ScaledOffset(new RectOffset(6, 6, 2, 2)),
            clipping = TextClipping.Clip
        };
        SetButtonState(style.normal, normal, textColor);
        SetButtonState(style.hover, hover, textColor);
        SetButtonState(style.active, active, Color.black);
        SetButtonState(style.focused, hover, textColor);
        return style;
    }

    private GUIStyle CreateLabelStyle(int fontSize, FontStyle fontStyle, TextAnchor alignment, Color color)
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = ScaledFont(fontSize),
            fontStyle = fontStyle,
            alignment = alignment,
            wordWrap = false,
            padding = ScaledOffset(GUI.skin.label.padding)
        };
        style.normal.textColor = color;
        return style;
    }

    private int ScaledFont(int size) => Mathf.Max(8, Mathf.RoundToInt(size * scale));

    private RectOffset ScaledOffset(RectOffset offset) => new(
        Mathf.RoundToInt(offset.left * scale), Mathf.RoundToInt(offset.right * scale),
        Mathf.RoundToInt(offset.top * scale), Mathf.RoundToInt(offset.bottom * scale));

    private static void SetButtonState(GUIStyleState state, Texture2D background, Color textColor)
    {
        state.background = background;
        state.textColor = textColor;
    }

    /// <summary>Anti-aliased white magnifying glass with a minus or plus in the lens.</summary>
    private Texture2D CreateMagnifierIcon(bool plus)
    {
        const int size = 64;
        var lens = new Vector2(26, 26);
        const float lensRadius = 18, ringHalfWidth = 3.5f, handleHalfWidth = 5, signHalfWidth = 3, signHalfLength = 10;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
        { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Trilinear };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var point = new Vector2(x + 0.5f, y + 0.5f);
                float ring = Mathf.Abs(Vector2.Distance(point, lens) - lensRadius) - ringHalfWidth;
                float handle = SegmentDistance(point, new Vector2(40, 40), new Vector2(58, 58)) - handleHalfWidth;
                float minus = BoxDistance(point - lens, signHalfLength, signHalfWidth);
                float sign = plus ? Mathf.Min(minus, BoxDistance(point - lens, signHalfWidth, signHalfLength)) : minus;
                float distance = Mathf.Min(ring, Mathf.Min(handle, sign));
                // Texture rows run bottom-up; the icon is drawn top-down.
                pixels[(size - 1 - y) * size + x] = new Color(1, 1, 1, Mathf.Clamp01(0.5f - distance));
            }
        texture.SetPixels(pixels);
        texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
        textures.Add(texture);
        return texture;
    }

    private static float SegmentDistance(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / segment.sqrMagnitude);
        return Vector2.Distance(point, start + segment * t);
    }

    private static float BoxDistance(Vector2 offset, float halfWidth, float halfHeight)
    {
        var outside = new Vector2(Mathf.Max(Mathf.Abs(offset.x) - halfWidth, 0), Mathf.Max(Mathf.Abs(offset.y) - halfHeight, 0));
        return outside.magnitude + Mathf.Min(Mathf.Max(Mathf.Abs(offset.x) - halfWidth, Mathf.Abs(offset.y) - halfHeight), 0);
    }

    private Texture2D CreateTexture(Color color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixel(0, 0, color);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        textures.Add(texture);
        return texture;
    }

    private readonly struct Choice<T>
    {
        internal Choice(string label, T value, bool enabled = true)
        {
            Label = label;
            Value = value;
            Enabled = enabled;
        }

        internal string Label { get; }
        internal T Value { get; }
        internal bool Enabled { get; }
    }

    private enum SelectorKind
    {
        None,
        Template,
        Symmetry,
        Tetris,
        Layers
    }

    private readonly struct GuiState
    {
        private GuiState(Color color, Color contentColor, Color backgroundColor, bool enabled, int depth)
        {
            Color = color;
            ContentColor = contentColor;
            BackgroundColor = backgroundColor;
            Enabled = enabled;
            Depth = depth;
        }

        private Color Color { get; }
        private Color ContentColor { get; }
        private Color BackgroundColor { get; }
        private bool Enabled { get; }
        private int Depth { get; }

        internal static GuiState Capture() => new(GUI.color, GUI.contentColor, GUI.backgroundColor, GUI.enabled, GUI.depth);

        internal static void ApplyForPanel()
        {
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            GUI.depth = -1000;
        }

        internal void Restore()
        {
            GUI.color = Color;
            GUI.contentColor = ContentColor;
            GUI.backgroundColor = BackgroundColor;
            GUI.enabled = Enabled;
            GUI.depth = Depth;
        }
    }
}
