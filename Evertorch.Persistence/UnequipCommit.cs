using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One unequip to commit (Persistence §5): the slot to empty, under a new operation ID.
/// </summary>
public sealed class UnequipCommit
{
    public UnequipCommit(Guid operationId, long characterId, string slot, DateTime at)
    {
        OperationId = operationId;
        CharacterId = characterId;
        Slot = slot ?? throw new ArgumentNullException(nameof(slot));
        At = at;
    }

    public Guid OperationId { get; }

    public long CharacterId { get; }

    /// <summary><c>Weapon</c> or <c>Armor</c>.</summary>
    public string Slot { get; }

    public DateTime At { get; }
}
}
