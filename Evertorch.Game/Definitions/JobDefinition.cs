using System;
using System.Collections.Generic;

namespace Evertorch.Game
{
public sealed class JobDefinition
{
    /// <summary>
    ///     A base job, such as the Adventurer: every value its own, its tree its own skills.
    /// </summary>
    public JobDefinition(
        JobDefinitionId id,
        string displayName,
        PrimaryStats startingStats,
        int healthBase,
        int healthPerLevel,
        int spiritBase,
        int spiritPerLevel,
        int unarmedAttackSpeedPenalty,
        double baseSpeed,
        MapDefinitionId startingMap,
        SkillDefinitionId basicAttack,
        ExperienceDefinitionId experienceTable,
        ExperienceDefinitionId jobExperienceTable,
        IReadOnlyList<SkillDefinitionId> skills,
        IReadOnlyList<WeaponType> weapons)
        : this(
            id,
            displayName,
            startingStats,
            healthBase,
            healthPerLevel,
            spiritBase,
            spiritPerLevel,
            unarmedAttackSpeedPenalty,
            baseSpeed,
            startingMap,
            basicAttack,
            experienceTable,
            jobExperienceTable,
            skills,
            weapons,
            null,
            0,
            skills)
    {
    }

    private JobDefinition(
        JobDefinitionId id,
        string displayName,
        PrimaryStats startingStats,
        int healthBase,
        int healthPerLevel,
        int spiritBase,
        int spiritPerLevel,
        int unarmedAttackSpeedPenalty,
        double baseSpeed,
        MapDefinitionId startingMap,
        SkillDefinitionId basicAttack,
        ExperienceDefinitionId experienceTable,
        ExperienceDefinitionId jobExperienceTable,
        IReadOnlyList<SkillDefinitionId> skills,
        IReadOnlyList<WeaponType> weapons,
        JobDefinitionId? baseJob,
        int carriedSkillPoints,
        IReadOnlyList<SkillDefinitionId> tree)
    {
        Id = id;
        DisplayName = displayName;
        StartingStats = startingStats;
        HealthBase = healthBase;
        HealthPerLevel = healthPerLevel;
        SpiritBase = spiritBase;
        SpiritPerLevel = spiritPerLevel;
        UnarmedAttackSpeedPenalty = unarmedAttackSpeedPenalty;
        BaseSpeed = baseSpeed;
        StartingMap = startingMap;
        BasicAttack = basicAttack;
        ExperienceTable = experienceTable;
        JobExperienceTable = jobExperienceTable;
        Skills = skills;
        Weapons = weapons ?? throw new ArgumentNullException(nameof(weapons));
        BaseJob = baseJob;
        CarriedSkillPoints = carriedSkillPoints;
        Tree = tree;
    }

    public JobDefinitionId Id { get; }

    public string DisplayName { get; }

    public PrimaryStats StartingStats { get; }

    public int HealthBase { get; }

    public int HealthPerLevel { get; }

    public int SpiritBase { get; }

    public int SpiritPerLevel { get; }

    public int UnarmedAttackSpeedPenalty { get; }

    /// <summary>World units per second before any movement rule is applied.</summary>
    public double BaseSpeed { get; }

    public MapDefinitionId StartingMap { get; }

    public SkillDefinitionId BasicAttack { get; }

    public ExperienceDefinitionId ExperienceTable { get; }

    /// <summary>
    ///     The table of the job's levels, which job experience fills (Gameplay Systems §2.1).
    /// </summary>
    public ExperienceDefinitionId JobExperienceTable { get; }

    /// <summary>
    ///     The job's own skills, in order; a first job's <see cref="Tree" /> puts its base job's before them.
    /// </summary>
    public IReadOnlyList<SkillDefinitionId> Skills { get; }

    /// <summary>
    ///     The weapon types the job wields (Gameplay Systems §11.1), at least one.
    /// </summary>
    public IReadOnlyList<WeaponType> Weapons { get; }

    /// <summary>
    ///     The job a first job changes from, whose starting statistics, map, base table, basic attack, and movement it
    ///     shares (Content Pipeline §4); null for a base job.
    /// </summary>
    public JobDefinitionId? BaseJob { get; }

    /// <summary>
    ///     The skill points a first job is granted for its base job's levels: the base job's job cap less one; 0 for a
    ///     base job (Gameplay Systems §2.1).
    /// </summary>
    public int CarriedSkillPoints { get; }

    /// <summary>
    ///     The whole skill tree its characters learn from, in order (Gameplay Systems §9): a base job's own skills, or
    ///     a first job's base job's skills followed by its own.
    /// </summary>
    public IReadOnlyList<SkillDefinitionId> Tree { get; }

    /// <summary>
    ///     A first job (Content Pipeline §4): its own values, and the rest from <paramref name="baseJob" />, whose job
    ///     levels <paramref name="baseJobExperience" /> caps. The loaders refuse a base job that has one itself and an
    ///     own skill its base tree already holds before they call this.
    /// </summary>
    public static JobDefinition FirstJob(
        JobDefinitionId id,
        string displayName,
        JobDefinition baseJob,
        ExperienceTableDefinition baseJobExperience,
        int healthBase,
        int healthPerLevel,
        int spiritBase,
        int spiritPerLevel,
        ExperienceDefinitionId jobExperienceTable,
        IReadOnlyList<SkillDefinitionId> skills,
        IReadOnlyList<WeaponType> weapons)
    {
        if (baseJob.BaseJob != null)
        {
            throw new ArgumentException($"Job {baseJob.Id} has a base job of its own.", nameof(baseJob));
        }

        if (baseJobExperience.Id != baseJob.JobExperienceTable)
        {
            throw new ArgumentException(
                $"Table {baseJobExperience.Id} is not the job table of {baseJob.Id}.",
                nameof(baseJobExperience));
        }

        var tree = new List<SkillDefinitionId>(baseJob.Tree);
        tree.AddRange(skills);

        // A table of n entries caps its job at level n + 1, so the carried points are n.
        return new JobDefinition(
            id,
            displayName,
            baseJob.StartingStats,
            healthBase,
            healthPerLevel,
            spiritBase,
            spiritPerLevel,
            baseJob.UnarmedAttackSpeedPenalty,
            baseJob.BaseSpeed,
            baseJob.StartingMap,
            baseJob.BasicAttack,
            baseJob.ExperienceTable,
            jobExperienceTable,
            skills,
            weapons,
            baseJob.Id,
            baseJobExperience.Levels.Count,
            tree.AsReadOnly());
    }

    public bool CanWield(WeaponType weaponType)
    {
        foreach (WeaponType wielded in Weapons)
        {
            if (wielded == weaponType)
            {
                return true;
            }
        }

        return false;
    }
}
}
