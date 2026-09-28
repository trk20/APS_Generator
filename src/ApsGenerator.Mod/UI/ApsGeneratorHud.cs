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
    bool HasActivePrefab { get; }
    float SolveElapsedSeconds { get; }
    void Generate();
    void Cancel();
    void HideHud();
    void ResetDefaults();
    void Save();
    void Clear();
    void ChangeSettings(Action change);
    void ClaimHudInput();
}

internal sealed class ApsGeneratorHud : IDisposable
{
    private const float PanelHeight = 248;
    private const float MaximumPanelWidth = 844;
    private const float PanelMargin = 12;
    private const float Padding = 12;
    private const float Gap = 8;
    private const float BodyTop = 43;
    private const float BodyHeight = 150;
    private const float SelectorHeight = 26;
    private const float StepperRowHeight = 28;
    private const float StepperButtonWidth = 27;
    private const float StepperControlGap = 1;
    private const float DropdownRowHeight = 19;
    private const float CompactStepperWidth = 82;
    private const float CompactControlGap = 16;
    private const float TemplateCardWidth = 248;
    private const float TetrisOptionsCardWidth = 174;
    private const float PrefabCardWidth = 200;
    private const float SolveSettingsCardWidth = 174;

    private readonly List<Texture2D> textures = [];
    private SelectorKind expandedSelector;
    private GUIStyle? panelStyle;
    private GUIStyle? cardStyle;
    private GUIStyle? buttonStyle;
    private GUIStyle? selectedButtonStyle;
    private GUIStyle? primaryButtonStyle;
    private GUIStyle? titleStyle;
    private GUIStyle? valueStyle;
    private GUIStyle? bodyStyle;
    private GUIStyle? mutedStyle;
    private GUIStyle? warningStyle;

    internal Rect Draw(IApsGeneratorHudController controller)
    {
        EnsureStyles();
        Rect rect = CalculatePanelRect();
        GuiState previous = GuiState.Capture();
        try
        {
            GuiState.ApplyForPanel();
            GUI.Box(rect, GUIContent.none, panelStyle!);
            GUI.BeginGroup(rect);
            try { DrawPanel(controller, rect.width); }
            finally { GUI.EndGroup(); }
        }
        finally
        {
            previous.Restore();
        }
        return rect;
    }

    public void Dispose()
    {
        foreach (Texture2D texture in textures)
            UnityEngine.Object.Destroy(texture);
        textures.Clear();
    }

    private void DrawPanel(IApsGeneratorHudController controller, float width)
    {
        Rect[] columns = CalculateColumns(width);
        var selectors = CreateSelectors(columns, controller.Settings);
        DismissDropdownOnOutsideClick(selectors, controller);
        DrawHeader(controller, width);

        bool previousEnabled = GUI.enabled;
        DrawTemplateCard(columns[0], selectors, controller);
        DrawTetrisOptionsCard(columns[1], selectors, controller);
        DrawPrefabCard(columns[2], selectors, controller);
        DrawSolveSettingsCard(columns[3], controller);

        const float generateWidth = 190;
        const float utilityWidth = 64;
        float buttonGroupWidth = controller.HasActivePrefab
            ? generateWidth + Gap + utilityWidth + Gap + utilityWidth
            : generateWidth;
        float buttonX = (width - buttonGroupWidth) / 2;
        GUI.enabled = previousEnabled && expandedSelector == SelectorKind.None;
        DrawButton(
            new Rect(buttonX, BodyTop + BodyHeight + 6, generateWidth, 34),
            controller.IsSolving ? "CANCEL" : controller.HasCurrentSolution ? "REGENERATE" : "GENERATE",
            controller.IsSolving ? controller.Cancel : controller.Generate,
            controller,
            primaryButtonStyle!);
        if (controller.HasActivePrefab)
        {
            DrawButton(
                new Rect(buttonX + generateWidth + Gap, BodyTop + BodyHeight + 6, utilityWidth, 34),
                "SAVE",
                controller.Save,
                controller,
                buttonStyle!);
            DrawButton(
                new Rect(buttonX + generateWidth + Gap * 2 + utilityWidth, BodyTop + BodyHeight + 6, utilityWidth, 34),
                "CLEAR",
                controller.Clear,
                controller,
                buttonStyle!);
        }
        GUI.enabled = previousEnabled;
        DrawExpandedSelector(selectors, controller);
    }

    private void DrawHeader(IApsGeneratorHudController controller, float width)
    {
        const float closeWidth = 42;
        const float defaultsWidth = 76;
        float closeX = width - Padding - closeWidth;
        float defaultsX = closeX - Gap - defaultsWidth;
        GUI.Label(new Rect(Padding, 3, 170, 36), "APS GENERATOR", titleStyle!);
        float statusX = 164;
        if (controller.IsSolving)
        {
            DrawSpinner(new Rect(statusX, 9, 22, 22), controller.SolveElapsedSeconds);
            statusX += 28;
        }
        string status = controller.IsSolving
            ? $"{controller.Status} · {controller.SolveElapsedSeconds:0.0}s"
            : controller.Status;
        GUI.Label(new Rect(statusX, 3, defaultsX - statusX - Gap, 36), status, bodyStyle!);
        DrawButton(new Rect(defaultsX, 5, defaultsWidth, 30), "Defaults", controller.ResetDefaults, controller, buttonStyle!);
        DrawButton(new Rect(closeX, 5, closeWidth, 30), "×", controller.HideHud, controller, buttonStyle!);
    }

    private void DrawTemplateCard(Rect area, IReadOnlyDictionary<SelectorKind, Selector> selectors, IApsGeneratorHudController controller)
    {
        ApsGenerationSettings settings = controller.Settings;
        DrawCard(area, "TEMPLATE");
        GUI.Label(new Rect(area.x + 9, area.y + 29, 46, SelectorHeight), "Shape", mutedStyle!);
        DrawSelectorButton(SelectorKind.Template, selectors[SelectorKind.Template], controller);

        DrawDimensionPair(Row(area, 57, StepperRowHeight), settings, controller);
        float evenSizesY = 85;
        if (settings.TemplateShape == ApsTemplateShape.CircleCenterHole)
        {
            DrawHoleSizeRow(Row(area, 85, StepperRowHeight), settings, controller);
            evenSizesY = 113;
        }
        Rect evenSizesRect = CenteredAfterLabelRect(area, evenSizesY, 72, 92, SelectorHeight);
        GUI.Label(new Rect(area.x + 9, area.y + evenSizesY, 72, SelectorHeight), "Even sizes", mutedStyle!);
        DrawSegmented(
            evenSizesRect,
            [new Choice<bool>("Off", false), new Choice<bool>("On", true)],
            settings.AllowEvenDimensions,
            settings.SetAllowEvenDimensions,
            controller);
        if (settings.IsUnusuallyLarge)
            GUI.Label(
                new Rect(area.xMax - 99, area.y + 3, 90, 22),
                "Large Solve",
                warningStyle!);
    }

    private void DrawTetrisOptionsCard(
        Rect area,
        IReadOnlyDictionary<SelectorKind, Selector> selectors,
        IApsGeneratorHudController controller)
    {
        ApsGenerationSettings settings = controller.Settings;
        DrawCard(area, "TETRIS OPTIONS");

        GUI.Label(new Rect(area.x + 9, area.y + 29, 54, SelectorHeight), "Tetris", mutedStyle!);
        DrawSelectorButton(SelectorKind.Tetris, selectors[SelectorKind.Tetris], controller);

        GUI.Label(new Rect(area.x + 9, area.y + 58, area.width - 18, 22), "Symmetry", mutedStyle!);
        DrawSelectorButton(SelectorKind.Symmetry, selectors[SelectorKind.Symmetry], controller);
        if (settings.SymmetryType != SymmetryType.None)
        {
            Rect modeRect = new(area.xMax - 67, area.y + 82, 58, SelectorHeight);
            string mode = settings.SymmetryMode == SymmetryMode.Hard ? "Hard" : "Soft";
            DrawSettingButton(modeRect, new GUIContent(mode), selectedButtonStyle!, () => settings.SetSymmetryMode(
                settings.SymmetryMode == SymmetryMode.Hard ? SymmetryMode.Soft : SymmetryMode.Hard), controller);
        }
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
        GUI.Label(new Rect(area.x + 9, area.y + 71, 40, SelectorHeight), "Layers", mutedStyle!);
        DrawSelectorButton(SelectorKind.Layers, selectors[SelectorKind.Layers], controller);
    }

    private void DrawSelectorButton(
        SelectorKind selector,
        Selector definition,
        IApsGeneratorHudController controller)
    {
        bool previousEnabled = GUI.enabled;
        GUI.enabled = previousEnabled &&
                      (expandedSelector == SelectorKind.None || expandedSelector == selector);
        bool clicked = GUI.Button(
            definition.Anchor,
            definition.Label + (expandedSelector == selector ? "  ▴" : "  ▾"),
            buttonStyle!);
        GUI.enabled = previousEnabled;
        if (!clicked)
            return;
        controller.ClaimHudInput();
        expandedSelector = expandedSelector == selector ? SelectorKind.None : selector;
    }

    private sealed record SelectorChoice(string Label, bool Selected, bool Enabled, Action Select);
    private sealed record Selector(Rect Anchor, IReadOnlyList<SelectorChoice> Choices)
    {
        internal string Label => Choices.First(choice => choice.Selected).Label;
        internal Rect Bounds => new(Anchor.x, Anchor.y, Anchor.width,
            Anchor.height + 1 + Choices.Count * DropdownRowHeight);
    }

    private static Selector CreateSelector<T>(
        Rect anchor,
        IReadOnlyList<Choice<T>> choices,
        T selected,
        Action<T> select)
        where T : notnull => new(anchor, choices.Select(choice => new SelectorChoice(
            choice.Label, EqualityComparer<T>.Default.Equals(choice.Value, selected), choice.Enabled,
            () => select(choice.Value))).ToArray());

    private static IReadOnlyDictionary<SelectorKind, Selector> CreateSelectors(Rect[] columns, ApsGenerationSettings settings) =>
        new Dictionary<SelectorKind, Selector>
        {
            [SelectorKind.Template] = CreateSelector(
                TemplateShapeRect(columns[0]),
                [new Choice<ApsTemplateShape>("Center hole", ApsTemplateShape.CircleCenterHole),
                 new Choice<ApsTemplateShape>("Circle", ApsTemplateShape.Circle),
                 new Choice<ApsTemplateShape>("Rectangle", ApsTemplateShape.Rectangle)],
                settings.TemplateShape, settings.SetTemplateShape),
            [SelectorKind.Symmetry] = CreateSelector(
                SymmetrySelectorRect(columns[1]),
                [new Choice<SymmetryType>("None", SymmetryType.None),
                 new Choice<SymmetryType>("Mirror", SymmetryType.VerticalReflection),
                 new Choice<SymmetryType>("2x Mirror", SymmetryType.BothReflection),
                 new Choice<SymmetryType>("180°", SymmetryType.Rotation180),
                 new Choice<SymmetryType>("90°", SymmetryType.Rotation90, settings.Width == settings.Depth)],
                settings.SymmetryType, settings.SetSymmetryType),
            [SelectorKind.Tetris] = CreateSelector(
                new Rect(columns[1].xMax - 99, columns[1].y + 29, 90, SelectorHeight),
                [new Choice<TetrisType>("3-clip", TetrisType.ThreeClip),
                 new Choice<TetrisType>("4-clip", TetrisType.FourClip),
                 new Choice<TetrisType>("5-clip", TetrisType.FiveClip)],
                settings.TetrisType, settings.SetTetrisType),
            [SelectorKind.Layers] = CreateSelector(
                new Rect(columns[2].x + 54, columns[2].y + 71, columns[2].width - 63, SelectorHeight),
                ExportExtraLayersExtensions.OptionsFor(settings.TetrisType)
                    .Select(layers => new Choice<ExportExtraLayers>(LayerLabel(layers), layers)).ToArray(),
                settings.ExtraLayers, settings.SetExtraLayers)
        };

    private void DrawExpandedSelector(IReadOnlyDictionary<SelectorKind, Selector> selectors, IApsGeneratorHudController controller)
    {
        if (!selectors.TryGetValue(expandedSelector, out Selector? selector)) return;
        Rect anchor = selector.Anchor;
        float firstY = anchor.yMax + 1;
        for (int index = 0; index < selector.Choices.Count; index++)
        {
            SelectorChoice choice = selector.Choices[index];
            var rect = new Rect(anchor.x, firstY + index * DropdownRowHeight, anchor.width, DropdownRowHeight);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && choice.Enabled;
            if (GUI.Button(rect, choice.Label, choice.Selected ? selectedButtonStyle! : buttonStyle!))
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
            selector.Bounds.Contains(current.mousePosition)) return;
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
        GUI.Label(new Rect(area.x + 9, area.y + 101, 74, SelectorHeight), "Early stop", mutedStyle!);
        DrawSegmented(
            InlineControlRect(area, 101, 68, 128, SelectorHeight),
            [new Choice<bool>("Off", false), new Choice<bool>("On", true)],
            settings.EarlyStopEnabled,
            settings.SetEarlyStop,
            controller);
    }

    private void DrawDimensionPair(Rect rect, ApsGenerationSettings settings, IApsGeneratorHudController controller)
    {
        float controlsX = rect.xMax - CompactStepperWidth * 2 - CompactControlGap;
        DrawCompactStepper(new Rect(controlsX, rect.y, CompactStepperWidth, rect.height), settings.Width,
            settings.AdjustWidth, true, controller);
        DrawCompactStepper(new Rect(controlsX + CompactStepperWidth + CompactControlGap, rect.y,
                CompactStepperWidth, rect.height),
            settings.Depth, settings.AdjustDepth, !settings.IsDepthLocked, controller);
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
        GUI.Label(new Rect(rect.x, rect.y, holeLabelWidth, rect.height), "Hole", mutedStyle!);
        var shapeRect = new Rect(rect.xMax - holeShapeWidth, rect.y + 3,
            holeShapeWidth, rect.height - 6);
        var shapeLabelRect = new Rect(shapeRect.x - subControlGap - subLabelWidth, rect.y,
            subLabelWidth, rect.height);
        var controlRect = new Rect(shapeLabelRect.x - 5 - CompactStepperWidth, rect.y,
            CompactStepperWidth, rect.height);
        var sizeLabelRect = new Rect(controlRect.x - 3 - 24, rect.y, 24, rect.height);
        GUI.Label(sizeLabelRect, "size", mutedStyle!);
        GUI.Label(shapeLabelRect, "shape", mutedStyle!);
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
        IApsGeneratorHudController controller)
    {
        const float buttonWidth = 25;
        float valueWidth = rect.width - buttonWidth * 2 - StepperControlGap * 2;
        bool previousEnabled = GUI.enabled;
        GUI.enabled = previousEnabled && expandedSelector == SelectorKind.None && enabled;
        if (GUI.Button(new Rect(rect.x, rect.y + 3, buttonWidth, rect.height - 6), "−", buttonStyle!))
            ChangeSetting(() => adjust(-1), controller);
        float valueX = rect.x + buttonWidth + StepperControlGap;
        GUI.Label(new Rect(valueX, rect.y + 3, valueWidth, rect.height - 6), $"{value}", valueStyle!);
        if (GUI.Button(new Rect(valueX + valueWidth + StepperControlGap, rect.y + 3,
                buttonWidth, rect.height - 6), "+", buttonStyle!))
            ChangeSetting(() => adjust(1), controller);
        GUI.enabled = previousEnabled;
    }

    private void DrawCard(Rect area, string title)
    {
        GUI.Box(area, GUIContent.none, cardStyle!);
        GUI.Label(new Rect(area.x + 9, area.y + 3, area.width - 18, 24), title, titleStyle!);
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
        GUI.enabled = previousEnabled && expandedSelector == SelectorKind.None;
        for (int index = 0; index < choices.Count; index++)
        {
            Choice<T> choice = choices[index];
            GUI.enabled = previousEnabled && expandedSelector == SelectorKind.None && choice.Enabled;
            bool isSelected = EqualityComparer<T>.Default.Equals(choice.Value, selected);
            if (GUI.Button(
                    new Rect(rect.x + index * (width + 3), rect.y, width, rect.height),
                    choice.Label,
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
        internal static readonly StepperLayout Solver = new(40, 68);
    }

    private void DrawStepper(Rect rect, Stepper stepper, IApsGeneratorHudController controller, StepperLayout layout)
    {
        float controlsWidth = StepperButtonWidth * 2 + layout.ValueWidth + StepperControlGap * 2;
        float controlsX = layout.ControlsOffset.HasValue
            ? Mathf.Min(rect.x + layout.ControlsOffset.Value, rect.xMax - controlsWidth)
            : rect.xMax - controlsWidth;
        GUI.Label(new Rect(rect.x, rect.y, Mathf.Max(0, controlsX - rect.x - 4), rect.height), stepper.Label, mutedStyle!);
        bool previousEnabled = GUI.enabled;
        bool controlsEnabled = previousEnabled && expandedSelector == SelectorKind.None && stepper.Enabled;
        GUI.enabled = controlsEnabled && stepper.CanDecrease;
        if (GUI.Button(new Rect(controlsX, rect.y + 3, StepperButtonWidth, rect.height - 6), "−", buttonStyle!))
            ChangeSetting(() => stepper.Adjust(-1), controller);
        GUI.enabled = controlsEnabled;
        GUI.Label(new Rect(controlsX + StepperButtonWidth + StepperControlGap, rect.y + 3,
            layout.ValueWidth, rect.height - 6), stepper.Value, valueStyle!);
        GUI.enabled = controlsEnabled && stepper.CanIncrease;
        if (GUI.Button(new Rect(controlsX + controlsWidth - StepperButtonWidth, rect.y + 3,
            StepperButtonWidth, rect.height - 6), "+", buttonStyle!))
            ChangeSetting(() => stepper.Adjust(1), controller);
        GUI.enabled = previousEnabled;
    }

    private static void DrawButton(Rect rect, string label, Action action, IApsGeneratorHudController controller, GUIStyle style)
    {
        if (!GUI.Button(rect, label, style))
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
        bool clicked = GUI.Button(rect, content, style);
        GUI.enabled = previousEnabled;
        if (clicked)
            ChangeSetting(change, controller);
    }

    private static void ChangeSetting(Action change, IApsGeneratorHudController controller)
    {
        controller.ClaimHudInput();
        controller.ChangeSettings(change);
    }

    private static Rect[] CalculateColumns(float panelWidth)
    {
        float contentWidth = panelWidth - Padding * 2 - Gap * 3;
        float desiredWidth = TemplateCardWidth + TetrisOptionsCardWidth + PrefabCardWidth + SolveSettingsCardWidth;
        float scale = Mathf.Min(1, contentWidth / desiredWidth);
        float tetrisOptionsWidth = TetrisOptionsCardWidth * scale;
        float prefabWidth = PrefabCardWidth * scale;
        float solveSettingsWidth = SolveSettingsCardWidth * scale;
        float templateWidth = contentWidth - tetrisOptionsWidth - prefabWidth - solveSettingsWidth;
        float x = Padding;
        return
        [
            new Rect(x, BodyTop, templateWidth, BodyHeight),
            new Rect(x += templateWidth + Gap, BodyTop, tetrisOptionsWidth, BodyHeight),
            new Rect(x += tetrisOptionsWidth + Gap, BodyTop, prefabWidth, BodyHeight),
            new Rect(x + prefabWidth + Gap, BodyTop, solveSettingsWidth, BodyHeight)
        ];
    }

    private static Rect TemplateShapeRect(Rect card)
    {
        float width = CompactStepperWidth * 2 + CompactControlGap;
        return new Rect(card.xMax - width - 9, card.y + 29, width, SelectorHeight);
    }

    private static Rect SymmetrySelectorRect(Rect card) =>
        new(card.x + 9, card.y + 82, 90, SelectorHeight);

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

    private static Rect CalculatePanelRect()
    {
        float width = Mathf.Min(Screen.width - 2 * PanelMargin, MaximumPanelWidth);
        float height = Mathf.Min(PanelHeight, Screen.height - 2 * PanelMargin);
        return new Rect((Screen.width - width) / 2, Screen.height - height - PanelMargin, width, height);
    }

    private void EnsureStyles()
    {
        if (panelStyle is not null)
            return;

        Texture2D panel = CreateTexture(new Color(0.025f, 0.035f, 0.045f, 0.97f));
        Texture2D card = CreateTexture(new Color(0.065f, 0.085f, 0.10f, 1f));
        Texture2D button = CreateTexture(new Color(0.13f, 0.17f, 0.20f, 1f));
        Texture2D hover = CreateTexture(new Color(0.17f, 0.28f, 0.32f, 1f));
        Texture2D accent = CreateTexture(new Color(0.10f, 0.72f, 0.80f, 1f));
        Texture2D selected = CreateTexture(new Color(0.08f, 0.52f, 0.60f, 1f));

        panelStyle = CreateBoxStyle(panel);
        cardStyle = CreateBoxStyle(card);
        buttonStyle = CreateButtonStyle(button, hover, accent, Color.white);
        selectedButtonStyle = CreateButtonStyle(selected, accent, accent, Color.white);
        primaryButtonStyle = CreateButtonStyle(accent, selected, selected, Color.black);
        titleStyle = CreateLabelStyle(15, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        valueStyle = CreateLabelStyle(14, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        valueStyle.padding = new RectOffset(0, 0, 0, 0);
        bodyStyle = CreateLabelStyle(13, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white);
        mutedStyle = CreateLabelStyle(12, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.68f, 0.78f, 0.82f));
        warningStyle = CreateLabelStyle(12, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.78f, 0.12f));
    }

    private static void DrawSpinner(Rect area, float elapsedSeconds)
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
                new Rect(
                    center.x + Mathf.Cos(radians) * radius - 2,
                    center.y + Mathf.Sin(radians) * radius - 2,
                    4,
                    4),
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

    private static GUIStyle CreateButtonStyle(Texture2D normal, Texture2D hover, Texture2D active, Color textColor)
    {
        var style = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
        SetButtonState(style.normal, normal, textColor);
        SetButtonState(style.hover, hover, textColor);
        SetButtonState(style.active, active, Color.black);
        SetButtonState(style.focused, hover, textColor);
        return style;
    }

    private static GUIStyle CreateLabelStyle(int fontSize, FontStyle fontStyle, TextAnchor alignment, Color color)
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = fontSize, fontStyle = fontStyle, alignment = alignment, wordWrap = false };
        style.normal.textColor = color;
        return style;
    }

    private static void SetButtonState(GUIStyleState state, Texture2D background, Color textColor)
    {
        state.background = background;
        state.textColor = textColor;
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
