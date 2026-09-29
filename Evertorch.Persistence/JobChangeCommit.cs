using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One job change to commit (Persistence §5): the job the character has, the first job it becomes, and, when the
///     new job cannot wield the worn weapon, the slot to empty, under a new operation ID.
/// </summary>
public sealed class JobChangeCommit
{
    public JobChangeCommit(
        Guid operationId,
        long characterId,
        string fromJob,
        string toJob,
        string? unequipSlot,
        DateTime at)
    {
        OperationId = operationId;
        CharacterId = characterId;
        FromJob = fromJob ?? throw new ArgumentNullException(nameof(fromJob));
        ToJob = toJob ?? throw new ArgumentNullException(nameof(toJob));
        UnequipSlot = unequipSlot;
        At = at;
    }

    public Guid OperationId { get; }

    public long CharacterId { get; }

    /// <summary>
    ///     The job the character must have; any other refuses the change.
    /// </summary>
    public string FromJob { get; }

    public string ToJob { get; }

    /// <summary>
    ///     <c>Weapon</c> when the item worn there comes off with the change; null when nothing does.
    /// </summary>
    public string? UnequipSlot { get; }

    public DateTime At { get; }
}
}
