using System.Reflection;
using UnityEngine;

namespace ApsGenerator.Mod.UI;

internal static class ApsGeneratorIcon
{
    private const string ResourceName = "ApsGenerator.Mod.Assets.Icon.ico";
    private const int ImageOffsetPosition = 18;

    internal static Texture2D Load()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {ResourceName}.");
        using var reader = new BinaryReader(stream);
        stream.Position = ImageOffsetPosition;
        int imageOffset = reader.ReadInt32();
        if (imageOffset <= ImageOffsetPosition || imageOffset >= stream.Length)
            throw new InvalidDataException("The embedded APS Generator icon has an invalid image offset.");

        stream.Position = imageOffset;
        byte[] image = reader.ReadBytes(checked((int)(stream.Length - imageOffset)));
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = "APS Generator launcher icon"
        };
        if (texture.LoadImage(image))
            return texture;

        UnityEngine.Object.Destroy(texture);
        throw new InvalidDataException("The embedded APS Generator icon could not be decoded.");
    }
}
