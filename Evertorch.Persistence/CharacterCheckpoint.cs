using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Persistence
{
/// <summary>
///     The limited-rollback state of a character in the world (Persistence §6): its job, its map, its position, its HP,
///     where HP 0 records a character checkpointed dead, its SP, its level and experience and its job level and job
///     experience, which a checkpoint never lowers, the job pair only while the stored job is the checkpoint's, the
///     progress of its active quests, which a checkpoint never lowers either, and its primary statistics and learned
///     skills, which it writes whole. It never writes the job itself: only a change's commit does.
/// </summary>
public sealed class CharacterCheckpoint
{
    public CharacterCheckpoint(
        long characterId,
        string job,
        MapDefinitionId map,
        WorldPosition position,
        int health,
        int spirit,
        int level,
        long experience,
        DateTime at,
        IReadOnlyList<StoredQuest>? quests = null,
        bool isCommitInFlight = false,
        int jobLevel = 1,
        long jobExperience = 0,
        PrimaryStats? stats = null,
        IReadOnlyList<StoredSkill>? skills = null)
    {
        CharacterId = characterId;
        Job = job ?? throw new ArgumentNullException(nameof(job));
        Map = map;
        Position = position;
        Health = health;
        Spirit = spirit;
        Level = level;
        Experience = experience;
        At = at;
        Quests = quests ?? Array.Empty<StoredQuest>();
        IsCommitInFlight = isCommitInFlight;
        JobLevel = jobLevel;
        JobExperience = jobExperience;
        Stats = stats;
        Skills = skills;
    }

    public long CharacterId { get; }

    /// <summary>
    ///     The job the character had when the checkpoint was taken; the job pair is written only while the stored job
    ///     is this one, so a checkpoint taken before a change never undoes it (Persistence §6).
    /// </summary>
    public string Job { get; }

    public MapDefinitionId Map { get; }

    public WorldPosition Position { get; }

    public int Health { get; }

    public int Spirit { get; }

    public int Level { get; }

    /// <summary>
    ///     Experience toward the next level.
    /// </summary>
    public long Experience { get; }

    public DateTime At { get; }

    /// <summary>
    ///     The character's active quests with their progress; a completed quest is the turn-in's to write.
    /// </summary>
    public IReadOnlyList<StoredQuest> Quests { get; }

    /// <summary>
    ///     A quest's turn-in or a job change was in flight when the checkpoint was taken, so the levels and experience
    ///     it holds may not include what that commit writes; the checkpoint leaves both pairs, the statistics, and the
    ///     skills alone (Persistence §6).
    /// </summary>
    public bool IsCommitInFlight { get; }

    /// <summary>
    ///     The job level, compared and replaced with <see cref="JobExperience" /> as a pair, never lowered, and only
    ///     while the stored job is <see cref="Job" />.
    /// </summary>
    public int JobLevel { get; }

    /// <summary>
    ///     Job experience toward the next job level.
    /// </summary>
    public long JobExperience { get; }

    /// <summary>
    ///     The primary statistics, written whole; null leaves the stored ones as they are.
    /// </summary>
    public PrimaryStats? Stats { get; }

    /// <summary>
    ///     Every learned skill at its level, written whole, so a skill left out is forgotten; null leaves the stored
    ///     ones as they are.
    /// </summary>
    public IReadOnlyList<StoredSkill>? Skills { get; }
}
}
