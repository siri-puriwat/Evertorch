using System;
using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     A boss's prize on its way to its most valuable player's bag (Gameplay Systems §11): the boss, the item and how
///     many, and the MVP experience the award gained, which the prize's message reports (Network Protocol §9).
/// </summary>
public sealed class BossGrant
{
    public BossGrant(MonsterDefinitionId boss, ItemDefinitionId item, int amount, long gainedExperience)
    {
        Boss = boss;
        Item = item;
        Amount = amount;
        GainedExperience = gainedExperience;
    }

    public MonsterDefinitionId Boss { get; }

    public ItemDefinitionId Item { get; }

    public int Amount { get; }

    public long GainedExperience { get; }

    /// <summary>
    ///     The grant's one operation ID, kept for its whole life: a commit made again after an outage, or a drop at the
    ///     player's feet, carries it, so the ledger's unique index lets the prize into a bag once.
    /// </summary>
    public Guid OperationId { get; } = Guid.NewGuid();
}
}
