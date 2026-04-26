using System.Globalization;
using System.Text.Json;

namespace ApsGenerator.UI.Services;

public static class UserSettingsStore
{
    private const string AppFolderName = "ApsGenerator";
    private const string SettingsFileName = "settings.json";

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true
    };

    public static UserSettings Load()
    {
        var defaults = new UserSettings();
        var settingsPath = GetSettingsPath();

        if (!File.Exists(settingsPath))
            return defaults;

        try
        {
            using var stream = File.OpenRead(settingsPath);
            using var document = JsonDocument.Parse(stream);
            return ParseSettings(document.RootElement, defaults);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Failed to load user settings: {ex}");
            return defaults;
        }
    }

    public static void Save(UserSettings settings)
    {
        if (settings is null)
            return;

        try
        {
            var settingsPath = GetSettingsPath();
            var settingsDirectory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrWhiteSpace(settingsDirectory))
                Directory.CreateDirectory(settingsDirectory);

            using var stream = File.Create(settingsPath);
            JsonSerializer.Serialize(stream, settings, JsonSerializerOptions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Failed to save user settings: {ex}");
        }
    }

    private static UserSettings ParseSettings(JsonElement root, UserSettings defaults)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return defaults;

        return new UserSettings
        {
            TemplateShape = ReadEnum(root, nameof(UserSettings.TemplateShape), defaults.TemplateShape),
            TemplateWidth = ReadInt(root, nameof(UserSettings.TemplateWidth), defaults.TemplateWidth),
            TemplateHeight = ReadInt(root, nameof(UserSettings.TemplateHeight), defaults.TemplateHeight),
            IsHeightLocked = ReadBool(root, nameof(UserSettings.IsHeightLocked), defaults.IsHeightLocked),
            SelectedTetrisType = ReadEnum(root, nameof(UserSettings.SelectedTetrisType), defaults.SelectedTetrisType),
            SelectedSymmetryType = ReadEnum(root, nameof(UserSettings.SelectedSymmetryType), defaults.SelectedSymmetryType),
            IsHardSymmetry = ReadBool(root, nameof(UserSettings.IsHardSymmetry), defaults.IsHardSymmetry),
            EarlyStopEnabled = ReadBool(root, nameof(UserSettings.EarlyStopEnabled), defaults.EarlyStopEnabled),
            MaxTimeSeconds = ReadDouble(root, nameof(UserSettings.MaxTimeSeconds), defaults.MaxTimeSeconds),
            IsMaximize = ReadBool(root, nameof(UserSettings.IsMaximize), defaults.IsMaximize),
            TargetPlacementCount = ReadInt(root, nameof(UserSettings.TargetPlacementCount), defaults.TargetPlacementCount),
            PaintMode = ReadEnum(root, nameof(UserSettings.PaintMode), defaults.PaintMode),
            LastExportFolder = ReadString(root, nameof(UserSettings.LastExportFolder)),
            ThreadCount = ReadInt(root, nameof(UserSettings.ThreadCount), defaults.ThreadCount),
            DefaultExportHeightBasic = ReadInt(root, nameof(UserSettings.DefaultExportHeightBasic), defaults.DefaultExportHeightBasic),
            DefaultExportHeightFiveClip = ReadInt(root, nameof(UserSettings.DefaultExportHeightFiveClip), defaults.DefaultExportHeightFiveClip),
            ExportNameTemplate = ReadString(root, nameof(UserSettings.ExportNameTemplate)) ?? defaults.ExportNameTemplate,
            NumSolutions = ReadInt(root, nameof(UserSettings.NumSolutions), defaults.NumSolutions),
            UiScale = ReadDouble(root, nameof(UserSettings.UiScale), defaults.UiScale)
        };
    }

    private static string GetSettingsPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appDataPath))
            appDataPath = AppContext.BaseDirectory;

        return Path.Combine(appDataPath, AppFolderName, SettingsFileName);
    }

    private static TEnum ReadEnum<TEnum>(JsonElement root, string propertyName, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return fallback;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numericValue))
        {
            var parsed = (TEnum)Enum.ToObject(typeof(TEnum), numericValue);
            return Enum.IsDefined(parsed) ? parsed : fallback;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var textValue = value.GetString();
            if (!string.IsNullOrWhiteSpace(textValue)
                && Enum.TryParse(textValue, ignoreCase: true, out TEnum parsed)
                && Enum.IsDefined(parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    private static bool ReadBool(JsonElement root, string propertyName, bool fallback)
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => fallback
        };
    }

    private static int ReadInt(JsonElement root, string propertyName, int fallback)
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return fallback;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numericValue))
            return numericValue;

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static double ReadDouble(JsonElement root, string propertyName, double fallback)
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return fallback;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var numericValue))
            return numericValue;

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
