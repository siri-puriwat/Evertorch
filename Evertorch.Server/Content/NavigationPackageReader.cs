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

        double cellSize = AtMost(navigation, "cellSize", ContentLimits.MaxCellSize);
        double originX = Coordinate(navigation, "originX");
        double originZ = Coordinate(navigation, "originZ");
        double agentRadius = AtMost(navigation, "agentRadius", ContentLimits.MaxAgentRadius);
        double maxStepHeight = AtMost(navigation, "maxStepHeight", ContentLimits.MaxStepHeight);
        int columns = navigation.RequiredInt("columns", 1);
        int rows = navigation.RequiredInt("rows", 1);

        var legend = new Dictionary<char, NavigationCell>();
        IReadOnlyList<PackageObjectReader> entries = navigation.RequiredObjectArray("legend");
        if (entries.Count > ContentLimits.MaxLegendEntries)
        {
            navigation.Report("legend", $"has more than {ContentLimits.MaxLegendEntries} entries");
        }

        foreach (PackageObjectReader entry in entries)
        {
            ReadLegendEntry(entry, legend, problems);
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

    private static void ReadLegendEntry(
        PackageObjectReader entry,
        Dictionary<char, NavigationCell> legend,
        List<string> problems)
    {
        int problemsBefore = problems.Count;
        string symbol = entry.RequiredString("symbol");
        NavigationSurface surface = entry.RequiredEnum<NavigationSurface>("surface");
        RampAxis axis = entry.RequiredEnum<RampAxis>("axis");
        double heightAtMin = Coordinate(entry, "heightAtMin");
        double heightAtMax = Coordinate(entry, "heightAtMax");
        entry.ReportUnexpectedProperties();
        if (problems.Count != problemsBefore)
        {
            return;
        }

        // Only floor can be walked on, so a ramp of anything else would be a slope nobody could climb.
        if (axis != RampAxis.None && surface != NavigationSurface.Floor)
        {
            entry.Report("surface", "a ramp must be floor");
            return;
        }

        if (symbol.Length != 1)
        {
            entry.Report("symbol", "must be a single character");
            return;
        }

        if (legend.ContainsKey(symbol[0]))
        {
            entry.Report("symbol", $"'{symbol}' appears more than once in the legend");
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

    // The tools' upper bounds; a size of 0 or less is the grid's to refuse.
    private static double AtMost(PackageObjectReader reader, string name, double maximum)
    {
        double value = reader.RequiredDouble(name);
        if (value > maximum)
        {
            reader.Report(name, $"must be at most {maximum}");
        }

        return value;
    }

    private static double Coordinate(PackageObjectReader reader, string name)
    {
        double value = reader.RequiredDouble(name);
        if (Math.Abs(value) > ContentLimits.MaxCoordinate)
        {
            reader.Report(name, $"must be between -{ContentLimits.MaxCoordinate} and {ContentLimits.MaxCoordinate}");
        }

        return value;
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
            navigation.Report("cellRows", $"has {cellRows.Count} rows but rows is {rows}");
            return null;
        }

        var cells = new NavigationCell[columns * rows];
        for (int row = 0; row < rows; row++)
        {
            string cellRow = cellRows[row];
            if (cellRow.Length != columns)
            {
                navigation.Report($"cellRows[{row}]", $"has {cellRow.Length} symbols but columns is {columns}");
                return null;
            }

            for (int column = 0; column < columns; column++)
            {
                if (!legend.TryGetValue(cellRow[column], out NavigationCell cell))
                {
                    navigation.Report($"cellRows[{row}]", $"symbol '{cellRow[column]}' is not in the legend");
                    return null;
                }

                cells[row * columns + column] = cell;
            }
        }

        return cells;
    }
}
}
