using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One pickup to commit (Persistence §5): the drop's <see cref="DropId" /> is the operation ID, so it can be
///     committed at most once, by anyone.
/// </summary>
public sealed class PickupCommit
{
    public PickupCommit(
        Guid dropId,
        long characterId,
        string itemDefinitionId,
        int amount,
        int stackLimit,
        int maxRows,
        DateTime at)
    {
        if (amount < 1 || stackLimit < 1 || maxRows < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount, stack limit, and rows are at least 1.");
        }

        DropId = dropId;
        CharacterId = characterId;
        ItemDefinitionId = itemDefinitionId ?? throw new ArgumentNullException(nameof(itemDefinitionId));
        Amount = amount;
        StackLimit = stackLimit;
        MaxRows = maxRows;
        At = at;
    }

    public Guid DropId { get; }

    public long CharacterId { get; }

    public string ItemDefinitionId { get; }

    public int Amount { get; }

    /// <summary>
    ///     The most one row of the item may hold; a pickup that would exceed it is refused whole.
    /// </summary>
    public int StackLimit { get; }

    public int MaxRows { get; }

    public DateTime At { get; }
}
}
