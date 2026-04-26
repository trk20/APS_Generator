namespace ApsGenerator.UI.Services;

public static class FiveClipHeight
{
    public const int MinHeight = 3;
    public const int MaxHeight = 24;
    public const int Step = 3;

    public static int RoundToMultipleOf3(int value) =>
        Math.Clamp((int)(Math.Round(value / 3.0) * 3), MinHeight, MaxHeight);
}