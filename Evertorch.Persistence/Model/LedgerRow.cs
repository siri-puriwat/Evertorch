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
    public const string TradeOperation = "trade";
    public const string StorageDepositOperation = "storage_deposit";
    public const string StorageWithdrawOperation = "storage_withdraw";
    public const string MarketListOperation = "market_list";
    public const string MarketBuyOperation = "market_buy";
    public const string MarketCollectOperation = "market_collect";
    public const string RefineOperation = "refine";
    public const string CardInsertOperation = "card_insert";
    public const string CardRemoveOperation = "card_remove";

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
