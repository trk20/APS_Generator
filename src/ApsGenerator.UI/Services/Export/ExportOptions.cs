using ApsGenerator.Core.Models;

namespace ApsGenerator.UI.Services.Export;

public sealed record ExportOptions(
    string BlueprintName,
    int TargetHeight,
    ExportExtraLayers ExtraLayers,
    CoolerSnakeResult? CoolerSnakes = null);
