namespace ApsGenerator.Core.Models;

public readonly record struct CellOffset(int DeltaRow, int DeltaCol, CellRole Role);
