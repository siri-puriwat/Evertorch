using Evertorch.Game;

namespace Evertorch.Client.Tests.EditMode
{
internal static class ClientTestGrids
{
    public const float Speed = 5f;

    public const float TickSeconds = 0.05f;

    // One-metre cells, origin at zero, north row first. 'o' is a sealed-off pocket nobody can walk into.
    public static readonly string[] Yard =
    {
        "############",
        "#..........#",
        "#..........#",
        "#....#.....#",
        "#....#.....#",
        "#....#..####",
        "#....#..#o##",
        "#.......####",
        "#..........#",
        "############"
    };

    public static NavigationGrid CreateYard()
    {
        return Create(Yard);
    }

    public static WorldPosition Center(int column, int row)
    {
        return new WorldPosition(column + 0.5f, 0f, row + 0.5f);
    }

    private static NavigationGrid Create(string[] northFirstRows)
    {
        int rows = northFirstRows.Length;
        int columns = northFirstRows[0].Length;
        var cells = new NavigationCell[rows * columns];
        for (int row = 0; row < rows; row++)
        {
            string text = northFirstRows[rows - 1 - row];
            for (int column = 0; column < columns; column++)
            {
                NavigationSurface surface = text[column] == '.' || text[column] == 'o'
                    ? NavigationSurface.Floor
                    : NavigationSurface.Wall;
                cells[row * columns + column] = NavigationCell.Level(surface, 0f);
            }
        }

        return new NavigationGrid(columns, rows, 1f, 0f, 0f, 0.3f, 0.4f, cells);
    }
}
}
