using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Persistence
{
/// <summary>
///     The limited-rollback state of a character in the world (Persistence §6): its map, its position, its HP, where
///     HP 0 records a character checkpointed dead, its SP, its level and experience, which a checkpoint never lowers,
///     and the progress of its active quests, which a checkpoint never lowers either.
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
        bool isRewardInFlight = false)
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
}
}
