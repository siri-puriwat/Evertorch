using System.Collections.Generic;
using System.Globalization;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The client's words for a character's build (Prototype Content §2), composed from the values of
///     <see cref="CharacterSheet" />: the Stats window's rows and the feedback lines for what a new sheet raised.
/// </summary>
public static class BuildMessages
{
    private static readonly string[] StatNames = { "STR", "AGI", "VIT", "INT", "DEX", "LUK" };

    public static string StatName(PrimaryStat stat)
    {
        int index = (int)stat - 1;
        return index >= 0 && index < StatNames.Length ? StatNames[index] : stat.ToString();
    }

    /// <summary>
    ///     "STR 5" and "next 2", or "max" at the cap.
    /// </summary>
    public static string StatValue(PrimaryStat stat, CharacterSheetStat value)
    {
        return $"{StatName(stat)} {Number(value.Value)}";
    }

    public static string NextCost(CharacterSheetStat value)
    {
        return value.NextCost == 0 ? "max" : $"next {Number(value.NextCost)}";
    }

    public static string Points(int points)
    {
        return $"Points: {Number(points)}";
    }

    /// <summary>
    ///     The derived statistics in the Stats window's order, as label and value pairs; the critical chance in percent
    ///     with one decimal.
    /// </summary>
    public static IReadOnlyList<string> Derived(CharacterSheet sheet)
    {
        string critical = (sheet.Critical / 10.0).ToString("0.0", CultureInfo.InvariantCulture);
        return new[]
        {
            $"Attack {Number(sheet.Attack)}", $"Magic attack {Number(sheet.MagicAttack)}",
            $"Defense {Number(sheet.Defense)}", $"Magic defense {Number(sheet.MagicDefense)}",
            $"Hit {Number(sheet.Hit)}", $"Flee {Number(sheet.Flee)}", $"Critical {critical}%",
            $"Attack speed {Number(sheet.AttackSpeed)}"
        };
    }

    /// <summary>
    ///     The feedback lines for what <paramref name="after" /> raised over <paramref name="before" />: "Job level 3."
    ///     and "AGI 6.", each statistic that rose with its new value. A world's first sheet, with no
    ///     <paramref name="before" />, is its baseline and says nothing.
    /// </summary>
    public static IReadOnlyList<string> Describe(CharacterSheet? before, CharacterSheet after)
    {
        var lines = new List<string>();
        if (before == null)
        {
            return lines;
        }

        if (after.JobLevel > before.JobLevel)
        {
            lines.Add($"Job level {Number(after.JobLevel)}.");
        }

        for (int index = 0; index < CharacterSheet.StatCount; index++)
        {
            byte value = after.Stats[index].Value;
            if (value > before.Stats[index].Value)
            {
                lines.Add($"{StatNames[index]} {Number(value)}.");
            }
        }

        return lines;
    }

    private static string Number(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
}
