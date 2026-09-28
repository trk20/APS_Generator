using BrilliantSkies.Modding;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using ApsGenerator.Mod.UI;

namespace ApsGenerator.Mod;

public sealed class Plugin : GamePlugin_PostLoad
{
    private const string HostName = "ApsGenerator";
    private const string HarmonyId = "trk20.apsgenerator";

    private static readonly string ModDirectory =
        Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!;
    private static readonly JObject Manifest = JObject.Parse(File.ReadAllText(
        Path.Combine(ModDirectory, "plugin.json")));

    internal static string GameVersion => Manifest["gameversion"]!.ToObject<string>()!;

    public string name => Manifest["name"]!.ToObject<string>()!;
    public Version version => Manifest["version"]!.ToObject<Version>()!;

    public void OnLoad()
    {
        NativeLibraryBootstrap.LoadFromModDirectory(typeof(Plugin).Assembly);
        new Harmony(HarmonyId).PatchAll(typeof(Plugin).Assembly);

        var host = new GameObject(HostName)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent<ApsGeneratorBehaviour>();

        ModProblems.AddModProblem($"{name} v{version} active!", ModDirectory, string.Empty, false);
    }

    public bool AfterAllPluginsLoaded() => true;

    public void OnSave()
    {
    }
}
