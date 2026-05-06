using System.Collections.Generic;
using System.Numerics;

namespace ApsGenerator.UI.Services.Export;

public sealed record BlockDefinition(
    string Name,
    int BlockId,
    string DefaultBlockData,
    int MaterialCost);

public static class GameData
{
    public const string SharedClipBlockData = "AAAADgAAAAMAAQAAAAAAAF0bAQAAAwAAAAA=";

    public const string VehicleData = "sct0AAAAAAAA";
    public const int DefaultBCI = 0;
    public const string GameVersion = "4.2.5.2";
    public const string CreatorId = "c241a249-42d7-4bf7-85f1-4efe34ba5664";
    public const string CreatorReadableName = "trk20";
    public const int FileModelVersionMajor = 1;
    public const int FileModelVersionMinor = 0;

    public static readonly IReadOnlyDictionary<string, BlockDefinition> Blocks =
        new Dictionary<string, BlockDefinition>
        {
            ["Loader_1"] = new("Loader_1", 365, "", 240),
            ["Loader_2"] = new("Loader_2", 366, "", 300),
            ["Loader_3"] = new("Loader_3", 367, "", 330),
            ["Loader_4"] = new("Loader_4", 368, "", 360),
            ["Loader_5"] = new("Loader_5", 369, "", 390),
            ["Loader_6"] = new("Loader_6", 370, "", 420),
            ["Loader_7"] = new("Loader_7", 371, "", 450),
            ["Loader_8"] = new("Loader_8", 372, "", 480),

            ["Clip_1"] = new("Clip_1", 419, SharedClipBlockData, 160),
            ["Clip_2"] = new("Clip_2", 420, SharedClipBlockData, 200),
            ["Clip_3"] = new("Clip_3", 421, SharedClipBlockData, 220),
            ["Clip_4"] = new("Clip_4", 422, SharedClipBlockData, 240),
            ["Clip_5"] = new("Clip_5", 423, SharedClipBlockData, 260),
            ["Clip_6"] = new("Clip_6", 424, SharedClipBlockData, 280),
            ["Clip_7"] = new("Clip_7", 425, SharedClipBlockData, 300),
            ["Clip_8"] = new("Clip_8", 426, SharedClipBlockData, 320),

            ["AmmoIntake_1"] = new("AmmoIntake_1", 364, "", 50),
            ["Ejector_1"] = new("Ejector_1", 231, "", 10),
            ["GaugeIncreaser_1"] = new("GaugeIncreaser_1", 387, "", 20),
            ["Cooler_1"] = new("Cooler_1", 255, "", 50),
        };

    public static readonly IReadOnlyDictionary<int, string> ItemGuids =
        new Dictionary<int, string>
        {
            [0] = "f675b19a-4a67-41de-bd60-651bac2cfe17",
            [228] = "ac65ce78-2e21-465e-a28b-07ebbd65805e",
            [229] = "93c6e594-4bea-419c-93f6-8d1f6c3463ec",
            [230] = "8a96edf6-93c3-47c2-b3ad-894598f262e3",
            [231] = "d13556da-e6dc-4c49-a9ec-b47517709da5",
            [232] = "52ff74a3-3d0f-4b2f-bfe3-23ff12f74354",
            [255] = "703d6094-850b-45fc-a01c-25ceddd49dcb",
            [364] = "0a1aa046-e841-4813-907e-6567e596a079",
            [365] = "d7d56a2b-bc35-4c89-87de-38c99e7d1c2f",
            [366] = "04d60765-2c11-48eb-b9fa-c3eb847b91d5",
            [367] = "23453c1f-3946-4db0-a75e-f8b6dab12569",
            [368] = "86a9327f-50c1-44d9-aa06-08dc3f25b433",
            [369] = "7bb4572c-0ee5-42a5-b562-47a573757f41",
            [370] = "1dd8c1cf-ce0b-469d-9924-455dae4958f5",
            [371] = "704a2934-b07e-41cb-880d-91b3995152a9",
            [372] = "832b5533-fa5d-45ec-ba4f-2dca1e8f0bba",
            [387] = "3d6d4fca-b7a9-44f3-a888-d3e43a79331a",
            [419] = "375f4305-47bc-4abd-8c68-b67cb50e7036",
            [420] = "f45bd228-43c8-482c-824c-71e48e8ef27a",
            [421] = "7676bdb0-2cc4-4966-8d71-b1025244d911",
            [422] = "120b90ce-8434-4e44-a32f-7e5da874eaf7",
            [423] = "24182e93-94d4-4b29-8795-a107b21b2695",
            [424] = "1cbba583-ef54-4de9-9364-4e7371bf4ac3",
            [425] = "00e9e62f-8b53-4cca-89ef-2a99a76766da",
            [426] = "973c1ee1-2dc4-4cd7-be7c-32a37cb405de",
        };

    public static string GetAmmoIntakeBlockData(Vector3 direction)
    {
        if (direction.Y > 0.5f) return "AAAADgAAAAcAAAAAAAAAAF0bAQAAAAAAAATIAAAA";
        if (direction.Y < -0.5f) return "AAAADgAAAAcAAAAAAAAAAF0bAQAAAAAAAATHAAAA";
        if (direction.Z > 0.5f) return "AAAADgAAAAcAAAAAAAAAAF0bAQAAAAAAAATWAAAA";
        if (direction.X > 0.5f) return "AAAADgAAAAcAAAAAAAAAAF0bAQAAAAAAAAT6AAAA";
        if (direction.Z < -0.5f) return "AAAADgAAAAcAAAAAAAAAAF0bAQAAAAAAAAT5AAAA";
        if (direction.X < -0.5f) return "AAAADgAAAAcAAAAAAAAAAF0bAQAAAAAAAAT4AAAA";
        throw new ArgumentOutOfRangeException(nameof(direction), direction, "No AmmoIntake block data for the given direction.");
    }
}
