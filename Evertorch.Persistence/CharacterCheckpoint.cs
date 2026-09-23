using System;
using Evertorch.Game;

namespace Evertorch.Persistence
{
/// <summary>
///     The limited-rollback state of a character in the world (Persistence §6): its map, its position, and its HP,
///     where HP 0 records a character checkpointed dead.
/// </summary>
public sealed class CharacterCheckpoint
{
    public CharacterCheckpoint(long characterId, MapDefinitionId map, WorldPosition position, int health, DateTime at)
    {
        CharacterId = characterId;
        Map = map;
        Position = position;
        Health = health;
        At = at;
    }

    public long CharacterId { get; }

    public MapDefinitionId Map { get; }

    public WorldPosition Position { get; }

    public int Health { get; }

    public DateTime At { get; }
}
}
