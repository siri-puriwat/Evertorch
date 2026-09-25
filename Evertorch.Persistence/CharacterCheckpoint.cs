using System;
using Evertorch.Game;

namespace Evertorch.Persistence
{
/// <summary>
///     The limited-rollback state of a character in the world (Persistence §6): its map, its position, its HP, where
///     HP 0 records a character checkpointed dead, its SP, and its level and experience, which a checkpoint never
///     lowers.
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
        DateTime at)
    {
        CharacterId = characterId;
        Map = map;
        Position = position;
        Health = health;
        Spirit = spirit;
        Level = level;
        Experience = experience;
        At = at;
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
}
}
