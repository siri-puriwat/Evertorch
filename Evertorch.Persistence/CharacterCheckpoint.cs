using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Persistence
{
/// <summary>
///     The limited-rollback state of a character in the world (Persistence §6): its map, its position, its HP, where
///     HP 0 records a character checkpointed dead, its SP, its level and experience and its job level and job
///     experience, which a checkpoint never lowers, the progress of its active quests, which a checkpoint never lowers
///     either, and its primary statistics and learned skills, which it writes whole.
/// </summary>
public sealed class CharacterCheckpoint
{
    public CharacterCheckpoint(
        long characterId,
        MapDefinitionId map,
        WorldPosition position,
        int health,
        int spirit,
        int level,
        long experience,
        DateTime at,
        IReadOnlyList<StoredQuest>? quests = null,
        bool isRewardInFlight = false,
        int jobLevel = 1,
        long jobExperience = 0,
        PrimaryStats? stats = null,
        IReadOnlyList<StoredSkill>? skills = null)
    {
        CharacterId = characterId;
        Map = map;
        Position = position;
        Health = health;
        Spirit = spirit;
        Level = level;
        Experience = experience;
        At = at;
        Quests = quests ?? Array.Empty<StoredQuest>();
        IsRewardInFlight = isRewardInFlight;
        JobLevel = jobLevel;
        JobExperience = jobExperience;
        Stats = stats;
        Skills = skills;
    }

    public long CharacterId { get; }

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
    ///     A quest's turn-in was in flight when the checkpoint was taken, so the level and experience it holds may not
    ///     include the reward the turn-in commits; the checkpoint leaves them alone (Persistence §6).
    /// </summary>
    public bool IsRewardInFlight { get; }

    /// <summary>
    ///     The job level, compared and replaced with <see cref="JobExperience" /> as a pair, never lowered.
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
