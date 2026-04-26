using System.Text.Json;
using System.Text.Json.Serialization;
using ApsGenerator.Core.Models;

namespace ApsGenerator.UI.Services.Export;

public static class BlueprintExporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static void Export(
        IReadOnlyList<Placement> placements,
        Grid grid,
        TetrisType type,
        ExportOptions options,
        string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        string json = BuildJson(placements, grid, type, options);
        string outputPath = filePath.EndsWith(".blueprint", StringComparison.OrdinalIgnoreCase)
            ? filePath
            : $"{filePath}.blueprint";

        File.WriteAllText(outputPath, json);
    }

    public static string BuildJson(
        IReadOnlyList<Placement> placements,
        Grid grid,
        TetrisType type,
        ExportOptions options)
    {
        BlueprintFile blueprint = BlueprintBuilder.Build(placements, grid, type, options);
        return JsonSerializer.Serialize(blueprint, SerializerOptions);
    }
}
