using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Persistence
{
/// <summary>
///     A character as stored, with its inventory and its quests: what entering the world loads (Persistence §7).
///     Definition IDs are the stored text, which the current content may no longer define (Persistence §8).
/// </summary>
public sealed class StoredCharacter
{
    public StoredCharacter(
        long id,
        AccountId account,
        string name,
        string jobDefinitionId,
        int baseLevel,
        long experience,
        PrimaryStats stats,
        int health,
        int spirit,
        string mapDefinitionId,
        WorldPosition position,
        uint inventoryRevision,
        long coins,
        IReadOnlyList<StoredItem> items,
        IReadOnlyList<StoredQuest>? quests = null)
    {
        Id = id;
        Account = account;
        Name = name;
        JobDefinitionId = jobDefinitionId;
        BaseLevel = baseLevel;
        Experience = experience;
        Stats = stats;
        Health = health;
        Spirit = spirit;
        MapDefinitionId = mapDefinitionId;
        Position = position;
        InventoryRevision = inventoryRevision;
        Coins = coins;
        Items = items;
        Quests = quests ?? Array.Empty<StoredQuest>();
    }

    public long Id { get; }

    public AccountId Account { get; }

    public string Name { get; }

    public string JobDefinitionId { get; }

    public int BaseLevel { get; }

    /// <summary>
    ///     Base experience toward the next level.
    /// </summary>
    public long Experience { get; }

    public PrimaryStats Stats { get; }

    /// <summary>
    ///     HP at the last checkpoint; 0 means the character was checkpointed dead.
    /// </summary>
    public int Health { get; }

    public int Spirit { get; }

    public string MapDefinitionId { get; }

    public WorldPosition Position { get; }

    public uint InventoryRevision { get; }

    /// <summary>
    ///     The character's <c>currency</c>, from 0 to the cap (Gameplay Systems §11.3).
    /// </summary>
    public long Coins { get; }

    public IReadOnlyList<StoredItem> Items { get; }

    /// <summary>
    ///     The quests the character has accepted or completed, ordered by quest ID.
    /// </summary>
    public IReadOnlyList<StoredQuest> Quests { get; }
}
}
