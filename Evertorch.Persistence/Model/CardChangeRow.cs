using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One change of a row's cards (Persistence §4): an insertion, a removal at the Smith, or a card a refine that
///     destroyed its item returned to the bag. The rows are kept as numbers, since a carded row may later be sold,
///     stored, or destroyed.
/// </summary>
internal sealed class CardChangeRow
{
    public const string InsertChange = "insert";
    public const string RemoveChange = "remove";
    public const string ReturnChange = "return";

    public long Id { get; set; }

    public Guid OperationId { get; set; }

    public long CharacterId { get; set; }

    public string Change { get; set; } = string.Empty;

    public string CardDefinitionId { get; set; } = string.Empty;

    public long ItemInstanceId { get; set; }

    public string ItemDefinitionId { get; set; } = string.Empty;

    public int Slot { get; set; }

    public long CardInstanceId { get; set; }

    public long Fee { get; set; }

    public DateTime CreatedAt { get; set; }
}
}
