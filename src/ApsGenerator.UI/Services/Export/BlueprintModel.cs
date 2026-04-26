using System.Text.Json.Serialization;

namespace ApsGenerator.UI.Services.Export;

public sealed record BlueprintFile
{
    [JsonPropertyName("FileModelVersion")]
    public BlueprintFileModelVersion FileModelVersion { get; init; } = new();

    [JsonPropertyName("Name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("Version")]
    public int Version { get; init; }

    [JsonPropertyName("SavedTotalBlockCount")]
    public int SavedTotalBlockCount { get; init; }

    [JsonPropertyName("SavedMaterialCost")]
    public double SavedMaterialCost { get; init; }

    [JsonPropertyName("ContainedMaterialCost")]
    public double ContainedMaterialCost { get; init; }

    [JsonPropertyName("ItemDictionary")]
    public Dictionary<string, string> ItemDictionary { get; init; } = new();

    [JsonPropertyName("Blueprint")]
    public BlueprintBody Blueprint { get; init; } = new();
}

public sealed record BlueprintFileModelVersion
{
    [JsonPropertyName("Major")]
    public int Major { get; init; } = GameData.FileModelVersionMajor;

    [JsonPropertyName("Minor")]
    public int Minor { get; init; } = GameData.FileModelVersionMinor;
}

public sealed record BlueprintBody
{
    [JsonPropertyName("ContainedMaterialCost")]
    public double ContainedMaterialCost { get; init; }

    [JsonPropertyName("COL")]
    public List<string>? COL { get; init; }

    [JsonPropertyName("SCs")]
    public List<object> SCs { get; init; } = [];

    [JsonPropertyName("BLP")]
    public List<string> BLP { get; init; } = [];

    [JsonPropertyName("BLR")]
    public List<int> BLR { get; init; } = [];

    [JsonPropertyName("BP1")]
    public object? BP1 { get; init; }

    [JsonPropertyName("BP2")]
    public object? BP2 { get; init; }

    [JsonPropertyName("BCI")]
    public List<int> BCI { get; init; } = [];

    [JsonPropertyName("BEI")]
    public object? BEI { get; init; }

    [JsonPropertyName("BlockData")]
    public string BlockData { get; init; } = string.Empty;

    [JsonPropertyName("VehicleData")]
    public string VehicleData { get; init; } = GameData.VehicleData;

    [JsonPropertyName("designChanged")]
    public bool DesignChanged { get; init; }

    [JsonPropertyName("blueprintVersion")]
    public int BlueprintVersion { get; init; }

    [JsonPropertyName("blueprintName")]
    public string BlueprintName { get; init; } = string.Empty;

    [JsonPropertyName("SerializedInfo")]
    public BlueprintSerializedInfo SerializedInfo { get; init; } = new();

    [JsonPropertyName("Name")]
    public object? Name { get; init; }

    [JsonPropertyName("ItemNumber")]
    public int ItemNumber { get; init; }

    [JsonPropertyName("LocalPosition")]
    public string LocalPosition { get; init; } = "0,0,0";

    [JsonPropertyName("LocalRotation")]
    public string LocalRotation { get; init; } = "0,0,0,0";

    [JsonPropertyName("ForceId")]
    public int ForceId { get; init; }

    [JsonPropertyName("TotalBlockCount")]
    public int TotalBlockCount { get; init; }

    [JsonPropertyName("MaxCords")]
    public string MaxCords { get; init; } = "0,0,0";

    [JsonPropertyName("MinCords")]
    public string MinCords { get; init; } = "0,0,0";

    [JsonPropertyName("BlockIds")]
    public List<int> BlockIds { get; init; } = [];

    [JsonPropertyName("BlockState")]
    public object? BlockState { get; init; }

    [JsonPropertyName("AliveCount")]
    public int AliveCount { get; init; }

    [JsonPropertyName("BlockStringData")]
    public object? BlockStringData { get; init; }

    [JsonPropertyName("BlockStringDataIds")]
    public object? BlockStringDataIds { get; init; }

    [JsonPropertyName("GameVersion")]
    public string GameVersion { get; init; } = GameData.GameVersion;

    [JsonPropertyName("PersistentSubObjectIndex")]
    public int PersistentSubObjectIndex { get; init; } = -1;

    [JsonPropertyName("PersistentBlockIndex")]
    public int PersistentBlockIndex { get; init; } = -1;

    [JsonPropertyName("AuthorDetails")]
    public BlueprintAuthorDetails AuthorDetails { get; init; } = new();

    [JsonPropertyName("BlockCount")]
    public int BlockCount { get; init; }
}

public sealed record BlueprintSerializedInfo
{
    [JsonPropertyName("JsonDictionary")]
    public Dictionary<string, object> JsonDictionary { get; init; } = new();

    [JsonPropertyName("IsEmpty")]
    public bool IsEmpty { get; init; } = true;
}

public sealed record BlueprintAuthorDetails
{
    [JsonPropertyName("Valid")]
    public bool Valid { get; init; } = true;

    [JsonPropertyName("ForeignBlocks")]
    public int ForeignBlocks { get; init; }

    [JsonPropertyName("CreatorId")]
    public string CreatorId { get; init; } = GameData.CreatorId;

    [JsonPropertyName("ObjectId")]
    public string ObjectId { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("CreatorReadableName")]
    public string CreatorReadableName { get; init; } = GameData.CreatorReadableName;

    [JsonPropertyName("HashV1")]
    public string HashV1 { get; init; } = string.Empty;
}
