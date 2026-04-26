namespace ApsGenerator.UI.Models;

public sealed record EnumDisplayItem<T>(T Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}
