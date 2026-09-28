using Assets.Scripts.Gui;
using Assets.Scripts.Persistence;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ui.Special.PopUps;
using UnityEngine;
using ApsGenerator.Mod.Generation;

namespace ApsGenerator.Mod.UI;

internal sealed class ApsGeneratorBehaviour : MonoBehaviour, IApsGeneratorHudController
{
    private const float PrefabControlsRight = 0.6f;
    private const float PrefabControlsTop = 0.7f;
    private const float LauncherButtonGap = 5;
    private const float LauncherButtonSize = 48;
    private static ApsGeneratorBehaviour? instance;
    private static int suppressBuildInputUntilFrame = -1;

    private readonly ApsGenerationSettings settings = new();
    private readonly ApsGeneratorHud hud = new();
    private readonly ApsWorldVolumePreview worldPreview = new();
    private readonly ApsGenerationSession generation = new(new ApsGenerationSolver());
    private Rect windowRect;
    private string status = "Ready";
    private bool windowVisible;
    private bool worldPreviewEnabled = true;
    private bool launcherIconLoadAttempted;
    private ApsSettingsStore? settingsStore;
    private SavedSubObject? ownedPrefab;
    private Texture2D? launcherIcon;

    internal static bool ShouldSuppressBuildInput
    {
        get
        {
            ApsGeneratorBehaviour? behaviour = instance;
            if (Time.frameCount <= suppressBuildInputUntilFrame)
                return true;
            if (behaviour is null || !behaviour.windowVisible || !IsPrefabModeActive())
                return false;

            Vector2 pointer = new(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return behaviour.windowRect.Contains(pointer) ||
                   (behaviour.worldPreviewEnabled && behaviour.worldPreview.IsPointerCaptured(
                       behaviour.settings,
                       behaviour.generation.Solution is not null));
        }
    }

    internal static bool ShouldReplaceNativePrefabHud =>
        instance is { windowVisible: true } &&
        IsBuildModeActive() &&
        cBuild.GetSingleton()?.BuildingWith.Mode == BuildingWithMode.Prefab;

    internal static void DrawPrefabGeneratorButton()
    {
        if (instance is not { windowVisible: false } behaviour || !IsPrefabModeActive())
            return;

        var area = new Rect(
            Screen.width * PrefabControlsRight + LauncherButtonGap,
            Screen.height * PrefabControlsTop,
            LauncherButtonSize,
            LauncherButtonSize);
        behaviour.EnsureLauncherIconLoaded();
        var content = behaviour.launcherIcon is null
            ? new GUIContent("APS", "Open APS Generator")
            : new GUIContent(behaviour.launcherIcon, "Open APS Generator");
        if (!GUI.Button(area, content))
            return;

        ClaimBuildInput();
        behaviour.OpenHud();
    }

    ApsGenerationSettings IApsGeneratorHudController.Settings => settings;
    string IApsGeneratorHudController.Status => generation.IsBusy
        ? generation.IsRefreshing ? "Updating prefab" : "Solving"
        : status;
    bool IApsGeneratorHudController.IsSolving => generation.IsBusy;
    bool IApsGeneratorHudController.HasCurrentSolution => generation.Solution is not null;
    bool IApsGeneratorHudController.HasActivePrefab => IsGeneratedPrefabActive();
    float IApsGeneratorHudController.SolveElapsedSeconds => (float)generation.ElapsedSeconds;

    void IApsGeneratorHudController.Generate() => StartSolve();
    void IApsGeneratorHudController.Cancel()
    {
        generation.Cancel();
        status = "Cancelled";
    }
    void IApsGeneratorHudController.HideHud() => HideHud();
    void IApsGeneratorHudController.ResetDefaults() => ChangeSettings(settings.ResetToDefaults);
    void IApsGeneratorHudController.Save() => ShowSaveDialog();
    void IApsGeneratorHudController.Clear() => ClearGeneration();
    void IApsGeneratorHudController.ChangeSettings(Action change) => ChangeSettings(change);
    void IApsGeneratorHudController.ClaimHudInput() => ClaimBuildInput();

    private void Awake()
    {
        instance = this;
        settingsStore = new ApsSettingsStore();
        settingsStore.LoadInto(settings);
    }

    private void Update()
    {
        if (windowVisible && !HasGeneratorPlacementOwnership())
            HideHud();
        else if (!IsBuildModeActive())
            worldPreview.CancelInteraction();

        if (Input.GetKeyDown(KeyCode.F7) && IsBuildModeActive())
        {
            if (windowVisible)
                HideHud();
            else
                OpenHud();
        }

        ObserveCompletedSolve();
    }

    private void LateUpdate()
    {
        if (!worldPreviewEnabled || !windowVisible || !IsPrefabModeActive())
            return;

        try
        {
            worldPreview.Draw(settings, generation.Solution is not null, ChangeSettings);
        }
        catch (Exception error)
        {
            worldPreviewEnabled = false;
            status = "3D preview disabled after a rendering error. Generation remains available.";
            AdvLogger.LogException("[APS Generator] 3D preview rendering failed", error, LogOptions.None);
        }
    }

    private void OnGUI()
    {
        if (!windowVisible || !IsPrefabModeActive())
            return;

        try
        {
            windowRect = hud.Draw(this);
        }
        catch (Exception error)
        {
            HideHud();
            AdvLogger.LogException("[APS Generator] HUD rendering failed", error, LogOptions.None);
        }
    }

    private void StartSolve()
    {
        if (generation.IsBusy) return;
        if (TryDisarmGeneratedPrefab())
            EnterGeneratorPrefabMode();
        generation.Generate(settings.CreateRequest());
    }

    private void ObserveCompletedSolve()
    {
        ApsGenerationCompletion? completion = generation.Poll();
        if (completion is null) return;
        if (completion.Error is Exception error)
        {
            AdvLogger.LogException("[APS Generator] Solver failed", error, LogOptions.None);
            status = $"Failed: {error.GetType().Name}. See the FtD log for details.";
            return;
        }
        if (completion.Solution is ApsSolution solution)
        {
            status = ApsGenerationStatus.Describe(solution);
            if (solution.Result.Placements.Count > 0) TryStartNativePlacement();
        }
    }

    private bool TryStartNativePlacement()
    {
        if (!HasGeneratorPlacementOwnership() || generation.Solution is not ApsSolution solution)
            return false;

        try
        {
            cBuild build = cBuild.GetSingleton();
            if (build is null ||
                (build.buildMode != enumBuildMode.active &&
                 build.buildMode != enumBuildMode.activeInventory) ||
                build.GetC() is not AllConstruct construct)
            {
                return false;
            }

            ApsGeneratedPrefab generated = ApsPrefabFactory.Create(construct, solution);
            build.SetLoadPrefab(generated.Prefab);
            ownedPrefab = generated.Prefab;
            status = $"{ApsGenerationStatus.Describe(solution)} · {generation.LastSolveSeconds:0.0}s · " +
                     $"{solution.Result.ClusterCount} clusters · " +
                     $"{generated.MaterialCost:N0} mats";
            return true;
        }
        catch (Exception error)
        {
            AdvLogger.LogException("[APS Generator] Native prefab creation failed", error, LogOptions.None);
            status = $"Prefab creation failed: {error.Message}";
            return false;
        }
    }

    private void ChangeSettings(Action change)
    {
        ApsSettingsChangeImpact impact = settings.Change(change);
        if (impact == ApsSettingsChangeImpact.None) return;
        settingsStore?.Save(settings);
        if (impact == ApsSettingsChangeImpact.FutureSolve) return;
        if (impact == ApsSettingsChangeImpact.Prefab)
        {
            RefreshGeneratedPrefab();
            return;
        }
        ClearGeneration();
    }

    private void RefreshGeneratedPrefab()
    {
        if (TryDisarmGeneratedPrefab())
            EnterGeneratorPrefabMode();
        generation.Refresh(settings.CreateRequest());
    }

    private void ClearGeneration()
    {
        bool ownsPlacement = HasGeneratorPlacementOwnership();
        generation.Clear();
        status = "Ready";
        if (!ownsPlacement) return;

        TryDisarmGeneratedPrefab();
        EnterGeneratorPrefabMode();
    }

    private void ShowSaveDialog()
    {
        cBuild? build = cBuild.GetSingleton();
        SavedSubObject? prefab = build?.BuildingWith.Prefab;
        if (!IsGeneratedPrefabActive() || prefab is not { IsValid: true })
        {
            status = "No generated prefab to save.";
            return;
        }

        try
        {
            BlueprintFolder folder = GameFolders.GetPrefabFolder();
            GuiPopUp.Instance.Add(new PopupTreeViewSave<BlueprintFileModel>(
                "Save prefab",
                FtdGuiUtils.GetFileBrowserFor(folder),
                (name, saved) =>
                {
                    if (!saved) return;
                    prefab.Name = name;
                    status = $"Saved prefab · {name}";
                },
                name => BlueprintFileModelHelp.GenerateBlueprintFileModel(prefab.Blueprint, name),
                string.IsNullOrEmpty(prefab.Name) ? "Generated APS" : prefab.Name));
        }
        catch (Exception error)
        {
            AdvLogger.LogException("[APS Generator] Failed to open prefab save dialog", error, LogOptions.None);
            status = $"Save failed: {error.Message}";
        }
    }

    private bool TryDisarmGeneratedPrefab()
    {
        if (!IsGeneratedPrefabActive())
            return false;
        cBuild.GetSingleton()?.SetBlockToPlace(null!);
        return true;
    }

    private bool IsGeneratedPrefabActive()
    {
        return HasGeneratorPlacementOwnership() &&
               ownedPrefab?.AnalyticsName == ApsPrefabFactory.AnalyticsName;
    }

    private bool HasGeneratorPlacementOwnership()
    {
        cBuild? build = cBuild.GetSingleton();
        return windowVisible && ownedPrefab is not null &&
               IsBuildModeActive() &&
               build?.BuildingWith.Mode == BuildingWithMode.Prefab &&
               ReferenceEquals(build.BuildingWith.Prefab, ownedPrefab);
    }

    private static bool IsPrefabModeActive()
    {
        cBuild? build = cBuild.GetSingleton();
        return IsBuildModeActive() &&
               build?.BuildingWith.Mode == BuildingWithMode.Prefab;
    }

    private void OpenHud()
    {
        windowVisible = true;
        if (!IsGeneratedPrefabActive())
        {
            EnterGeneratorPrefabMode();
            if (!generation.IsBusy && generation.Solution?.Result.Placements.Count > 0)
                TryStartNativePlacement();
        }
    }

    private void HideHud()
    {
        windowVisible = false;
        worldPreview.CancelInteraction();
    }

    private void EnsureLauncherIconLoaded()
    {
        if (launcherIconLoadAttempted)
            return;
        launcherIconLoadAttempted = true;

        try
        {
            launcherIcon = ApsGeneratorIcon.Load();
        }
        catch (Exception error)
        {
            AdvLogger.LogException("[APS Generator] Failed to load launcher icon", error, LogOptions.None);
        }
    }

    private void EnterGeneratorPrefabMode()
    {
        cBuild? build = cBuild.GetSingleton();
        if (build is null)
            return;

        var captureCursor = new SavedSubObject(null!)
        {
            Dimensions = new Vector3i(1, 1, 1)
        };
        build.SetLoadPrefab(captureCursor);
        ownedPrefab = captureCursor;
    }

    private static bool IsBuildModeActive()
    {
        cBuild? build = cBuild.GetSingleton();
        return build is not null &&
               (build.buildMode == enumBuildMode.active ||
                build.buildMode == enumBuildMode.activeInventory);
    }

    private static void ClaimBuildInput()
    {
        suppressBuildInputUntilFrame = Time.frameCount + 2;
        Event.current?.Use();
    }

    private void OnDestroy()
    {
        generation.Dispose();
        if (ReferenceEquals(instance, this)) instance = null;
        worldPreview.Dispose();
        hud.Dispose();
        if (launcherIcon is not null)
            UnityEngine.Object.Destroy(launcherIcon);
        settingsStore?.Save(settings);
    }
}
