namespace ApsGenerator.UI.Models;

public static class TemplateShapeValues
{
    public static EnumDisplayItem<TemplateShape>[] All { get; } =
    [
        new(TemplateShape.Circle, "Circle"),
        new(TemplateShape.CircleCenterHole, "Circle - Center Hole"),
        new(TemplateShape.Rectangle, "Rectangle")
    ];
}
