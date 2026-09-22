using System;

namespace Evertorch.Game.Tests
{
/// <summary>
///     Builds small grids from text. Rows are written north (largest Z) first, one character per 1 m cell, with the
///     south-west corner at the world origin. Legend: <c>.</c> floor at 0, <c>^</c> floor at 1, <c>#</c> wall,
///     <c>o</c> obstacle, <c>N</c> NPC marker, <c>G</c> gate, <c>&gt;</c> ramp rising east from 0 to 1,
///     <c>&lt;</c> ramp rising west, <c>n</c> ramp rising north, <c>s</c> ramp rising south.
/// </summary>
internal static class TestGrids
{
    public const float AgentRadius = 0.3f;
    public const float MaxStepHeight = 0.4f;

    public static NavigationGrid FromRows(params string[] northFirstRows)
    {
        int rows = northFirstRows.Length;
        int columns = northFirstRows[0].Length;
        var cells = new NavigationCell[rows * columns];
        for (int row = 0; row < rows; row++)
        {
            string text = northFirstRows[rows - 1 - row];
            if (text.Length != columns)
            {
                throw new ArgumentException("Every row must have the same length.");
            }

            for (int column = 0; column < columns; column++)
            {
                cells[row * columns + column] = ToCell(text[column]);
            }
        }

        return new NavigationGrid(columns, rows, 1f, 0f, 0f, AgentRadius, MaxStepHeight, cells);
    }

    /// <summary>
    ///     The center of a cell, addressed as in the text: column from the west, row from the south.
    /// </summary>
    public static WorldPosition Center(NavigationGrid grid, int column, int row)
    {
        return grid.GetCellCenter(column, row);
    }

    private static NavigationCell ToCell(char symbol)
    {
        switch (symbol)
        {
            case '.':
                return NavigationCell.Level(NavigationSurface.Floor, 0f);
            case '^':
                return NavigationCell.Level(NavigationSurface.Floor, 1f);
            case '#':
                return NavigationCell.Level(NavigationSurface.Wall, 0f);
            case 'o':
                return NavigationCell.Level(NavigationSurface.Obstacle, 0f);
            case 'N':
                return NavigationCell.Level(NavigationSurface.NpcMarker, 0f);
            case 'G':
                return NavigationCell.Level(NavigationSurface.Gate, 0f);
            case '>':
                return NavigationCell.Ramp(RampAxis.X, 0f, 1f);
            case '<':
                return NavigationCell.Ramp(RampAxis.X, 1f, 0f);
            case 'n':
                return NavigationCell.Ramp(RampAxis.Z, 0f, 1f);
            case 's':
                return NavigationCell.Ramp(RampAxis.Z, 1f, 0f);
            default:
                throw new ArgumentException($"Unknown grid symbol '{symbol}'.");
        }
    }
}
}
