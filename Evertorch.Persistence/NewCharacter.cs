using System;
using Evertorch.Game;

namespace Evertorch.Persistence
{
/// <summary>
///     A character about to be created, with every starting value the server chose (Persistence §4). It enters at
///     base and job level 1 with no experience or currency.
/// </summary>
public sealed class NewCharacter
{
    public NewCharacter(
        string name,
        JobDefinitionId job,
        PrimaryStats stats,
        int health,
        int spirit,
        MapDefinitionId map,
        WorldPosition position,
        DateTime createdAt)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Job = job;
        Stats = stats;
        Health = health;
        Spirit = spirit;
        Map = map;
        Position = position;
        CreatedAt = createdAt;
    }

    public string Name { get; }

    public JobDefinitionId Job { get; }

    public PrimaryStats Stats { get; }

    public int Health { get; }

    public int Spirit { get; }

    public MapDefinitionId Map { get; }

    public WorldPosition Position { get; }

    public DateTime CreatedAt { get; }
}
}
