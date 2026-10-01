using System.Collections.Generic;
using System.Globalization;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The client's words for a character's build (Prototype Content §2), composed from the values of
///     <see cref="CharacterSheet" /> and <see cref="SkillList" />: the Stats and Skills windows' rows and the chat's
///     system
///     lines for what a new sheet or skill list raised.
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
    ///     The chat's system lines for what <paramref name="after" /> raised over <paramref name="before" />: "Job level 3."
    ///     and "AGI 6.", each statistic that rose with its new value, or "Every point returned." for the Guildmaster's
    ///     reset, the only change that lowers a statistic or gives skill points back without a job level. A world's
    ///     first sheet, with no <paramref name="before" />, is its baseline and says nothing.
    /// </summary>
    public static IReadOnlyList<string> Describe(
        CharacterSheet? before,
        CharacterSheet after,
        ClientContent? content = null)
    {
        var lines = new List<string>();
        if (before == null)
        {
            return lines;
        }

        // A job change says so, and nothing of the job level that fell with it (Prototype Content §2).
        if (after.Job != before.Job)
        {
            lines.Add($"You are now {WithArticle(JobName(content, after.Job))}.");
            return lines;
        }

        if (IsReset(before, after))
        {
            lines.Add("Every point returned.");
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

    public static string JobName(ClientContent? content, JobDefinitionId job)
    {
        return content != null && content.TryGetJob(job, out ClientJob? found) && found != null
            ? found.DisplayName
            : job.Value;
    }

    /// <summary>
    ///     "a Vanguard", "an Arcanist".
    /// </summary>
    public static string WithArticle(string name)
    {
        return name.Length > 0 && "AEIOUaeiou".IndexOf(name[0]) >= 0 ? $"an {name}" : $"a {name}";
    }

    public static string SkillName(ClientContent? content, SkillDefinitionId skill)
    {
        return content != null && content.TryGetSkill(skill, out ClientSkill? found) && found != null
            ? found.DisplayName
            : skill.Value;
    }

    /// <summary>
    ///     "Lv 1/5": the level learned of the skill's maximum.
    /// </summary>
    public static string SkillLevel(SkillListEntry entry)
    {
        return $"Lv {Number(entry.Level)}/{Number(entry.MaxLevel)}";
    }

    /// <summary>
    ///     "Needs Strike Lv 1" while <paramref name="entry" />'s prerequisite in <paramref name="tree" /> is below its
    ///     level; null once it is met, or without one.
    /// </summary>
    public static string? UnmetPrerequisite(
        IReadOnlyList<SkillListEntry> tree,
        SkillListEntry entry,
        ClientContent? content)
    {
        if (entry.PrerequisiteIndex >= tree.Count)
        {
            return null;
        }

        SkillListEntry required = tree[entry.PrerequisiteIndex];
        return required.Level >= entry.PrerequisiteLevel
            ? null
            : $"Needs {SkillName(content, required.Skill)} Lv {Number(entry.PrerequisiteLevel)}";
    }

    /// <summary>
    ///     The chat's system lines for what <paramref name="after" /> raised over <paramref name="before" />: "Strike Lv 2."
    ///     for each skill whose level rose. A world's first list, with no <paramref name="before" />, says nothing.
    /// </summary>
    public static IReadOnlyList<string> DescribeSkills(
        IReadOnlyList<SkillListEntry>? before,
        IReadOnlyList<SkillListEntry> after,
        ClientContent? content)
    {
        var lines = new List<string>();
        if (before == null)
        {
            return lines;
        }

        foreach (SkillListEntry entry in after)
        {
            if (entry.Level > LevelIn(before, entry.Skill))
            {
                lines.Add($"{SkillName(content, entry.Skill)} Lv {Number(entry.Level)}.");
            }
        }

        return lines;
    }

    private static bool IsReset(CharacterSheet before, CharacterSheet after)
    {
        for (int index = 0; index < CharacterSheet.StatCount; index++)
        {
            if (after.Stats[index].Value < before.Stats[index].Value)
            {
                return true;
            }
        }

        return after.SkillPoints > before.SkillPoints && after.JobLevel == before.JobLevel;
    }

    private static int LevelIn(IReadOnlyList<SkillListEntry> list, SkillDefinitionId skill)
    {
        foreach (SkillListEntry entry in list)
        {
            if (entry.Skill == skill)
            {
                return entry.Level;
            }
        }

        return 0;
    }

    private static string Number(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
}
