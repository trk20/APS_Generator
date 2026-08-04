namespace ApsGenerator.Core.Models;

public enum EjectorKind
{
    /// <summary>Under loader at Y=−1; protrudes into an own clip at Y=−1.</summary>
    Bottom,

    /// <summary>
    /// 3-clip open-arm mount pointing down (−Y).
    /// Frees all clip bottoms and the loader bottom for intakes.
    /// </summary>
    VerticalOpenArmDown,

    /// <summary>
    /// Synthetic intake-only choice: no ejector block.
    /// Bottom intakes under loader and clips fill quota (typically zero top deficit).
    /// </summary>
    None,
}
