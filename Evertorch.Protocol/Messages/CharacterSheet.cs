using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     The receiver's own character's build (Network Protocol §6, §9): its job level, its job, which the owner's client
///     draws its body from, and its job experience, the stat and skill points it has left, each primary statistic with
///     what raising it next costs, and the derived statistics the
///     Stats window shows. Sent to its owner alone, right after <c>WorldEntered</c> in every baseline and in the tick of
///     any change. The values travel on purpose, because the player must see them (Content Pipeline §5).
/// </summary>
public sealed class CharacterSheet : IEquatable<CharacterSheet>
{
    /// <summary>
    ///     STR, AGI, VIT, INT, DEX, and LUK, in that order.
    /// </summary>
    public const int StatCount = 6;

    /// <summary>
    ///     The highest value a primary statistic reaches, where its next cost is 0 (Gameplay Systems §2).
    /// </summary>
    public const byte MaxStat = 99;

    /// <summary>
    ///     The longest sheet, with a job ID at the 64-byte limit.
    /// </summary>
    public const int MaxEncodedLength = sizeof(ushort) + sizeof(ushort) + sizeof(ushort)
        + ProtocolLimits.MaxDefinitionIdBytes + 2 * sizeof(ulong) + 2 * sizeof(ushort) + StatCount * 2 * sizeof(byte)
        + 8 * sizeof(ushort);

    public CharacterSheet(
        ushort jobLevel,
        JobDefinitionId job,
        ulong jobExperience,
        ulong jobExperienceToNextLevel,
        ushort statPoints,
        ushort skillPoints,
        IReadOnlyList<CharacterSheetStat> stats,
        ushort attack,
        ushort magicAttack,
        ushort defense,
        ushort magicDefense,
        ushort hit,
        ushort flee,
        ushort critical,
        ushort attackSpeed)
    {
        if (stats == null)
        {
            throw new ArgumentNullException(nameof(stats));
        }

        if (stats.Count != StatCount)
        {
            throw new ArgumentException($"A sheet holds exactly {StatCount} statistics.", nameof(stats));
        }

        if (job == default)
        {
            throw new ArgumentException("A job is required.", nameof(job));
        }

        JobLevel = jobLevel;
        Job = job;
        JobExperience = jobExperience;
        JobExperienceToNextLevel = jobExperienceToNextLevel;
        StatPoints = statPoints;
        SkillPoints = skillPoints;
        Stats = stats;
        Attack = attack;
        MagicAttack = magicAttack;
        Defense = defense;
        MagicDefense = magicDefense;
        Hit = hit;
        Flee = flee;
        Critical = critical;
        AttackSpeed = attackSpeed;
    }

    public ushort JobLevel { get; }

    /// <summary>
    ///     The character's job, which a job change replaces (Gameplay Systems §6.1).
    /// </summary>
    public JobDefinitionId Job { get; }

    /// <summary>
    ///     Job experience toward the next job level.
    /// </summary>
    public ulong JobExperience { get; }

    /// <summary>
    ///     What the next job level needs in all; 0 at the job's cap.
    /// </summary>
    public ulong JobExperienceToNextLevel { get; }

    public ushort StatPoints { get; }

    public ushort SkillPoints { get; }

    /// <summary>
    ///     STR, AGI, VIT, INT, DEX, and LUK, in that order.
    /// </summary>
    public IReadOnlyList<CharacterSheetStat> Stats { get; }

    public ushort Attack { get; }

    public ushort MagicAttack { get; }

    public ushort Defense { get; }

    public ushort MagicDefense { get; }

    public ushort Hit { get; }

    public ushort Flee { get; }

    /// <summary>
    ///     Tenths of a percent.
    /// </summary>
    public ushort Critical { get; }

    public ushort AttackSpeed { get; }

    public bool Equals(CharacterSheet? other)
    {
        if (other == null)
        {
            return false;
        }

        for (int index = 0; index < StatCount; index++)
        {
            if (!Stats[index].Equals(other.Stats[index]))
            {
                return false;
            }
        }

        return JobLevel == other.JobLevel
            && Job == other.Job
            && JobExperience == other.JobExperience
            && JobExperienceToNextLevel == other.JobExperienceToNextLevel
            && StatPoints == other.StatPoints
            && SkillPoints == other.SkillPoints
            && Attack == other.Attack
            && MagicAttack == other.MagicAttack
            && Defense == other.Defense
            && MagicDefense == other.MagicDefense
            && Hit == other.Hit
            && Flee == other.Flee
            && Critical == other.Critical
            && AttackSpeed == other.AttackSpeed;
    }

    public static bool TryRead(ReadOnlySpan<byte> source, out CharacterSheet? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.CharacterSheet)
            || !reader.TryReadUInt16(out ushort jobLevel)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string jobText)
            || !reader.TryReadUInt64(out ulong jobExperience)
            || !reader.TryReadUInt64(out ulong jobExperienceToNextLevel)
            || !reader.TryReadUInt16(out ushort statPoints)
            || !reader.TryReadUInt16(out ushort skillPoints)
            || jobLevel == 0
            || !JobDefinitionId.TryCreate(jobText, out JobDefinitionId job))
        {
            return false;
        }

        var stats = new CharacterSheetStat[StatCount];
        for (int index = 0; index < StatCount; index++)
        {
            // A statistic below its cap always costs something to raise, and one at the cap nothing.
            if (!reader.TryReadByte(out byte value)
                || !reader.TryReadByte(out byte nextCost)
                || value > MaxStat
                || value == MaxStat != (nextCost == 0))
            {
                return false;
            }

            stats[index] = new CharacterSheetStat(value, nextCost);
        }

        if (!reader.TryReadUInt16(out ushort attack)
            || !reader.TryReadUInt16(out ushort magicAttack)
            || !reader.TryReadUInt16(out ushort defense)
            || !reader.TryReadUInt16(out ushort magicDefense)
            || !reader.TryReadUInt16(out ushort hit)
            || !reader.TryReadUInt16(out ushort flee)
            || !reader.TryReadUInt16(out ushort critical)
            || !reader.TryReadUInt16(out ushort attackSpeed)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new CharacterSheet(
            jobLevel,
            job,
            jobExperience,
            jobExperienceToNextLevel,
            statPoints,
            skillPoints,
            stats,
            attack,
            magicAttack,
            defense,
            magicDefense,
            hit,
            flee,
            critical,
            attackSpeed);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.CharacterSheet);
        writer.WriteUInt16(JobLevel);
        writer.WriteString(Job.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt64(JobExperience);
        writer.WriteUInt64(JobExperienceToNextLevel);
        writer.WriteUInt16(StatPoints);
        writer.WriteUInt16(SkillPoints);
        foreach (CharacterSheetStat stat in Stats)
        {
            writer.WriteByte(stat.Value);
            writer.WriteByte(stat.NextCost);
        }

        writer.WriteUInt16(Attack);
        writer.WriteUInt16(MagicAttack);
        writer.WriteUInt16(Defense);
        writer.WriteUInt16(MagicDefense);
        writer.WriteUInt16(Hit);
        writer.WriteUInt16(Flee);
        writer.WriteUInt16(Critical);
        writer.WriteUInt16(AttackSpeed);
        return writer.Position;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as CharacterSheet);
    }

    public int GetEncodedLength()
    {
        return MaxEncodedLength - ProtocolLimits.MaxDefinitionIdBytes
            + WireText.GetEncodedLength(Job.Value, ProtocolLimits.MaxDefinitionIdBytes) - sizeof(ushort);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(JobLevel, Job, JobExperience, StatPoints, SkillPoints, Attack, Hit, AttackSpeed);
    }
}
}
