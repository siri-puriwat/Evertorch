using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;
using Microsoft.Extensions.Logging;

namespace Evertorch.Server
{
/// <summary>
///     A character's build (Gameplay Systems §2, §9): the stat points its base levels grant less what raising its primary
///     statistics above its job's start costs, the skill points its job levels grant less the levels it has learned,
///     and the reset of a build that spends more than it has. Neither pool is stored (Persistence §4). Tick thread
///     only.
/// </summary>
public sealed class CharacterBuilds
{
    /// <summary>
    ///     Why a load found a build it had to reset (Persistence §6).
    /// </summary>
    public const string OverspentReason = "overspent";

    /// <summary>
    ///     Why the Guildmaster reset a build (Gameplay Systems §6.1).
    /// </summary>
    public const string GuildmasterReason = "guildmaster";

    private static readonly Action<ILogger, long, long, PrimaryStat, int, int, Exception?> LogStatRaised =
        LoggerMessage.Define<long, long, PrimaryStat, int, int>(
            LogLevel.Information,
            new EventId(1012, "StatRaised"),
            "Character {Character} on connection {Connection} raised {Stat} from {Previous} to {Value}.");

    private static readonly Action<ILogger, long, long, SkillDefinitionId, int, Exception?> LogSkillLearned =
        LoggerMessage.Define<long, long, SkillDefinitionId, int>(
            LogLevel.Information,
            new EventId(1013, "SkillLearned"),
            "Character {Character} on connection {Connection} learned {Skill} at level {Level}.");

    private static readonly Action<ILogger, long, long, string, Exception?> LogBuildReset =
        LoggerMessage.Define<long, long, string>(
            LogLevel.Information,
            new EventId(1014, "BuildReset"),
            "Build of character {Character} on connection {Connection} reset ({Reason}): statistics back to the job's "
            + "start and every skill forgotten.");

    private readonly ServerContent m_content;
    private readonly IProgressionRules m_rules;
    private readonly CharacterStats m_stats;
    private readonly MessageSender m_sender;
    private readonly ServerInstruments m_instruments;
    private readonly ILogger<CharacterBuilds> m_logger;

    public CharacterBuilds(
        ServerContent content,
        IProgressionRules rules,
        CharacterStats stats,
        MessageSender sender,
        ServerInstruments instruments,
        ILogger<CharacterBuilds> logger)
    {
        m_content = content;
        m_rules = rules;
        m_stats = stats;
        m_sender = sender;
        m_instruments = instruments;
        m_logger = logger;
    }

    /// <summary>
    ///     The stat points <paramref name="player" /> has left, never below 0.
    /// </summary>
    public int StatPointsLeft(PlayerEntity player)
    {
        int spent = StatPointsSpent(m_content.Jobs[player.Job].StartingStats, player.Primary);
        return Math.Max(0, m_rules.StatPointsGranted(player.Level) - Math.Max(0, spent));
    }

    /// <summary>
    ///     The skill points <paramref name="player" /> has left, never below 0.
    /// </summary>
    public int SkillPointsLeft(PlayerEntity player)
    {
        return Math.Max(0, m_rules.SkillPointsGranted(player.JobLevel) - SkillPointsSpent(player));
    }

    /// <summary>
    ///     Raises <paramref name="stat" /> by <paramref name="steps" /> with stat points, whole or not at all (Gameplay
    ///     Systems §2): refused with <see cref="CommandRejectionReason.RequirementNotMet" /> when a raise would pass the
    ///     cap, then with <see cref="CommandRejectionReason.NotEnoughPoints" /> when the points left do not cover them.
    ///     The statistics are derived once; the owner hears of new maximums at once, and of the rest through its
    ///     sheet.
    /// </summary>
    public CommandRejectionReason TryRaise(PlayerEntity player, ConnectionId connection, PrimaryStat stat, int steps)
    {
        int value = ValueOf(player.Primary, stat);
        if (steps < 1 || value + steps > m_rules.StatCap)
        {
            return CommandRejectionReason.RequirementNotMet;
        }

        int cost = 0;
        for (int raised = value; raised < value + steps; raised++)
        {
            cost += m_rules.StatRaiseCost(raised);
        }

        if (cost > StatPointsLeft(player))
        {
            return CommandRejectionReason.NotEnoughPoints;
        }

        int maxHealth = player.MaxHealth;
        int maxSpirit = player.MaxSpirit;
        player.SetPrimary(With(player.Primary, stat, value + steps));
        m_stats.Recalculate(player, m_content.Jobs[player.Job]);
        if (player.MaxHealth != maxHealth || player.MaxSpirit != maxSpirit)
        {
            m_sender.SendHealth(player);
        }

        LogStatRaised(m_logger, player.Character.Value, connection.Value, stat, value, value + steps, null);
        m_instruments.RecordBuildChange(BuildChange.Stat);
        return CommandRejectionReason.None;
    }

    /// <summary>
    ///     Learns one level of <paramref name="skill" /> with a skill point (Gameplay Systems §9): refused with
    ///     <see cref="CommandRejectionReason.RequirementNotMet" /> for a skill outside the job's tree, at its maximum, or
    ///     whose prerequisite is not met, then with <see cref="CommandRejectionReason.NotEnoughPoints" /> when no point
    ///     is left. A cast under way keeps the level it began at.
    /// </summary>
    public CommandRejectionReason TryLearn(PlayerEntity player, ConnectionId connection, SkillDefinitionId skill)
    {
        if (!Contains(m_content.Jobs[player.Job].Skills, skill))
        {
            return CommandRejectionReason.RequirementNotMet;
        }

        SkillDefinition definition = m_content.Skills[skill];
        player.Skills.TryGetValue(skill, out int level);
        SkillRequirement? requires = definition.Requires;
        int required = 0;
        if (requires != null)
        {
            player.Skills.TryGetValue(requires.Skill, out required);
        }

        if (level >= definition.MaxLevel || (requires != null && required < requires.Level))
        {
            return CommandRejectionReason.RequirementNotMet;
        }

        if (SkillPointsLeft(player) < 1)
        {
            return CommandRejectionReason.NotEnoughPoints;
        }

        player.SetSkillLevel(skill, level + 1);
        LogSkillLearned(m_logger, player.Character.Value, connection.Value, skill, level + 1, null);
        m_instruments.RecordBuildChange(BuildChange.Skill);
        return CommandRejectionReason.None;
    }

    /// <summary>
    ///     What the owner's Stats window shows of <paramref name="player" /> (Network Protocol §9): its job progress,
    ///     both pools, each statistic with its next cost (0 at the cap), and the derived statistics, each held to what
    ///     its field carries.
    /// </summary>
    public CharacterSheet SheetOf(PlayerEntity player)
    {
        JobDefinition job = m_content.Jobs[player.Job];
        ExperienceTableDefinition table = m_content.ExperienceTables[job.JobExperienceTable];
        PrimaryStats primary = player.Primary;
        int[] values = { primary.Str, primary.Agi, primary.Vit, primary.Int, primary.Dex, primary.Luk };
        var stats = new CharacterSheetStat[CharacterSheet.StatCount];
        for (int index = 0; index < stats.Length; index++)
        {
            int value = Math.Min(Math.Max(values[index], 0), m_rules.StatCap);
            int cost = value >= m_rules.StatCap ? 0 : m_rules.StatRaiseCost(value);
            stats[index] = new CharacterSheetStat((byte)value, (byte)Math.Min(byte.MaxValue, cost));
        }

        DerivedStats derived = player.Stats;
        return new CharacterSheet(
            (byte)Math.Min(byte.MaxValue, player.JobLevel),
            (ulong)Math.Max(0, player.JobExperience),
            (ulong)m_rules.ExperienceToNextLevel(table, player.JobLevel),
            (ushort)Math.Min(ushort.MaxValue, StatPointsLeft(player)),
            (byte)Math.Min(byte.MaxValue, SkillPointsLeft(player)),
            stats,
            Clamp(derived.PhysicalAttack),
            Clamp(derived.MagicalAttack),
            Clamp(derived.SoftDefense),
            Clamp(derived.SoftMagicDefense),
            Clamp(derived.Hit),
            Clamp(derived.Flee),
            Clamp(derived.Critical),
            Clamp(derived.AttackSpeed));
    }

    /// <summary>
    ///     Whether <paramref name="player" />'s build spends more than its levels grant, holds a statistic below its job's
    ///     start, or has learned a skill its job's tree lacks or above the skill's maximum: what only a hand-edited row
    ///     or changed content leaves.
    /// </summary>
    public bool IsOverspent(PlayerEntity player)
    {
        JobDefinition job = m_content.Jobs[player.Job];
        int statsSpent = StatPointsSpent(job.StartingStats, player.Primary);
        if (statsSpent < 0 || statsSpent > m_rules.StatPointsGranted(player.Level))
        {
            return true;
        }

        foreach (KeyValuePair<SkillDefinitionId, int> learned in player.Skills)
        {
            if (!Contains(job.Skills, learned.Key) || learned.Value > m_content.Skills[learned.Key].MaxLevel)
            {
                return true;
            }
        }

        return SkillPointsSpent(player) > m_rules.SkillPointsGranted(player.JobLevel);
    }

    /// <summary>
    ///     Resets <paramref name="player" />'s build when <see cref="IsOverspent" />, logs it, and says whether it did.
    /// </summary>
    public bool ResetIfOverspent(PlayerEntity player, ConnectionId connection)
    {
        if (!IsOverspent(player))
        {
            return false;
        }

        Reset(player);
        LogBuildReset(m_logger, player.Character.Value, connection.Value, OverspentReason, null);
        return true;
    }

    /// <summary>
    ///     The Guildmaster's reset (Gameplay Systems §6.1): every point back for free, the owner told of new maximums at
    ///     once and of the rest through its sheet, logged and counted. The caller interrupts a cast and queues the
    ///     checkpoint.
    /// </summary>
    public void ResetAtGuildmaster(PlayerEntity player, ConnectionId connection)
    {
        int maxHealth = player.MaxHealth;
        int maxSpirit = player.MaxSpirit;
        Reset(player);
        if (player.MaxHealth != maxHealth || player.MaxSpirit != maxSpirit)
        {
            m_sender.SendHealth(player);
        }

        LogBuildReset(m_logger, player.Character.Value, connection.Value, GuildmasterReason, null);
        m_instruments.RecordBuildChange(BuildChange.Reset);
    }

    /// <summary>
    ///     Returns every point: the primary statistics go back to the job's start and every skill to level 0, and the
    ///     statistics are derived again.
    /// </summary>
    public void Reset(PlayerEntity player)
    {
        JobDefinition job = m_content.Jobs[player.Job];
        player.SetPrimary(job.StartingStats);
        player.ForgetSkills();
        m_stats.Recalculate(player, job);
    }

    private static int ValueOf(PrimaryStats primary, PrimaryStat stat)
    {
        return stat switch
        {
            PrimaryStat.Str => primary.Str,
            PrimaryStat.Agi => primary.Agi,
            PrimaryStat.Vit => primary.Vit,
            PrimaryStat.Int => primary.Int,
            PrimaryStat.Dex => primary.Dex,
            PrimaryStat.Luk => primary.Luk,
            _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, "Not a primary statistic.")
        };
    }

    private static PrimaryStats With(PrimaryStats primary, PrimaryStat stat, int value)
    {
        return new PrimaryStats(
            stat == PrimaryStat.Str ? value : primary.Str,
            stat == PrimaryStat.Agi ? value : primary.Agi,
            stat == PrimaryStat.Vit ? value : primary.Vit,
            stat == PrimaryStat.Int ? value : primary.Int,
            stat == PrimaryStat.Dex ? value : primary.Dex,
            stat == PrimaryStat.Luk ? value : primary.Luk);
    }

    private static ushort Clamp(int value)
    {
        return (ushort)Math.Min(ushort.MaxValue, Math.Max(0, value));
    }

    private static bool Contains(IReadOnlyList<SkillDefinitionId> skills, SkillDefinitionId skill)
    {
        foreach (SkillDefinitionId listed in skills)
        {
            if (listed == skill)
            {
                return true;
            }
        }

        return false;
    }

    private static int SkillPointsSpent(PlayerEntity player)
    {
        int spent = 0;
        foreach (int level in player.Skills.Values)
        {
            spent += level;
        }

        return spent;
    }

    // What raising each statistic from its start to its value cost; -1 when one is below its start.
    private int StatPointsSpent(PrimaryStats start, PrimaryStats current)
    {
        int[] starts = { start.Str, start.Agi, start.Vit, start.Int, start.Dex, start.Luk };
        int[] values = { current.Str, current.Agi, current.Vit, current.Int, current.Dex, current.Luk };
        int spent = 0;
        for (int index = 0; index < starts.Length; index++)
        {
            if (values[index] < starts[index])
            {
                return -1;
            }

            for (int value = starts[index]; value < values[index]; value++)
            {
                spent += m_rules.StatRaiseCost(value);
            }
        }

        return spent;
    }
}
}
