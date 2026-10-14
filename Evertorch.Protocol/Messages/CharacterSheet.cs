using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     The receiver's own character's build (Network Protocol §6, §9): its job level, its job, which the owner's client
///     draws its body from, and its job experience, the stat and skill points it has left, each primary statistic with
///     what raising it next costs, the derived statistics the Stats window shows, what the worn equipment adds to
///     them, and the attack and armor elements (Gameplay Systems §2, §3). Sent to its owner alone, right after
///     <c>WorldEntered</c> in every baseline and in the tick of any change. The values travel on purpose, because the
///     player must see them (Content Pipeline §5).
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
        + 17 * sizeof(ushort) + 2 * sizeof(byte) + AilmentCount * sizeof(byte);

    /// <summary>
    ///     The ailments a sheet names a resistance to: poison, stun, freeze, sleep, silence, and blind, in that order.
    /// </summary>
    public const int AilmentCount = 6;

    /// <summary>
    ///     The most a resistance reaches, in percent (Gameplay Systems §2).
    /// </summary>
    public const byte MaxResistance = 100;

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
        ushort attackSpeed,
        ushort equipmentAttack = 0,
        ushort equipmentMagicAttack = 0,
        ushort equipmentDefense = 0,
        ushort equipmentStr = 0,
        ushort equipmentAgi = 0,
        ushort equipmentVit = 0,
        ushort equipmentInt = 0,
        ushort equipmentDex = 0,
        ushort equipmentLuk = 0,
        Element attackElement = Element.Neutral,
        Element armorElement = Element.Neutral,
        byte poisonResistance = 0,
        byte stunResistance = 0,
        byte freezeResistance = 0,
        byte sleepResistance = 0,
        byte silenceResistance = 0,
        byte blindResistance = 0)
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

        foreach (byte resistance in new[]
                 {
                     poisonResistance, stunResistance, freezeResistance, sleepResistance, silenceResistance,
                     blindResistance
                 })
        {
            if (resistance > MaxResistance)
            {
                throw new ArgumentOutOfRangeException(nameof(poisonResistance), "A resistance runs from 0 to 100.");
            }
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
        EquipmentAttack = equipmentAttack;
        EquipmentMagicAttack = equipmentMagicAttack;
        EquipmentDefense = equipmentDefense;
        EquipmentStr = equipmentStr;
        EquipmentAgi = equipmentAgi;
        EquipmentVit = equipmentVit;
        EquipmentInt = equipmentInt;
        EquipmentDex = equipmentDex;
        EquipmentLuk = equipmentLuk;
        AttackElement = attackElement;
        ArmorElement = armorElement;
        PoisonResistance = poisonResistance;
        StunResistance = stunResistance;
        FreezeResistance = freezeResistance;
        SleepResistance = sleepResistance;
        SilenceResistance = silenceResistance;
        BlindResistance = blindResistance;
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

    /// <summary>
    ///     What the worn weapon adds to <see cref="Attack" />: its own attack and its refine bonus (Gameplay Systems
    ///     §11.1).
    /// </summary>
    public ushort EquipmentAttack { get; }

    /// <summary>
    ///     What a worn staff's refine bonus adds to <see cref="MagicAttack" />.
    /// </summary>
    public ushort EquipmentMagicAttack { get; }

    /// <summary>
    ///     What the worn armor adds beside the soft <see cref="Defense" />: its own defense and its refine bonus.
    /// </summary>
    public ushort EquipmentDefense { get; }

    /// <summary>
    ///     What the worn equipment adds to each primary statistic (Gameplay Systems §2), shown beside it as "STR 30 + 2".
    /// </summary>
    public ushort EquipmentStr { get; }

    public ushort EquipmentAgi { get; }

    public ushort EquipmentVit { get; }

    public ushort EquipmentInt { get; }

    public ushort EquipmentDex { get; }

    public ushort EquipmentLuk { get; }

    /// <summary>
    ///     The element of the character's swings and weapon skills: a converter's, else Neutral (Gameplay Systems §9.1).
    /// </summary>
    public Element AttackElement { get; }

    /// <summary>
    ///     The element the worn armor's card gives the character, else Neutral (Gameplay Systems §11.7).
    /// </summary>
    public Element ArmorElement { get; }

    /// <summary>
    ///     The character's resistance to poison against a source of its own level, its cards' included, 0 to 100
    ///     (Gameplay Systems §2); the five that follow are stun's, freeze's, sleep's, silence's, and blind's.
    /// </summary>
    public byte PoisonResistance { get; }

    public byte StunResistance { get; }

    public byte FreezeResistance { get; }

    public byte SleepResistance { get; }

    public byte SilenceResistance { get; }

    public byte BlindResistance { get; }

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
            && AttackSpeed == other.AttackSpeed
            && EquipmentAttack == other.EquipmentAttack
            && EquipmentMagicAttack == other.EquipmentMagicAttack
            && EquipmentDefense == other.EquipmentDefense
            && EquipmentStr == other.EquipmentStr
            && EquipmentAgi == other.EquipmentAgi
            && EquipmentVit == other.EquipmentVit
            && EquipmentInt == other.EquipmentInt
            && EquipmentDex == other.EquipmentDex
            && EquipmentLuk == other.EquipmentLuk
            && AttackElement == other.AttackElement
            && ArmorElement == other.ArmorElement
            && PoisonResistance == other.PoisonResistance
            && StunResistance == other.StunResistance
            && FreezeResistance == other.FreezeResistance
            && SleepResistance == other.SleepResistance
            && SilenceResistance == other.SilenceResistance
            && BlindResistance == other.BlindResistance;
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
            || !reader.TryReadUInt16(out ushort equipmentAttack)
            || !reader.TryReadUInt16(out ushort equipmentMagicAttack)
            || !reader.TryReadUInt16(out ushort equipmentDefense)
            || !reader.TryReadUInt16(out ushort equipmentStr)
            || !reader.TryReadUInt16(out ushort equipmentAgi)
            || !reader.TryReadUInt16(out ushort equipmentVit)
            || !reader.TryReadUInt16(out ushort equipmentInt)
            || !reader.TryReadUInt16(out ushort equipmentDex)
            || !reader.TryReadUInt16(out ushort equipmentLuk)
            || !reader.TryReadByte(out byte attackElement)
            || !reader.TryReadByte(out byte armorElement)
            || !reader.TryReadByte(out byte poisonResistance)
            || !reader.TryReadByte(out byte stunResistance)
            || !reader.TryReadByte(out byte freezeResistance)
            || !reader.TryReadByte(out byte sleepResistance)
            || !reader.TryReadByte(out byte silenceResistance)
            || !reader.TryReadByte(out byte blindResistance)
            || !reader.IsAtEnd
            || !WireEnums.IsDefined((Element)attackElement)
            || !WireEnums.IsDefined((Element)armorElement)
            || poisonResistance > MaxResistance
            || stunResistance > MaxResistance
            || freezeResistance > MaxResistance
            || sleepResistance > MaxResistance
            || silenceResistance > MaxResistance
            || blindResistance > MaxResistance)
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
            attackSpeed,
            equipmentAttack,
            equipmentMagicAttack,
            equipmentDefense,
            equipmentStr,
            equipmentAgi,
            equipmentVit,
            equipmentInt,
            equipmentDex,
            equipmentLuk,
            (Element)attackElement,
            (Element)armorElement,
            poisonResistance,
            stunResistance,
            freezeResistance,
            sleepResistance,
            silenceResistance,
            blindResistance);
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
        writer.WriteUInt16(EquipmentAttack);
        writer.WriteUInt16(EquipmentMagicAttack);
        writer.WriteUInt16(EquipmentDefense);
        writer.WriteUInt16(EquipmentStr);
        writer.WriteUInt16(EquipmentAgi);
        writer.WriteUInt16(EquipmentVit);
        writer.WriteUInt16(EquipmentInt);
        writer.WriteUInt16(EquipmentDex);
        writer.WriteUInt16(EquipmentLuk);
        writer.WriteByte((byte)AttackElement);
        writer.WriteByte((byte)ArmorElement);
        writer.WriteByte(PoisonResistance);
        writer.WriteByte(StunResistance);
        writer.WriteByte(FreezeResistance);
        writer.WriteByte(SleepResistance);
        writer.WriteByte(SilenceResistance);
        writer.WriteByte(BlindResistance);
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
