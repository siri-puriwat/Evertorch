using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Server
{
internal static class NavigationPackageReader
{
    /// <summary>
    ///     Returns null after reporting at least one problem.
    /// </summary>
    public static NavigationGrid? Read(PackageObjectReader map, List<string> problems)
    {
        int problemsBefore = problems.Count;
        PackageObjectReader? navigation = map.RequiredObject("navigation");
        if (navigation == null)
        {
            return null;
        }

        double cellSize = navigation.RequiredDouble("cellSize");
        double originX = navigation.RequiredDouble("originX");
        double originZ = navigation.RequiredDouble("originZ");
        double agentRadius = navigation.RequiredDouble("agentRadius");
        double maxStepHeight = navigation.RequiredDouble("maxStepHeight");
        int columns = navigation.RequiredInt("columns", 1);
        int rows = navigation.RequiredInt("rows", 1);

        var legend = new Dictionary<char, NavigationCell>();
        foreach (PackageObjectReader entry in navigation.RequiredObjectArray("legend"))
        {
            ReadLegendEntry(entry, legend);
        }

        IReadOnlyList<string> cellRows = navigation.RequiredStringArray("cellRows");
        navigation.ReportUnexpectedProperties();
        if (problems.Count != problemsBefore)
        {
            return null;
        }

        if (columns > NavigationGrid.MaxCellsPerAxis || rows > NavigationGrid.MaxCellsPerAxis)
        {
            navigation.Report("columns", "a grid may have at most 512 cells along each axis");
            return null;
        }

        NavigationCell[]? cells = ReadCells(navigation, cellRows, legend, columns, rows);
        if (cells == null)
        {
            return null;
        }

        try
        {
            return new NavigationGrid(
                columns,
                rows,
                (float)cellSize,
                (float)originX,
                (float)originZ,
                (float)agentRadius,
                (float)maxStepHeight,
                cells);
        }
        catch (ArgumentException exception)
        {
            map.Report("navigation", exception.Message);
            return null;
        }
    }

    private static void ReadLegendEntry(PackageObjectReader entry, Dictionary<char, NavigationCell> legend)
    {
        string symbol = entry.RequiredString("symbol");
        NavigationSurface surface = entry.RequiredEnum<NavigationSurface>("surface");
        RampAxis axis = entry.RequiredEnum<RampAxis>("axis");
        double heightAtMin = entry.RequiredDouble("heightAtMin");
        double heightAtMax = entry.RequiredDouble("heightAtMax");
        entry.ReportUnexpectedProperties();

        if (symbol.Length != 1)
        {
            entry.Report("symbol", "must be a single character");
            return;
        }

        if (legend.ContainsKey(symbol[0]))
        {
            entry.Report("symbol", "'" + symbol + "' appears more than once in the legend");
            return;
        }

        try
        {
            legend.Add(symbol[0], new NavigationCell(surface, axis, (float)heightAtMin, (float)heightAtMax));
        }
        catch (ArgumentException exception)
        {
            entry.Report("heightAtMax", exception.Message);
        }
    }

    private static NavigationCell[]? ReadCells(
        PackageObjectReader navigation,
        IReadOnlyList<string> cellRows,
        Dictionary<char, NavigationCell> legend,
        int columns,
        int rows)
    {
        if (cellRows.Count != rows)
        {
            navigation.Report("cellRows", "has " + cellRows.Count + " rows but rows is " + rows);
            return null;
        }

        var cells = new NavigationCell[columns * rows];
        for (int row = 0; row < rows; row++)
        {
            string cellRow = cellRows[row];
            if (cellRow.Length != columns)
            {
                navigation.Report(
                    "cellRows[" + row + "]",
                    "has " + cellRow.Length + " symbols but columns is " + columns);
                return null;
            }

            for (int column = 0; column < columns; column++)
            {
                if (!legend.TryGetValue(cellRow[column], out NavigationCell cell))
                {
                    navigation.Report("cellRows[" + row + "]", "symbol '" + cellRow[column] + "' is not in the legend");
                    return null;
                }

                cells[row * columns + column] = cell;
            }
        }

        return cells;
    }
}
}
