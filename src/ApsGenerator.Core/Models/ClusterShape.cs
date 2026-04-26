namespace ApsGenerator.Core.Models;

public sealed class ClusterShape
{
    public TetrisType Type { get; }
    public int RotationIndex { get; }
    public IReadOnlyList<CellOffset> Offsets { get; }

    private ClusterShape(TetrisType type, int rotationIndex, IReadOnlyList<CellOffset> offsets)
    {
        Type = type;
        RotationIndex = rotationIndex;
        Offsets = offsets;
    }

    public static IReadOnlyList<ClusterShape> GetShapes(TetrisType type) => type switch
    {
        TetrisType.ThreeClip => ThreeClipShapes,
        TetrisType.FourClip => FourClipShapes,
        TetrisType.FiveClip => FiveClipShapes,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    private static readonly IReadOnlyList<ClusterShape> ThreeClipShapes =
    [
        // Rot 0 (open bottom)
        new(TetrisType.ThreeClip, 0,
        [
            new(0, 0, CellRole.Loader),
            new(-1, 0, CellRole.Clip),
            new(0, -1, CellRole.Clip),
            new(0, 1, CellRole.Clip)
        ]),
        // Rot 1 (open left)
        new(TetrisType.ThreeClip, 1,
        [
            new(0, 0, CellRole.Loader),
            new(-1, 0, CellRole.Clip),
            new(0, 1, CellRole.Clip),
            new(1, 0, CellRole.Clip)
        ]),
        // Rot 2 (open top)
        new(TetrisType.ThreeClip, 2,
        [
            new(0, 0, CellRole.Loader),
            new(0, -1, CellRole.Clip),
            new(0, 1, CellRole.Clip),
            new(1, 0, CellRole.Clip)
        ]),
        // Rot 3 (open right)
        new(TetrisType.ThreeClip, 3,
        [
            new(0, 0, CellRole.Loader),
            new(-1, 0, CellRole.Clip),
            new(0, -1, CellRole.Clip),
            new(1, 0, CellRole.Clip)
        ])
    ];

    private static readonly IReadOnlyList<ClusterShape> FourClipShapes =
    [
        new(TetrisType.FourClip, 0,
        [
            new(0, 0, CellRole.Loader),
            new(-1, 0, CellRole.Clip),
            new(0, -1, CellRole.Clip),
            new(0, 1, CellRole.Clip),
            new(1, 0, CellRole.Clip)
        ])
    ];

    private static readonly IReadOnlyList<ClusterShape> FiveClipShapes =
    [
        // Rot 0 (connection right)
        new(TetrisType.FiveClip, 0,
        [
            new(0, 0, CellRole.Loader),
            new(-1, 0, CellRole.Clip),
            new(0, -1, CellRole.Clip),
            new(1, 0, CellRole.Clip),
            new(0, 1, CellRole.Connection)
        ]),
        // Rot 1 (connection bottom)
        new(TetrisType.FiveClip, 1,
        [
            new(0, 0, CellRole.Loader),
            new(-1, 0, CellRole.Clip),
            new(0, -1, CellRole.Clip),
            new(0, 1, CellRole.Clip),
            new(1, 0, CellRole.Connection)
        ]),
        // Rot 2 (connection left)
        new(TetrisType.FiveClip, 2,
        [
            new(0, 0, CellRole.Loader),
            new(-1, 0, CellRole.Clip),
            new(0, 1, CellRole.Clip),
            new(1, 0, CellRole.Clip),
            new(0, -1, CellRole.Connection)
        ]),
        // Rot 3 (connection top)
        new(TetrisType.FiveClip, 3,
        [
            new(0, 0, CellRole.Loader),
            new(0, -1, CellRole.Clip),
            new(0, 1, CellRole.Clip),
            new(1, 0, CellRole.Clip),
            new(-1, 0, CellRole.Connection)
        ])
    ];
}
