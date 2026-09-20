using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Evertorch.Game;

namespace Evertorch.Tools
{
/// <summary>
/// Writes a navigation grid the same way into both packages: server validation and client prediction must read
/// identical geometry. Authoring symbols are not kept; each distinct cell kind gets a letter in order of first
/// appearance, which keeps the output deterministic and free of digits.
/// </summary>
internal static class NavigationJson
{
    private const string Symbols = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static void Write(Utf8JsonWriter writer, NavigationGrid grid)
    {
        List<NavigationCell> legend = new List<NavigationCell>();
        Dictionary<NavigationCell, char> symbolByCell = new Dictionary<NavigationCell, char>();
        string[] cellRows = new string[grid.Rows];
        for (int row = 0; row < grid.Rows; row++)
        {
            StringBuilder text = new StringBuilder(grid.Columns);
            for (int column = 0; column < grid.Columns; column++)
            {
                NavigationCell cell = grid.GetCell(column, row);
                if (!symbolByCell.TryGetValue(cell, out char symbol))
                {
                    symbol = Symbols[legend.Count];
                    symbolByCell.Add(cell, symbol);
                    legend.Add(cell);
                }

                text.Append(symbol);
            }

            cellRows[row] = text.ToString();
        }

        writer.WriteStartObject("navigation");
        writer.WriteNumber("cellSize", grid.CellSize);
        writer.WriteNumber("originX", grid.OriginX);
        writer.WriteNumber("originZ", grid.OriginZ);
        writer.WriteNumber("agentRadius", grid.AgentRadius);
        writer.WriteNumber("maxStepHeight", grid.MaxStepHeight);
        writer.WriteNumber("columns", grid.Columns);
        writer.WriteNumber("rows", grid.Rows);

        writer.WriteStartArray("legend");
        for (int index = 0; index < legend.Count; index++)
        {
            writer.WriteStartObject();
            writer.WriteString("symbol", Symbols[index].ToString());
            writer.WriteString("surface", EnumText.Of(legend[index].Surface));
            writer.WriteString("axis", EnumText.Of(legend[index].Axis));
            writer.WriteNumber("heightAtMin", legend[index].HeightAtMin);
            writer.WriteNumber("heightAtMax", legend[index].HeightAtMax);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        // Row 0 is the southern edge, matching NavigationGrid, not the north-first order authors write.
        writer.WriteStartArray("cellRows");
        foreach (string cellRow in cellRows)
        {
            writer.WriteStringValue(cellRow);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
}
