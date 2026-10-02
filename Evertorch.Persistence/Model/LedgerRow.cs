using System;

namespace Evertorch.Persistence
{
/// <summary>
///     One immutable economy ledger entry. A database trigger rejects every update and delete.
/// </summary>
internal sealed class LedgerRow
{
    public const string PickupOperation = "pickup";
    public const string EquipOperation = "equip";
    public const string UnequipOperation = "unequip";
    public const string ConsumeOperation = "consume";
    public const string BuyOperation = "buy";
    public const string SellOperation = "sell";
    public const string QuestRewardOperation = "quest_reward";
    public const string BossRewardOperation = "boss_reward";

    public long Id { get; set; }

    public Guid OperationId { get; set; }

    public long? ActorCharacterId { get; set; }

    public string OperationType { get; set; } = string.Empty;

    public long? ItemInstanceId { get; set; }

    public string? ItemDefinitionId { get; set; }

    public int QuantityDelta { get; set; }

    public long CurrencyDelta { get; set; }

    public string? MetadataJson { get; set; }

    public DateTime CreatedAt { get; set; }
}
}
