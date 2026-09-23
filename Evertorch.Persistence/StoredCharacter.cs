using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Persistence
{
/// <summary>
///     A character as stored, with its inventory: what entering the world loads (Persistence §7). Definition IDs are
///     the stored text, which the current content may no longer define (Persistence §8).
/// </summary>
public sealed class StoredCharacter
{
    public StoredCharacter(
        long id,
        AccountId account,
        string name,
        string jobDefinitionId,
        int baseLevel,
        PrimaryStats stats,
        int health,
        string mapDefinitionId,
        WorldPosition position,
        uint inventoryRevision,
        IReadOnlyList<StoredItem> items)
    {
        Id = id;
        Account = account;
        Name = name;
        JobDefinitionId = jobDefinitionId;
        BaseLevel = baseLevel;
        Stats = stats;
        Health = health;
        MapDefinitionId = mapDefinitionId;
        Position = position;
        InventoryRevision = inventoryRevision;
        Items = items;
    }

    public long Id { get; }

    public AccountId Account { get; }

    public string Name { get; }

    public string JobDefinitionId { get; }

    public int BaseLevel { get; }

    public PrimaryStats Stats { get; }

    /// <summary>
    ///     HP at the last checkpoint; 0 means the character was checkpointed dead.
    /// </summary>
    public int Health { get; }

    public string MapDefinitionId { get; }

    public WorldPosition Position { get; }

    public uint InventoryRevision { get; }

    public IReadOnlyList<StoredItem> Items { get; }
}
}
