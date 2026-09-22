using System;
using System.Collections.Generic;
using System.Globalization;
using Evertorch.Game;

namespace Evertorch.Tools
{
/// <summary>
///     Reads a map's <c>navigation</c> block: a legend of cell kinds and the rows of symbols drawn with it, written
///     with the northern row first so the text reads like a map.
/// </summary>
internal static class NavigationReader
{
    public static NavigationGrid? Read(YamlFieldReader navigation, List<ContentDiagnostic> diagnostics)
    {
        int errorsBefore = diagnostics.Count;

        double cellSize = navigation.RequiredDouble("cellSize", 0d, ContentLimits.MaxCellSize, true);
        YamlFieldReader origin = navigation.RequiredMapping("origin");
        double originX = origin.RequiredDouble("x", -ContentLimits.MaxCoordinate, ContentLimits.MaxCoordinate, false);
        double originZ = origin.RequiredDouble("z", -ContentLimits.MaxCoordinate, ContentLimits.MaxCoordinate, false);
        double agentRadius = navigation.RequiredDouble("agentRadius", 0d, ContentLimits.MaxAgentRadius, true);
        double maxStepHeight = navigation.RequiredDouble("maxStepHeight", 0d, ContentLimits.MaxStepHeight, false);

        bool hasShape = diagnostics.Count == errorsBefore;
        double maxRampSlope = hasShape
            ? NavigationGrid.MaxRampSlope((float)agentRadius, (float)maxStepHeight)
            : double.PositiveInfinity;

        Dictionary<char, NavigationCell> legend = ReadLegend(navigation, cellSize, maxRampSlope);
        IReadOnlyList<string> rows = navigation.RequiredStringSequence("rows");
        if (diagnostics.Count != errorsBefore || !CheckRows(navigation, rows, legend))
        {
            return null;
        }

        int columns = rows[0].Length;
        var cells = new NavigationCell[rows.Count * columns];
        for (int row = 0; row < rows.Count; row++)
        {
            string northFirstRow = rows[rows.Count - 1 - row];
            for (int column = 0; column < columns; column++)
            {
                cells[row * columns + column] = legend[northFirstRow[column]];
            }
        }

        return new NavigationGrid(
            columns,
            rows.Count,
            (float)cellSize,
            (float)originX,
            (float)originZ,
            (float)agentRadius,
            (float)maxStepHeight,
            cells);
    }

    private static Dictionary<char, NavigationCell> ReadLegend(
        YamlFieldReader navigation,
        double cellSize,
        double maxRampSlope)
    {
        var legend = new Dictionary<char, NavigationCell>();
        IReadOnlyList<YamlFieldReader> entries = navigation.RequiredMappingSequence("legend");
        if (entries.Count > ContentLimits.MaxLegendEntries)
        {
            navigation.ReportField(
                "legend",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "must not have more than {0} entries",
                    ContentLimits.MaxLegendEntries));
        }

        foreach (YamlFieldReader entry in entries)
        {
            string symbol = entry.RequiredString("symbol");
            NavigationSurface surface = entry.RequiredEnum<NavigationSurface>("surface");
            NavigationCell? cell = ReadCell(entry, surface, cellSize, maxRampSlope);

            if (symbol.Length > 1)
            {
                entry.ReportField("symbol", "must be a single character");
            }
            else if (symbol.Length == 1 && legend.ContainsKey(symbol[0]))
            {
                entry.ReportField("symbol", "'" + symbol + "' is already in the legend");
            }
            else if (symbol.Length == 1 && cell.HasValue)
            {
                legend.Add(symbol[0], cell.Value);
            }
        }

        return legend;
    }

    private static NavigationCell? ReadCell(
        YamlFieldReader entry,
        NavigationSurface surface,
        double cellSize,
        double maxRampSlope)
    {
        bool hasHeight = entry.Has("height");
        bool hasRamp = entry.Has("ramp");
        if (hasHeight == hasRamp)
        {
            entry.ReportField(hasHeight ? "ramp" : "height", "exactly one of height and ramp is required");
            return null;
        }

        if (hasHeight)
        {
            double height = entry.RequiredDouble(
                "height",
                -ContentLimits.MaxCoordinate,
                ContentLimits.MaxCoordinate,
                false);
            return NavigationCell.Level(surface, (float)height);
        }

        YamlFieldReader ramp = entry.RequiredMapping("ramp");
        RampAxis axis = ramp.RequiredEnum<RampAxis>("axis");
        double from = ramp.RequiredDouble("from", -ContentLimits.MaxCoordinate, ContentLimits.MaxCoordinate, false);
        double to = ramp.RequiredDouble("to", -ContentLimits.MaxCoordinate, ContentLimits.MaxCoordinate, false);

        bool isValid = true;
        if (axis == RampAxis.None)
        {
            ramp.ReportField("axis", "must be x or z");
            isValid = false;
        }

        if (surface != NavigationSurface.Floor)
        {
            entry.ReportField("surface", "only floor can be a ramp");
            isValid = false;
        }

        if (cellSize > 0d && Math.Abs(to - from) / cellSize > maxRampSlope)
        {
            ramp.ReportField(
                "to",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "ramp is too steep: rise over length must not exceed {0:0.###} for this agentRadius and maxStepHeight",
                    maxRampSlope));
            isValid = false;
        }

        return isValid
            ? new NavigationCell(NavigationSurface.Floor, axis, (float)from, (float)to)
            : null;
    }

    private static bool CheckRows(
        YamlFieldReader navigation,
        IReadOnlyList<string> rows,
        Dictionary<char, NavigationCell> legend)
    {
        if (rows.Count == 0 || rows.Count > NavigationGrid.MaxCellsPerAxis)
        {
            navigation.ReportField("rows", "must have between 1 and 512 rows");
            return false;
        }

        bool isValid = true;
        int columns = rows[0].Length;
        if (columns == 0 || columns > NavigationGrid.MaxCellsPerAxis)
        {
            navigation.ReportField("rows[0]", "must have between 1 and 512 symbols");
            return false;
        }

        for (int index = 0; index < rows.Count; index++)
        {
            string row = rows[index];
            string field = string.Format(CultureInfo.InvariantCulture, "rows[{0}]", index);
            if (row.Length != columns)
            {
                navigation.ReportField(
                    field,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "has {0} symbols but the first row has {1}",
                        row.Length,
                        columns));
                isValid = false;
                continue;
            }

            for (int column = 0; column < row.Length; column++)
            {
                if (!legend.ContainsKey(row[column]))
                {
                    navigation.ReportField(
                        field,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "symbol '{0}' at position {1} is not in the legend",
                            row[column],
                            column + 1));
                    isValid = false;
                    break;
                }
            }
        }

        return isValid;
    }
}
}
