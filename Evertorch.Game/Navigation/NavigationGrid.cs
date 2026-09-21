using System;

namespace Evertorch.Game
{
/// <summary>
/// Walkability and ground height for one map, sampled on a uniform grid over the X/Z plane. Positions stay
/// continuous; the grid only answers where a body may stand and step. Server validation and client prediction
/// must ask the same instance-equivalent grid so they agree.
/// </summary>
public sealed class NavigationGrid
{
    public const int MaxCellsPerAxis = 512;

    /// <summary>
    /// Longest single displacement anything may test with <see cref="CanStep"/>. Longer moves are split, so a body
    /// can never pass through something thinner than this.
    /// </summary>
    public const float MaxMoveStep = 0.25f;

    /// <summary>
    /// Longest distance between two positions checked by <see cref="HasLineOfSight"/>.
    /// </summary>
    public const float LineOfSightStep = 0.125f;

    // Keeps a ramp clear of the step-height limit by a margin, so rounding cannot stall a body halfway up.
    private const float RampSlopeMargin = 0.9f;

    private readonly NavigationCell[] m_cells;

    /// <param name="cells">Row-major: index = row × columns + column. Row 0 has the smallest Z.</param>
    public NavigationGrid(
        int columns,
        int rows,
        float cellSize,
        float originX,
        float originZ,
        float agentRadius,
        float maxStepHeight,
        NavigationCell[] cells)
    {
        if (columns < 1 || columns > MaxCellsPerAxis || rows < 1 || rows > MaxCellsPerAxis)
        {
            throw new ArgumentException("Grid dimensions must be between 1 and 512 cells.");
        }

        if (!IsPositiveFinite(cellSize))
        {
            throw new ArgumentException("Cell size must be positive and finite.", nameof(cellSize));
        }

        if (!IsFinite(originX) || !IsFinite(originZ))
        {
            throw new ArgumentException("The grid origin must be finite.");
        }

        if (!IsPositiveFinite(agentRadius))
        {
            throw new ArgumentException("Agent radius must be positive and finite.", nameof(agentRadius));
        }

        // Paths run between cell centres. A body wider than a cell cannot stand on the centre of any cell beside a
        // wall, so places it could legally reach would have no route to them.
        if (agentRadius > cellSize / 2f)
        {
            throw new ArgumentException("Agent radius must not exceed half the cell size.", nameof(agentRadius));
        }

        if (!IsFinite(maxStepHeight) || maxStepHeight < 0f)
        {
            throw new ArgumentException("Maximum step height must be finite and not negative.", nameof(maxStepHeight));
        }

        if (cells == null || cells.Length != columns * rows)
        {
            throw new ArgumentException("The cell count must equal columns × rows.", nameof(cells));
        }

        float maxRampSlope = MaxRampSlope(agentRadius, maxStepHeight);
        foreach (NavigationCell cell in cells)
        {
            if (cell.IsWalkable && Math.Abs(cell.HeightAtMax - cell.HeightAtMin) / cellSize > maxRampSlope)
            {
                throw new ArgumentException("A ramp is too steep for the agent radius and step height.", nameof(cells));
            }
        }

        Columns = columns;
        Rows = rows;
        CellSize = cellSize;
        OriginX = originX;
        OriginZ = originZ;
        AgentRadius = agentRadius;
        MaxStepHeight = maxStepHeight;
        m_cells = (NavigationCell[])cells.Clone();
    }

    public int Columns { get; }

    public int Rows { get; }

    public float CellSize { get; }

    /// <summary>World X of the edge of column 0 with the smaller coordinate.</summary>
    public float OriginX { get; }

    /// <summary>World Z of the edge of row 0 with the smaller coordinate.</summary>
    public float OriginZ { get; }

    public float AgentRadius { get; }

    /// <summary>
    /// Largest sudden change in ground height a body may cross. Taller edges block like walls, from both sides.
    /// </summary>
    public float MaxStepHeight { get; }

    /// <summary>
    /// The longest displacement the mover tests in one piece. Only the end of a piece is checked, so a piece must
    /// not be longer than the body is wide from its centre: a longer one could land on the far side of a thin
    /// wall, or of the point where two walls meet at a corner, without ever overlapping either.
    /// </summary>
    public float MoveStepLength => Math.Min(MaxMoveStep, AgentRadius);

    /// <summary>
    /// The spacing of the checks along a line of sight, bounded by the agent radius for the same reason.
    /// </summary>
    public float LineOfSightStepLength => Math.Min(LineOfSightStep, AgentRadius);

    /// <summary>
    /// The steepest ramp (rise over run) a grid accepts. The step-height rule compares ground heights across a
    /// body's footprint and across one move, so a ramp must rise less than one step height over either distance.
    /// </summary>
    public static float MaxRampSlope(float agentRadius, float maxStepHeight)
    {
        return RampSlopeMargin * maxStepHeight / Math.Max(agentRadius, MaxMoveStep);
    }

    public NavigationCell GetCell(int column, int row)
    {
        if (!Contains(column, row))
        {
            throw new ArgumentOutOfRangeException(nameof(column), "The cell is outside the grid.");
        }

        return m_cells[(row * Columns) + column];
    }

    public bool Contains(int column, int row)
    {
        return column >= 0 && column < Columns && row >= 0 && row < Rows;
    }

    public bool TryGetCellIndex(float x, float z, out int column, out int row)
    {
        column = ToIndex(x, OriginX);
        row = ToIndex(z, OriginZ);
        return Contains(column, row);
    }

    public bool IsWalkable(float x, float z)
    {
        return TryGetCellIndex(x, z, out int column, out int row) && GetCell(column, row).IsWalkable;
    }

    /// <summary>
    /// Ground height under a point. False outside the grid or on a cell that cannot be walked on.
    /// </summary>
    public bool TrySampleHeight(float x, float z, out float height)
    {
        if (TryGetCellIndex(x, z, out int column, out int row) && GetCell(column, row).IsWalkable)
        {
            height = HeightInCell(column, row, x, z);
            return true;
        }

        height = 0f;
        return false;
    }

    public WorldPosition GetCellCenter(int column, int row)
    {
        float x = OriginX + ((column + 0.5f) * CellSize);
        float z = OriginZ + ((row + 0.5f) * CellSize);
        return new WorldPosition(x, HeightInCell(column, row, x, z), z);
    }

    /// <summary>
    /// Whether a body of <see cref="AgentRadius"/> may stand centered on the point: every cell its circle overlaps
    /// must be walkable ground within one step height of the ground under its center.
    /// </summary>
    public bool CanOccupy(float x, float z)
    {
        if (!IsFinite(x) || !IsFinite(z) || !TrySampleHeight(x, z, out float centerHeight))
        {
            return false;
        }

        int minColumn = ToIndex(x - AgentRadius, OriginX);
        int maxColumn = ToIndex(x + AgentRadius, OriginX);
        int minRow = ToIndex(z - AgentRadius, OriginZ);
        int maxRow = ToIndex(z + AgentRadius, OriginZ);
        float radiusSquared = AgentRadius * AgentRadius;

        for (int row = minRow; row <= maxRow; row++)
        {
            for (int column = minColumn; column <= maxColumn; column++)
            {
                float nearestX = Clamp(x, CellMin(column, OriginX), CellMin(column + 1, OriginX));
                float nearestZ = Clamp(z, CellMin(row, OriginZ), CellMin(row + 1, OriginZ));
                float deltaX = nearestX - x;
                float deltaZ = nearestZ - z;

                // Strictly inside: a body may rest flush against a wall and slide along it.
                if ((deltaX * deltaX) + (deltaZ * deltaZ) >= radiusSquared)
                {
                    continue;
                }

                if (!Contains(column, row) || !GetCell(column, row).IsWalkable)
                {
                    return false;
                }

                float nearestHeight = HeightInCell(column, row, nearestX, nearestZ);
                if (Math.Abs(nearestHeight - centerHeight) > MaxStepHeight)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Whether a body standing at the first point may move straight to the second, which must be close enough that
    /// nothing can lie between them (callers keep it at or below <see cref="LineOfSightStep"/> or their own sub-step).
    /// </summary>
    public bool CanStep(float fromX, float fromZ, float toX, float toZ)
    {
        if (!CanOccupy(toX, toZ))
        {
            return false;
        }

        return TrySampleHeight(fromX, fromZ, out float fromHeight)
            && TrySampleHeight(toX, toZ, out float toHeight)
            && Math.Abs(toHeight - fromHeight) <= MaxStepHeight;
    }

    /// <summary>
    /// Whether a body can walk the straight segment without being blocked, checked by stepping along it.
    /// </summary>
    public bool HasLineOfSight(WorldPosition from, WorldPosition to)
    {
        if (!CanOccupy(from.X, from.Z))
        {
            return false;
        }

        double deltaX = (double)to.X - from.X;
        double deltaZ = (double)to.Z - from.Z;
        double length = Math.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));

        // Also false for a non-finite end point: no segment inside the grid is longer than its two sides together.
        if (!(length <= (Columns + Rows) * (double)CellSize))
        {
            return false;
        }

        int steps = Math.Max(1, (int)Math.Ceiling(length / LineOfSightStepLength));

        float previousX = from.X;
        float previousZ = from.Z;
        for (int step = 1; step <= steps; step++)
        {
            double fraction = (double)step / steps;
            float x = (float)(from.X + (deltaX * fraction));
            float z = (float)(from.Z + (deltaZ * fraction));
            if (!CanStep(previousX, previousZ, x, z))
            {
                return false;
            }

            previousX = x;
            previousZ = z;
        }

        return true;
    }

    /// <summary>
    /// Whether a path may lead from the center of one cell to the center of a neighbouring one. It is the same
    /// walk the mover will attempt, so a body is never routed past a corner it would catch on.
    /// </summary>
    public bool CanTraverse(int fromColumn, int fromRow, int toColumn, int toRow)
    {
        int stepColumn = Math.Abs(toColumn - fromColumn);
        int stepRow = Math.Abs(toRow - fromRow);
        if (stepColumn > 1 || stepRow > 1 || (stepColumn == 0 && stepRow == 0))
        {
            return false;
        }

        if (!IsStandable(fromColumn, fromRow) || !IsStandable(toColumn, toRow))
        {
            return false;
        }

        return HasLineOfSight(GetCellCenter(fromColumn, fromRow), GetCellCenter(toColumn, toRow));
    }

    private bool IsStandable(int column, int row)
    {
        if (!Contains(column, row))
        {
            return false;
        }

        WorldPosition center = GetCellCenter(column, row);
        return CanOccupy(center.X, center.Z);
    }

    private float HeightInCell(int column, int row, float x, float z)
    {
        NavigationCell cell = GetCell(column, row);
        if (cell.Axis == RampAxis.None)
        {
            return cell.HeightAtMin;
        }

        float along = cell.Axis == RampAxis.X
            ? (x - CellMin(column, OriginX)) / CellSize
            : (z - CellMin(row, OriginZ)) / CellSize;
        float fraction = Clamp(along, 0f, 1f);
        return cell.HeightAtMin + ((cell.HeightAtMax - cell.HeightAtMin) * fraction);
    }

    private float CellMin(int index, float origin)
    {
        return origin + (index * CellSize);
    }

    private int ToIndex(float coordinate, float origin)
    {
        double index = Math.Floor(((double)coordinate - origin) / CellSize);

        // Not a number or far outside any grid: the result only has to stay outside without overflowing the cast.
        if (double.IsNaN(index) || index < -1d)
        {
            return -1;
        }

        return index > MaxCellsPerAxis ? MaxCellsPerAxis : (int)index;
    }

    private static float Clamp(float value, float minimum, float maximum)
    {
        if (value < minimum)
        {
            return minimum;
        }

        return value > maximum ? maximum : value;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsPositiveFinite(float value)
    {
        return IsFinite(value) && value > 0f;
    }
}
}
