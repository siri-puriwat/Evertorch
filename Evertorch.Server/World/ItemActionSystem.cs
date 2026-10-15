using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using Evertorch.Rules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Equips, unequips, uses, buys, and sells items, turns in quests, changes jobs, reads, fills, and empties the
///     account's storage, reads, lists on, buys from, and collects from the market board, and refines (Gameplay Systems
///     §2.2, §6.1, §11.1–§11.6; Persistence §5). The checks happen on the tick thread; the inventory and the coins
///     change only through the commit, and the server's copy of them, the statistics, HP and SP, the quests, and the
///     owner learn of it once its result is back. A commit whose answer was lost is settled from the ledger before the
///     character may start another inventory operation.
/// </summary>
public sealed class ItemActionSystem : ITickPhase
{
    private const string EquipOperation = "equip";
    private const string UnequipOperation = "unequip";
    private const string EquipLookup = "equip lookup";
    private const string UnequipLookup = "unequip lookup";
    private const string ConsumeOperation = "consume";
    private const string ConsumeLookup = "consume lookup";
    private const string BuyOperation = "buy";
    private const string BuyLookup = "buy lookup";
    private const string SellOperation = "sell";
    private const string SellLookup = "sell lookup";
    private const string QuestRewardOperation = "quest reward";
    private const string QuestRewardLookup = "quest reward lookup";
    private const string JobChangeOperation = "job change";
    private const string JobChangeLookup = "job change lookup";
    private const string StorageDepositOperation = "storage deposit";
    private const string StorageDepositLookup = "storage deposit lookup";
    private const string StorageWithdrawOperation = "storage withdraw";
    private const string StorageWithdrawLookup = "storage withdraw lookup";
    private const string StorageReadOperation = "storage read";
    private const string StorageFee = "storage fee";
    private const string MarketListOperation = "market list";
    private const string MarketListLookup = "market list lookup";
    private const string MarketBuyOperation = "market buy";
    private const string MarketBuyLookup = "market buy lookup";
    private const string MarketCollectOperation = "market collect";
    private const string MarketCollectLookup = "market collect lookup";
    private const string MarketReadOperation = "market read";
    private const string MarketFee = "market fee";
    private const string MarketTax = "market tax";
    private const string RefineOperation = "refine";
    private const string RefineLookup = "refine lookup";
    private const string CardInsertOperation = "card insert";
    private const string CardInsertLookup = "card insert lookup";
    private const string CardRemoveOperation = "card remove";
    private const string CardRemoveLookup = "card remove lookup";
    private const string CardRemovalCoins = "card removal";
    private const string Deposited = "deposit";
    private const string Withdrawn = "withdraw";
    private const int MillisecondsPerSecond = 1000;

    // A lookup that failed for a reason other than an outage is asked again only after this long, not on every tick
    // with an error each time (Persistence §9).
    private const int FailedLookupDelayMs = 1000;

    /// <summary>
    ///     The most rows an account's storage holds (Gameplay Systems §11.4).
    /// </summary>
    public const int MaxStorageRows = 300;

    private static readonly Action<ILogger, InventoryOperationKind, Guid, long, long, Exception?> LogUnsettled =
        LoggerMessage.Define<InventoryOperationKind, Guid, long, long>(
            LogLevel.Warning,
            new EventId(4010, "InventoryOperationUnsettled"),
            "The {Kind} commit {OperationId} of character {Character} on connection {Connection} gave no answer; the "
            + "character starts no other inventory operation until the ledger says what happened.");

    private static readonly Action<ILogger, long, long, int, string, long, Guid, Exception?> LogBought =
        LoggerMessage.Define<long, long, int, string, long, Guid>(
            LogLevel.Information,
            new EventId(1007, "ItemBought"),
            "Character {Character} on connection {Connection} bought {Quantity} {Item} for {Coins} coins (operation "
            + "{OperationId}).");

    private static readonly Action<ILogger, long, long, int, string, long, Guid, Exception?> LogDeposited =
        LoggerMessage.Define<long, long, int, string, long, Guid>(
            LogLevel.Information,
            new EventId(1029, "StorageDeposited"),
            "Character {Character} on connection {Connection} deposited {Quantity} {Item} for {Coins} coins (operation "
            + "{OperationId}).");

    private static readonly Action<ILogger, long, long, int, string, Guid, Exception?> LogWithdrawn =
        LoggerMessage.Define<long, long, int, string, Guid>(
            LogLevel.Information,
            new EventId(1030, "StorageWithdrawn"),
            "Character {Character} on connection {Connection} withdrew {Quantity} {Item} (operation {OperationId}).");

    private static readonly Action<ILogger, long, long, long, string, Exception?> LogStorageContentMismatch =
        LoggerMessage.Define<long, long, long, string>(
            LogLevel.Warning,
            new EventId(2010, "StorageContentMismatch"),
            "Connection {Connection} (account {Account}) could not read the storage of character {Character}: the "
            + "loaded content has no {Definition}. Its stored data is kept.");

    private static readonly Action<ILogger, long, int, string, long, long, Guid, Exception?> LogListed =
        LoggerMessage.Define<long, int, string, long, long, Guid>(
            LogLevel.Information,
            new EventId(1031, "MarketListed"),
            "Character {Character} listed {Quantity} {Item} as listing {Listing} for a fee of {Coins} coins (operation "
            + "{OperationId}).");

    private static readonly Action<ILogger, long, int, string, long, string, Guid, Exception?> LogMarketSold =
        LoggerMessage.Define<long, int, string, long, string, Guid>(
            LogLevel.Information,
            new EventId(1032, "MarketSold"),
            "Character {Character} bought {Quantity} {Item} on the board for {Coins} coins from listings {Listings} "
            + "(operation {OperationId}).");

    private static readonly Action<ILogger, long, long, int, string, long, Guid, Exception?> LogCollected =
        LoggerMessage.Define<long, long, int, string, long, Guid>(
            LogLevel.Information,
            new EventId(1033, "MarketCollected"),
            "Character {Character} collected listing {Listing}: {Quantity} {Item} back and {Coins} coins after the tax "
            + "(operation {OperationId}).");

    private static readonly Action<ILogger, long, long, long, string, Exception?> LogMarketContentMismatch =
        LoggerMessage.Define<long, long, long, string>(
            LogLevel.Warning,
            new EventId(2011, "MarketContentMismatch"),
            "Character {Character} on connection {Connection} could not collect listing {Listing}: the loaded content "
            + "has no {Definition}. The listing is kept.");

    private static readonly Action<ILogger, long, string, long, string, int, Guid, Exception?> LogCardInserted =
        LoggerMessage.Define<long, string, long, string, int, Guid>(
            LogLevel.Information,
            new EventId(1037, "CardInserted"),
            "Character {Character} inserted {Card} into row {Row} ({Item}) at slot {Slot} (operation {OperationId}).");

    // A log message takes six values, so the removed card's item is the operation's to name, by its card_changes row.
    private static readonly Action<ILogger, long, string, long, int, long, Guid, Exception?> LogCardRemoved =
        LoggerMessage.Define<long, string, long, int, long, Guid>(
            LogLevel.Information,
            new EventId(1038, "CardRemoved"),
            "Character {Character} took {Card} out of row {Row} at slot {Slot} for {Fee} coins (operation "
            + "{OperationId}).");

    private static readonly Action<ILogger, long, string, string, Guid, Exception?> LogCardsReturned =
        LoggerMessage.Define<long, string, string, Guid>(
            LogLevel.Information,
            new EventId(1041, "CardsReturned"),
            "Character {Character} got the cards of the destroyed {Item} back: {Cards} (operation {OperationId}).");

    private static readonly Action<ILogger, long, string, int, int, string, Guid, Exception?> LogRefined =
        LoggerMessage.Define<long, string, int, int, string, Guid>(
            LogLevel.Information,
            new EventId(1034, "ItemRefined"),
            "Character {Character} refined {Item} from +{LevelBefore} to +{LevelAfter}: {Outcome} (operation "
            + "{OperationId}).");

    private static readonly Action<ILogger, long, long, int, string, long, Guid, Exception?> LogSold =
        LoggerMessage.Define<long, long, int, string, long, Guid>(
            LogLevel.Information,
            new EventId(1008, "ItemSold"),
            "Character {Character} on connection {Connection} sold {Quantity} {Item} for {Coins} coins (operation "
            + "{OperationId}).");

    private readonly PersistenceWorker m_persistence;
    private readonly MessageSender m_sender;
    private readonly SessionRegistry m_sessions;
    private readonly CharacterLifetime m_lifetime;
    private readonly CharacterStats m_stats;
    private readonly ServerContent m_content;
    private readonly TimeProvider m_time;
    private readonly AuditLog m_audit;
    private readonly ILogger<ItemActionSystem> m_logger;
    private readonly ServerInstruments m_instruments;
    private readonly CharacterProgression m_progression;
    private readonly CharacterBuilds m_builds;
    private readonly CombatSystem m_combat;
    private readonly StatusEffectSystem m_statusEffects;
    private readonly IRandomSource m_random;
    private readonly float m_npcReach;
    private readonly uint m_failedLookupDelayTicks;
    private readonly int m_tickRate;
    private readonly List<(CharacterSession Character, uint NotBefore)> m_unsettled = new();
    private readonly IReadOnlyDictionary<string, int> m_stackLimits;
    private readonly IReadOnlyDictionary<string, ItemMarket> m_marketTerms;
    private readonly long m_pricePeriodMs;
    private readonly long m_listingDurationMs;
    private uint m_tick;

    public ItemActionSystem(
        PersistenceWorker persistence,
        MessageSender sender,
        SessionRegistry sessions,
        CharacterLifetime lifetime,
        CharacterStats stats,
        ServerContent content,
        TimeProvider time,
        IOptions<SimulationOptions> simulation,
        IOptions<WorldOptions> world,
        IOptions<MarketOptions> market,
        ServerInstruments instruments,
        CharacterProgression progression,
        CharacterBuilds builds,
        CombatSystem combat,
        StatusEffectSystem statusEffects,
        IRandomSource random,
        AuditLog audit,
        ILogger<ItemActionSystem> logger)
    {
        m_persistence = persistence;
        m_sender = sender;
        m_sessions = sessions;
        m_lifetime = lifetime;
        m_stats = stats;
        m_content = content;
        m_time = time;
        m_audit = audit;
        m_logger = logger;
        m_instruments = instruments;
        m_progression = progression;
        m_builds = builds;
        m_combat = combat;
        m_statusEffects = statusEffects;
        m_random = random;
        m_npcReach = NpcInteraction.Range + world.Value.AttackRangeTolerance;
        m_stackLimits =
            content.Items.Values.ToDictionary(item => item.Id.Value, item => item.StackLimit, StringComparer.Ordinal);
        m_marketTerms = content.Items.Values.Where(item => item.Market != null)
            .ToDictionary(item => item.Id.Value, item => item.Market!, StringComparer.Ordinal);
        m_pricePeriodMs = market.Value.PricePeriodMs;
        m_listingDurationMs = market.Value.ListingDurationMs;
        m_failedLookupDelayTicks =
            (uint)((long)FailedLookupDelayMs * simulation.Value.TickRate / MillisecondsPerSecond);
        m_tickRate = simulation.Value.TickRate;
    }

    public TickPhase Phase => TickPhase.SchedulePersistence;

    /// <summary>
    ///     Asks the ledger again about every commit whose answer was lost, once the database takes work again and any
    ///     wait after a failed lookup is over.
    /// </summary>
    public void Execute(in TickContext context)
    {
        m_tick = context.Tick;
        for (int index = m_unsettled.Count - 1; index >= 0; index--)
        {
            (CharacterSession character, uint notBefore) = m_unsettled[index];
            if (unchecked((int)(m_tick - notBefore)) >= 0 && TryQueueLookup(character))
            {
                m_unsettled.RemoveAt(index);
            }
        }
    }

    /// <summary>
    ///     An item action finished, one way or the other, and its character may now log out, leave, or cross.
    /// </summary>
    public event Action<CharacterSession>? Settled;

    /// <summary>
    ///     Checks an equip (Gameplay Systems §11.1) and, when it passes, queues its commit: the row goes into
    ///     <paramref name="destination" />, and a row the position held comes out of it. A destination the row's category
    ///     cannot go to, or a row worn anywhere, is refused with <see cref="CommandRejectionReason.NotAllowedNow" />; a
    ///     weapon of a type the job cannot wield with <see cref="CommandRejectionReason.RequirementNotMet" />. The caller
    ///     has already refused a dead or leaving character.
    /// </summary>
    public CommandRejectionReason TryEquip(
        ClientSession session,
        long inventoryItem,
        EquipmentSlot destination,
        uint commandSequence)
    {
        CharacterSession character = session.Character!;
        // First: only a settled inventory answers for the database, and one operation at a time keeps the changes in
        // commit order (Persistence §7).
        if (character.HasInventoryWork)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        if (!character.Inventory.TryGetRow(inventoryItem, out InventoryEntry row))
        {
            return CommandRejectionReason.InvalidTarget;
        }

        // A row worn in the other accessory position is refused here, not moved, so it never reaches the database's
        // one-position-per-row index (Persistence §5).
        ItemDefinition item = m_content.Items[row.Item];
        if (!EquipmentPositions.Fits(item.Type, destination) || row.Slot != EquipmentSlot.None)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        WeaponType? weaponType = item.Equipment?.WeaponType;
        if (weaponType.HasValue && !m_content.Jobs[character.Player.Job].CanWield(weaponType.Value))
        {
            return CommandRejectionReason.RequirementNotMet;
        }

        long worn = character.Inventory.WornIn(destination);
        ReportedRow? displaced = character.Inventory.TryGetRow(worn, out InventoryEntry wornRow)
            ? new ReportedRow(worn, wornRow.Item.Value)
            : null;
        var operation = new InventoryOperation(
            InventoryOperationKind.Equip,
            commandSequence,
            Guid.NewGuid(),
            reported: displaced);
        var commit = new EquipCommit(
            operation.OperationId,
            character.Character.Value,
            inventoryItem,
            CharacterInventory.StoredNameOf(destination),
            ReportRows(operation),
            m_time.GetUtcNow().UtcDateTime);
        return TryCommit(session, operation, (store, cancellation) => store.CommitEquipAsync(commit, cancellation));
    }

    /// <summary>
    ///     Checks an unequip and, when it passes, queues its commit; the item stays in the inventory. The caller has
    ///     already refused a dead or leaving character.
    /// </summary>
    public CommandRejectionReason TryUnequip(ClientSession session, EquipmentSlot slot, uint commandSequence)
    {
        CharacterSession character = session.Character!;
        if (character.HasInventoryWork)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        if (character.Inventory.WornIn(slot) == 0)
        {
            return CommandRejectionReason.InvalidTarget;
        }

        var operation = new InventoryOperation(InventoryOperationKind.Unequip, commandSequence, Guid.NewGuid());
        var commit = new UnequipCommit(
            operation.OperationId,
            character.Character.Value,
            CharacterInventory.StoredNameOf(slot),
            m_time.GetUtcNow().UtcDateTime);
        return TryCommit(session, operation, (store, cancellation) => store.CommitUnequipAsync(commit, cancellation));
    }

    /// <summary>
    ///     Checks a use (Gameplay Systems §11.2) and, when it passes, queues the commit of one unit of the row. The
    ///     caller has already refused a dead or leaving character.
    /// </summary>
    public CommandRejectionReason TryUse(ClientSession session, long inventoryItem, uint commandSequence)
    {
        CharacterSession character = session.Character!;
        if (character.HasInventoryWork)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        if (!character.Inventory.TryGetRow(inventoryItem, out InventoryEntry row))
        {
            return CommandRejectionReason.InvalidTarget;
        }

        ItemEffect? effect = m_content.Items[row.Item].Effect;
        if (effect == null)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        // A converter gives its element to the worn weapon's swings, so it needs one (Gameplay Systems §11.2).
        if (effect.Status != null && character.Player.Weapon == null)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        var operation = new InventoryOperation(
            InventoryOperationKind.Consume,
            commandSequence,
            Guid.NewGuid(),
            item: row.Item,
            deaths: character.Player.Deaths);
        var commit = new ConsumeCommit(
            operation.OperationId,
            character.Character.Value,
            inventoryItem,
            m_time.GetUtcNow().UtcDateTime);
        return TryCommit(session, operation, (store, cancellation) => store.CommitConsumeAsync(commit, cancellation));
    }

    /// <summary>
    ///     Checks a purchase (Gameplay Systems §6.1, then §11.3, in order) and, when it passes, queues its commit. The
    ///     caller has already refused a dead or leaving character.
    /// </summary>
    public CommandRejectionReason TryBuy(
        ClientSession session,
        EntityId npc,
        ItemDefinitionId item,
        uint quantity,
        uint commandSequence)
    {
        CharacterSession character = session.Character!;
        if (character.HasInventoryWork)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        CommandRejectionReason reach = NpcReach.Check(session, npc, m_npcReach, out NpcEntity? shopkeeper);
        if (reach != CommandRejectionReason.None)
        {
            return reach;
        }

        ShopEntry? stock = shopkeeper!.Definition.Shop.FirstOrDefault(entry => entry.Item == item);
        if (stock == null)
        {
            return CommandRejectionReason.InvalidTarget;
        }

        // Up to the stack limit, so an item with a stack limit of 1 is bought one at a time and a purchase adds at
        // most one row, the one the ledger names when an answer is lost.
        ItemDefinition definition = m_content.Items[item];
        if (quantity > definition.StackLimit)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        long total = stock.Price * quantity;
        if (character.Inventory.Coins < total)
        {
            return CommandRejectionReason.NotEnoughCoins;
        }

        if (!HasRoomFor(character.Inventory, definition, quantity))
        {
            return CommandRejectionReason.InventoryFull;
        }

        var operation = new InventoryOperation(
            InventoryOperationKind.Buy,
            commandSequence,
            Guid.NewGuid(),
            item: item,
            quantity: (int)quantity,
            coins: total);
        var commit = new BuyCommit(
            operation.OperationId,
            character.Character.Value,
            item.Value,
            (int)quantity,
            stock.Price,
            definition.StackLimit,
            PickupSystem.MaxInventoryRows,
            m_time.GetUtcNow().UtcDateTime);
        return TryCommit(session, operation, (store, cancellation) => store.CommitBuyAsync(commit, cancellation));
    }

    /// <summary>
    ///     Checks a sale (Gameplay Systems §6.1, then §11.3, in order) and, when it passes, queues its commit. The caller
    ///     has already refused a dead or leaving character.
    /// </summary>
    public CommandRejectionReason TrySell(
        ClientSession session,
        EntityId npc,
        long inventoryItem,
        uint quantity,
        uint commandSequence)
    {
        CharacterSession character = session.Character!;
        if (character.HasInventoryWork)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        CommandRejectionReason reach = NpcReach.Check(session, npc, m_npcReach, out NpcEntity? shopkeeper);
        if (reach != CommandRejectionReason.None)
        {
            return reach;
        }

        if (!shopkeeper!.Definition.HasShop || !character.Inventory.TryGetRow(inventoryItem, out InventoryEntry row))
        {
            return CommandRejectionReason.InvalidTarget;
        }

        ItemDefinition definition = m_content.Items[row.Item];
        if (row.Slot != EquipmentSlot.None
            || definition.SellPrice <= 0
            || quantity > row.Quantity
            || quantity > definition.StackLimit)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        // Refused rather than cut at the cap (Gameplay Systems §11.3).
        long proceeds = definition.SellPrice * quantity;
        if (character.Inventory.Coins > ContentLimits.MaxCurrency - proceeds)
        {
            return CommandRejectionReason.CoinCapReached;
        }

        var operation = new InventoryOperation(
            InventoryOperationKind.Sell,
            commandSequence,
            Guid.NewGuid(),
            item: row.Item,
            quantity: (int)quantity,
            coins: proceeds);
        var commit = new SellCommit(
            operation.OperationId,
            character.Character.Value,
            inventoryItem,
            (int)quantity,
            definition.SellPrice,
            m_time.GetUtcNow().UtcDateTime);
        return TryCommit(session, operation, (store, cancellation) => store.CommitSellAsync(commit, cancellation));
    }

    /// <summary>
    ///     Checks a turn-in (Gameplay Systems §2.2, after the checks of §6.1, in order) and, when it passes, queues its
    ///     commit, which carries the level and experience and the job level and job experience the reward leaves. The caller
    ///     has already refused a dead or
    ///     leaving character.
    /// </summary>
    public CommandRejectionReason TryCompleteQuest(
        ClientSession session,
        EntityId npc,
        QuestDefinitionId quest,
        uint commandSequence)
    {
        CharacterSession character = session.Character!;
        if (character.HasInventoryWork)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        CommandRejectionReason reach = NpcReach.Check(session, npc, m_npcReach, out NpcEntity? giver);
        if (reach != CommandRejectionReason.None)
        {
            return reach;
        }

        if (!m_content.Quests.TryGetValue(quest, out QuestDefinition? definition)
            || definition!.Giver != giver!.Definition.Id)
        {
            return CommandRejectionReason.InvalidTarget;
        }

        if (!character.Quests.TryGet(quest, out CharacterQuest? entry)
            || entry!.IsCompleted
            || entry.Progress < definition.Count)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        // Refused rather than cut at the cap (Gameplay Systems §11.3).
        if (character.Inventory.Coins > ContentLimits.MaxCurrency - definition.Currency)
        {
            return CommandRejectionReason.CoinCapReached;
        }

        LevelProgress carried = m_progression.WithExperience(character.Player, definition.BaseExperience);
        LevelProgress carriedJob = m_progression.WithJobExperience(character.Player, definition.JobExperience);
        var operation = new InventoryOperation(
            InventoryOperationKind.QuestReward,
            commandSequence,
            Guid.NewGuid(),
            coins: definition.Currency,
            quest: quest);
        var commit = new QuestRewardCommit(
            operation.OperationId,
            character.Character.Value,
            character.Player.Job.Value,
            quest.Value,
            entry.Progress,
            definition.Count,
            definition.Currency,
            carried.Level,
            carried.Experience,
            m_time.GetUtcNow().UtcDateTime,
            carriedJob.Level,
            carriedJob.Experience);
        return TryCommit(
            session,
            operation,
            (store, cancellation) => store.CommitQuestRewardAsync(commit, cancellation));
    }

    /// <summary>
    ///     Checks a job change (Gameplay Systems §6.1, after the checks of that section, in order) and, when it passes,
    ///     queues its commit, which takes off a worn weapon the new job cannot wield: the NPC must offer changes, and the
    ///     job must be a first job of the character's job, which must be at its cap. The caller has already refused a
    ///     dead or leaving character.
    /// </summary>
    public CommandRejectionReason TryChangeJob(
        ClientSession session,
        EntityId npc,
        JobDefinitionId job,
        uint commandSequence)
    {
        CharacterSession character = session.Character!;
        if (character.HasInventoryWork)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        CommandRejectionReason reach = NpcReach.Check(session, npc, m_npcReach, out NpcEntity? guildmaster);
        if (reach != CommandRejectionReason.None)
        {
            return reach;
        }

        if (!guildmaster!.Definition.OffersJobChange)
        {
            return CommandRejectionReason.InvalidTarget;
        }

        PlayerEntity player = character.Player;
        if (!m_content.Jobs.TryGetValue(job, out JobDefinition? definition)
            || definition!.BaseJob != player.Job
            || player.JobLevel < NpcServicesBuilder.JobCap(m_content, m_content.Jobs[player.Job]))
        {
            return CommandRejectionReason.RequirementNotMet;
        }

        long worn = character.Inventory.WornIn(EquipmentSlot.Weapon);
        bool isTakenOff = character.Inventory.TryGetRow(worn, out InventoryEntry weapon)
            && m_content.Items[weapon.Item].Equipment?.WeaponType is WeaponType type
            && !definition.CanWield(type);
        var operation = new InventoryOperation(
            InventoryOperationKind.JobChange,
            commandSequence,
            Guid.NewGuid(),
            job: job);
        var commit = new JobChangeCommit(
            operation.OperationId,
            character.Character.Value,
            player.Job.Value,
            job.Value,
            isTakenOff ? CharacterInventory.StoredNameOf(EquipmentSlot.Weapon) : null,
            m_time.GetUtcNow().UtcDateTime);
        return TryCommit(session, operation, (store, cancellation) => store.CommitJobChangeAsync(commit, cancellation));
    }

    // Each card's stack limit, which its return to the bag needs. A card the content no longer holds as an item has
    // none, which leaves no room for it; the load and the storage read keep such a card out of the bag.
    private Dictionary<string, int> CardLimitsOf(IReadOnlyList<ItemDefinitionId> cards)
    {
        var limits = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (ItemDefinitionId card in cards)
        {
            if (m_content.Items.TryGetValue(card, out ItemDefinition? definition))
            {
                limits[card.Value] = definition.StackLimit;
            }
        }

        return limits;
    }

    // As the store returns them: the bag after the destroyed row and an emptied ore leave, each card merged into a stack
    // of it below its limit or in a row of its own within the bag's rows.
    private static bool HasRoomForCards(
        CharacterInventory inventory,
        InventoryEntry destroyed,
        InventoryEntry ore,
        int oreQuantity,
        IReadOnlyDictionary<string, int> limits)
    {
        var bag = new List<(ItemDefinitionId Item, long Quantity)>();
        foreach (InventoryEntry row in inventory.Rows)
        {
            if (row.InventoryItem != destroyed.InventoryItem
                && !(row.InventoryItem == ore.InventoryItem && ore.Quantity == oreQuantity))
            {
                bag.Add((row.Item, row.Quantity));
            }
        }

        foreach (ItemDefinitionId card in destroyed.Cards)
        {
            if (!limits.TryGetValue(card.Value, out int limit))
            {
                return false;
            }

            int stack = limit == 1 ? -1 : bag.FindIndex(held => held.Item == card && held.Quantity < limit);
            if (stack >= 0)
            {
                bag[stack] = (card, bag[stack].Quantity + 1);
            }
            else if (bag.Count < PickupSystem.MaxInventoryRows)
            {
                bag.Add((card, 1));
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    // As the store puts a card back: into the first stack of it below its limit, else a row of its own within the
    // bag's rows. A refine's return can leave a card in more than one stack.
    private static bool HasRoomForCard(CharacterInventory inventory, ItemDefinition card)
    {
        if (card.StackLimit > 1)
        {
            foreach (InventoryEntry row in inventory.Rows)
            {
                if (row.Item == card.Id && row.Quantity < card.StackLimit)
                {
                    return true;
                }
            }
        }

        return inventory.Rows.Count < PickupSystem.MaxInventoryRows;
    }

    // A stackable item merges into its one row; any other takes a new row.
    private static bool HasRoomFor(CharacterInventory inventory, ItemDefinition item, uint quantity)
    {
        if (item.StackLimit > 1)
        {
            foreach (InventoryEntry row in inventory.Rows)
            {
                if (row.Item == item.Id)
                {
                    return row.Quantity <= item.StackLimit - quantity;
                }
            }
        }

        return inventory.Rows.Count < PickupSystem.MaxInventoryRows;
    }

    // The ledger names the row an operation acted on; the row a swap took out of its slot, or a refine's ore, is named
    // here with its item.
    private static ReportedRow[] ReportRows(InventoryOperation operation)
    {
        return operation.Reported == null ? Array.Empty<ReportedRow>() : new[] { operation.Reported };
    }

    private static InboundEventKind CommandOf(InventoryOperation operation)
    {
        return operation.Kind switch
        {
            InventoryOperationKind.Equip => InboundEventKind.Equip,
            InventoryOperationKind.Unequip => InboundEventKind.Unequip,
            InventoryOperationKind.Consume => InboundEventKind.UseItem,
            InventoryOperationKind.Buy => InboundEventKind.Buy,
            InventoryOperationKind.Sell => InboundEventKind.Sell,
            InventoryOperationKind.QuestReward => InboundEventKind.CompleteQuest,
            InventoryOperationKind.JobChange => InboundEventKind.ChangeJob,
            InventoryOperationKind.StorageDeposit => InboundEventKind.StorageDeposit,
            InventoryOperationKind.StorageWithdraw => InboundEventKind.StorageWithdraw,
            InventoryOperationKind.MarketList => InboundEventKind.MarketList,
            InventoryOperationKind.MarketBuy => InboundEventKind.MarketBuy,
            InventoryOperationKind.MarketCollect => InboundEventKind.MarketCollect,
            InventoryOperationKind.Refine => InboundEventKind.RefineItem,
            InventoryOperationKind.InsertCard => InboundEventKind.InsertCard,
            InventoryOperationKind.RemoveCard => InboundEventKind.RemoveCard,
            _ => throw NotAnItemAction(operation)
        };
    }

    private static string CommitNameOf(InventoryOperation operation)
    {
        return operation.Kind switch
        {
            InventoryOperationKind.Equip => EquipOperation,
            InventoryOperationKind.Unequip => UnequipOperation,
            InventoryOperationKind.Consume => ConsumeOperation,
            InventoryOperationKind.Buy => BuyOperation,
            InventoryOperationKind.Sell => SellOperation,
            InventoryOperationKind.QuestReward => QuestRewardOperation,
            InventoryOperationKind.JobChange => JobChangeOperation,
            InventoryOperationKind.StorageDeposit => StorageDepositOperation,
            InventoryOperationKind.StorageWithdraw => StorageWithdrawOperation,
            InventoryOperationKind.MarketList => MarketListOperation,
            InventoryOperationKind.MarketBuy => MarketBuyOperation,
            InventoryOperationKind.MarketCollect => MarketCollectOperation,
            InventoryOperationKind.Refine => RefineOperation,
            InventoryOperationKind.InsertCard => CardInsertOperation,
            InventoryOperationKind.RemoveCard => CardRemoveOperation,
            _ => throw NotAnItemAction(operation)
        };
    }

    private static string LookupNameOf(InventoryOperation operation)
    {
        return operation.Kind switch
        {
            InventoryOperationKind.Equip => EquipLookup,
            InventoryOperationKind.Unequip => UnequipLookup,
            InventoryOperationKind.Consume => ConsumeLookup,
            InventoryOperationKind.Buy => BuyLookup,
            InventoryOperationKind.Sell => SellLookup,
            InventoryOperationKind.QuestReward => QuestRewardLookup,
            InventoryOperationKind.JobChange => JobChangeLookup,
            InventoryOperationKind.StorageDeposit => StorageDepositLookup,
            InventoryOperationKind.StorageWithdraw => StorageWithdrawLookup,
            InventoryOperationKind.MarketList => MarketListLookup,
            InventoryOperationKind.MarketBuy => MarketBuyLookup,
            InventoryOperationKind.MarketCollect => MarketCollectLookup,
            InventoryOperationKind.Refine => RefineLookup,
            InventoryOperationKind.InsertCard => CardInsertLookup,
            InventoryOperationKind.RemoveCard => CardRemoveLookup,
            _ => throw NotAnItemAction(operation)
        };
    }

    /// <summary>
    ///     Checks a refine at the Smith (Gameplay Systems §11.6, in order) and, when it passes and the database takes
    ///     work, draws its one number and queues the commit of what it decided. A refused command draws nothing. The
    ///     caller has already refused a dead, leaving, or trading character.
    /// </summary>
    public CommandRejectionReason TryRefine(
        ClientSession session,
        EntityId npc,
        long inventoryItem,
        byte refineLevel,
        uint commandSequence)
    {
        CharacterSession character = session.Character!;
        if (character.HasInventoryWork)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        CommandRejectionReason reach = NpcReach.Check(session, npc, m_npcReach, out NpcEntity? smith);
        if (reach != CommandRejectionReason.None)
        {
            return reach;
        }

        if (!smith!.Definition.Refines || !character.Inventory.TryGetRow(inventoryItem, out InventoryEntry row))
        {
            return CommandRejectionReason.InvalidTarget;
        }

        RefineDefinitionId? tableId = m_content.Items[row.Item].Refine;
        if (row.Slot != EquipmentSlot.None
            || tableId == null
            || !m_content.RefineTables.TryGetValue(tableId.Value, out RefineTableDefinition? table)
            || row.RefineLevel >= table.LastLevel)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        if (row.RefineLevel != refineLevel)
        {
            return CommandRejectionReason.InvalidTarget;
        }

        if (!TryFindOre(character.Inventory, table, out InventoryEntry ore))
        {
            return CommandRejectionReason.RequirementNotMet;
        }

        long coins = table.Steps[row.RefineLevel].Coins;
        if (character.Inventory.Coins < coins)
        {
            return CommandRejectionReason.NotEnoughCoins;
        }

        // A carded row the draw could destroy needs room for its cards first, so no refusal hangs on the outcome
        // (Gameplay Systems §11.6).
        Dictionary<string, int> cardLimits = CardLimitsOf(row.Cards);
        if (row.Cards.Count > 0
            && table.OnFailure == RefineFailure.Destroy
            && table.Steps[row.RefineLevel].Chance < RefineRules.DrawRange
            && !HasRoomForCards(character.Inventory, row, ore, table.MaterialQuantity, cardLimits))
        {
            return CommandRejectionReason.InventoryFull;
        }

        // The draw is spent only on a command the writer can take (Gameplay Systems §11.6).
        if (!m_persistence.CanEnqueue)
        {
            return CommandRejectionReason.ServiceUnavailable;
        }

        RefineResolution resolution = RefineRules.Resolve(table, row.RefineLevel, m_random.Next(RefineRules.DrawRange));
        var operation = new InventoryOperation(
            InventoryOperationKind.Refine,
            commandSequence,
            Guid.NewGuid(),
            reported: new ReportedRow(ore.InventoryItem, ore.Item.Value),
            item: row.Item,
            coins: coins,
            refine: new RefineAttempt(
                inventoryItem,
                row.Item,
                row.RefineLevel,
                resolution.Level,
                resolution.Outcome,
                row.Cards));
        var commit = new RefineCommit(
            operation.OperationId,
            character.Character.Value,
            inventoryItem,
            row.Item.Value,
            row.RefineLevel,
            resolution.Outcome,
            resolution.Level,
            ore.InventoryItem,
            ore.Item.Value,
            table.MaterialQuantity,
            coins,
            m_time.GetUtcNow().UtcDateTime,
            cardLimits,
            PickupSystem.MaxInventoryRows);
        return TryCommit(session, operation, (store, cancellation) => store.CommitRefineAsync(commit, cancellation));
    }

    /// <summary>
    ///     Checks a removal at the Smith <paramref name="npc" /> of the card <paramref name="card" /> at
    ///     <paramref name="slot" /> of the bag's row <paramref name="inventoryItem" /> (Gameplay Systems §11.7, in order)
    ///     and, when it passes, queues its commit. The caller has already refused a dead, leaving, or trading character.
    /// </summary>
    public CommandRejectionReason TryRemoveCard(
        ClientSession session,
        EntityId npc,
        long inventoryItem,
        int slot,
        ItemDefinitionId card,
        uint commandSequence)
    {
        CharacterSession character = session.Character!;
        if (character.HasInventoryWork)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        CommandRejectionReason reach = NpcReach.Check(session, npc, m_npcReach, out NpcEntity? smith);
        if (reach != CommandRejectionReason.None)
        {
            return reach;
        }

        if (!smith!.Definition.Refines || !character.Inventory.TryGetRow(inventoryItem, out InventoryEntry row))
        {
            return CommandRejectionReason.InvalidTarget;
        }

        if (row.Slot != EquipmentSlot.None)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        if (slot < 1 || slot > row.Cards.Count || row.Cards[slot - 1] != card)
        {
            return CommandRejectionReason.InvalidTarget;
        }

        // A card the content no longer holds as a card has no fee and no stack, so it stays where it is.
        if (!m_content.Items.TryGetValue(card, out ItemDefinition? definition) || definition.Card == null)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        long fee = definition.Card.RemovalFee;
        if (character.Inventory.Coins < fee)
        {
            return CommandRejectionReason.NotEnoughCoins;
        }

        if (!HasRoomForCard(character.Inventory, definition))
        {
            return CommandRejectionReason.InventoryFull;
        }

        var operation = new InventoryOperation(
            InventoryOperationKind.RemoveCard,
            commandSequence,
            Guid.NewGuid(),
            reported: new ReportedRow(inventoryItem, row.Item.Value),
            item: card,
            quantity: slot,
            coins: fee);
        var commit = new RemoveCardCommit(
            operation.OperationId,
            character.Character.Value,
            inventoryItem,
            row.Item.Value,
            slot,
            card.Value,
            fee,
            definition.StackLimit,
            PickupSystem.MaxInventoryRows,
            m_time.GetUtcNow().UtcDateTime);
        return TryCommit(session, operation,
            (store, cancellation) => store.CommitRemoveCardAsync(commit, cancellation));
    }

    /// <summary>
    ///     Checks an insertion of the card in the bag's row <paramref name="cardItem" /> into the bag's row
    ///     <paramref name="inventoryItem" /> (Gameplay Systems §11.7, in order) and, when it passes, queues its commit. No
    ///     NPC, fee, or draw is involved. The caller has already refused a dead, leaving, or trading character.
    /// </summary>
    public CommandRejectionReason TryInsertCard(
        ClientSession session,
        long cardItem,
        long inventoryItem,
        uint commandSequence)
    {
        CharacterSession character = session.Character!;
        if (character.HasInventoryWork)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        if (!character.Inventory.TryGetRow(cardItem, out InventoryEntry card))
        {
            return CommandRejectionReason.InvalidTarget;
        }

        ItemCard? fits = m_content.Items[card.Item].Card;
        if (fits == null)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        if (!character.Inventory.TryGetRow(inventoryItem, out InventoryEntry row))
        {
            return CommandRejectionReason.InvalidTarget;
        }

        ItemDefinition item = m_content.Items[row.Item];
        if (item.Type != fits.Fits || row.Slot != EquipmentSlot.None || row.Cards.Count >= item.Slots)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        var operation = new InventoryOperation(
            InventoryOperationKind.InsertCard,
            commandSequence,
            Guid.NewGuid(),
            reported: new ReportedRow(inventoryItem, row.Item.Value),
            item: card.Item);
        var commit = new InsertCardCommit(
            operation.OperationId,
            character.Character.Value,
            cardItem,
            card.Item.Value,
            inventoryItem,
            row.Item.Value,
            item.Slots,
            m_time.GetUtcNow().UtcDateTime);
        return TryCommit(session, operation,
            (store, cancellation) => store.CommitInsertCardAsync(commit, cancellation));
    }

    // The first unworn row of the table's ore that holds an attempt's quantity.
    private static bool TryFindOre(CharacterInventory inventory, RefineTableDefinition table, out InventoryEntry ore)
    {
        foreach (InventoryEntry row in inventory.Rows)
        {
            if (row.Item == table.Material && row.Slot == EquipmentSlot.None && row.Quantity >= table.MaterialQuantity)
            {
                ore = row;
                return true;
            }
        }

        ore = default!;
        return false;
    }

    // A pickup is the pickup system's; an operation of any other kind reaching here is a server bug.
    private static InvalidOperationException NotAnItemAction(InventoryOperation operation)
    {
        return new InvalidOperationException($"A {operation.Kind} operation is not an item action.");
    }

    /// <summary>
    ///     Checks a read of the account's storage at the Storekeeper (Gameplay Systems §11.4) and, when it passes, queues
    ///     it. One read is in flight per character, and one asked meanwhile is answered by it, as a resync is. The caller
    ///     has already refused a dead, leaving, or trading character.
    /// </summary>
    public CommandRejectionReason TryOpenStorage(ClientSession session, EntityId npc, uint commandSequence)
    {
        CommandRejectionReason reach = NpcReach.Check(session, npc, m_npcReach, out NpcEntity? storekeeper);
        if (reach != CommandRejectionReason.None)
        {
            return reach;
        }

        if (!storekeeper!.Definition.KeepsStorage)
        {
            return CommandRejectionReason.InvalidTarget;
        }

        CharacterSession character = session.Character!;
        if (character.IsReadingStorage)
        {
            return CommandRejectionReason.None;
        }

        uint fee = (uint)storekeeper.Definition.DepositFee!.Value;
        AccountId account = character.Account;
        var read = new PersistenceJob<StoredStorage>(
            StorageReadOperation,
            session.Connection,
            character.Character.Value,
            (store, cancellation) => store.ReadStorageAsync(account, cancellation),
            (outcome, storage) => CompleteRead(character, commandSequence, fee, outcome, storage));
        if (!m_persistence.TryEnqueue(read))
        {
            return CommandRejectionReason.ServiceUnavailable;
        }

        character.IsReadingStorage = true;
        return CommandRejectionReason.None;
    }

    /// <summary>
    ///     Checks a deposit at the Storekeeper (Gameplay Systems §11.4, in order) and, when it passes, queues its commit,
    ///     which checks the room in storage. The caller has already refused a dead, leaving, or trading character.
    /// </summary>
    public CommandRejectionReason TryDeposit(
        ClientSession session,
        EntityId npc,
        long inventoryItem,
        uint quantity,
        uint commandSequence)
    {
        CharacterSession character = session.Character!;
        if (character.HasInventoryWork)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        CommandRejectionReason reach = NpcReach.Check(session, npc, m_npcReach, out NpcEntity? storekeeper);
        if (reach != CommandRejectionReason.None)
        {
            return reach;
        }

        if (!storekeeper!.Definition.KeepsStorage ||
            !character.Inventory.TryGetRow(inventoryItem, out InventoryEntry row))
        {
            return CommandRejectionReason.InvalidTarget;
        }

        if (row.Slot != EquipmentSlot.None || quantity > row.Quantity)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        int fee = storekeeper.Definition.DepositFee!.Value;
        if (character.Inventory.Coins < fee)
        {
            return CommandRejectionReason.NotEnoughCoins;
        }

        var operation = new InventoryOperation(
            InventoryOperationKind.StorageDeposit,
            commandSequence,
            Guid.NewGuid(),
            item: row.Item,
            quantity: (int)quantity,
            coins: fee);
        var commit = new StorageDepositCommit(
            operation.OperationId,
            character.Character.Value,
            inventoryItem,
            (int)quantity,
            fee,
            m_content.Items[row.Item].StackLimit,
            MaxStorageRows,
            m_time.GetUtcNow().UtcDateTime);
        return TryCommitStorage(
            session,
            operation,
            (store, cancellation) => store.CommitStorageDepositAsync(commit, cancellation));
    }

    /// <summary>
    ///     Checks a withdrawal at the Storekeeper (Gameplay Systems §11.4) and, when it passes, queues its commit. The
    ///     server keeps no view of storage, so the commit checks the row, its quantity, and the room in the bag. The caller
    ///     has already refused a dead, leaving, or trading character.
    /// </summary>
    public CommandRejectionReason TryWithdraw(
        ClientSession session,
        EntityId npc,
        long storageItem,
        uint quantity,
        uint commandSequence)
    {
        CharacterSession character = session.Character!;
        if (character.HasInventoryWork)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        CommandRejectionReason reach = NpcReach.Check(session, npc, m_npcReach, out NpcEntity? storekeeper);
        if (reach != CommandRejectionReason.None)
        {
            return reach;
        }

        if (!storekeeper!.Definition.KeepsStorage)
        {
            return CommandRejectionReason.InvalidTarget;
        }

        // No row holds more than a stack can.
        if (quantity > ContentLimits.MaxStack)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        var operation = new InventoryOperation(
            InventoryOperationKind.StorageWithdraw,
            commandSequence,
            Guid.NewGuid(),
            quantity: (int)quantity);
        var commit = new StorageWithdrawCommit(
            operation.OperationId,
            character.Character.Value,
            storageItem,
            (int)quantity,
            m_stackLimits,
            PickupSystem.MaxInventoryRows,
            m_time.GetUtcNow().UtcDateTime);
        return TryCommitStorage(
            session,
            operation,
            (store, cancellation) => store.CommitStorageWithdrawAsync(commit, cancellation));
    }

    private static bool IsStorage(InventoryOperation operation)
    {
        return operation.Kind == InventoryOperationKind.StorageDeposit
            || operation.Kind == InventoryOperationKind.StorageWithdraw;
    }

    private static StorageEntry EntryOf(StoredStorageItem row)
    {
        return new StorageEntry(
            row.Id,
            new ItemDefinitionId(row.ItemDefinitionId),
            (uint)row.Quantity,
            (byte)row.RefineLevel,
            row.Quantity == 0 ? null : CharacterInventory.CardsOf(row.Cards));
    }

    // The read answers the command that asked for it with the storage's parts, or refuses it with 6 when the database
    // did not answer or storage holds an item the loaded content lacks (Gameplay Systems §11.4; Persistence §8, §9).
    private void CompleteRead(
        CharacterSession character,
        uint commandSequence,
        uint fee,
        PersistenceOutcome outcome,
        StoredStorage storage)
    {
        character.IsReadingStorage = false;
        ClientSession? owner = character.Connection;
        if (owner == null || owner.State != SessionState.InWorld)
        {
            return;
        }

        string? unknown = outcome == PersistenceOutcome.Succeeded ? UnknownItemIn(storage) : null;
        if (unknown != null)
        {
            LogStorageContentMismatch(
                m_logger,
                owner.Connection.Value,
                character.Account.Value,
                character.Character.Value,
                unknown,
                null);
        }

        if (outcome != PersistenceOutcome.Succeeded || unknown != null)
        {
            owner.RefusedCommands++;
            m_audit.CommandRefused(owner, InboundEventKind.StorageOpen, CommandRejectionReason.ServiceUnavailable);
            m_sender.Send(
                owner.Connection,
                new CommandRejected(commandSequence, CommandRejectionReason.ServiceUnavailable));
            return;
        }

        var entries = new StorageEntry[storage.Items.Count];
        for (int index = 0; index < entries.Length; index++)
        {
            entries[index] = EntryOf(storage.Items[index]);
        }

        foreach (StorageSnapshot part in StorageSnapshot.CreateParts(storage.Revision, fee, entries))
        {
            m_sender.Send(owner.Connection, part);
        }
    }

    // A stored row's cards are items too, which a withdrawal would bring into the bag (Persistence §8).
    private string? UnknownItemIn(StoredStorage storage)
    {
        foreach (StoredStorageItem row in storage.Items)
        {
            foreach (string definition in row.Cards.Prepend(row.ItemDefinitionId))
            {
                if (!ItemDefinitionId.TryCreate(definition, out ItemDefinitionId item)
                    || !m_content.Items.ContainsKey(item))
                {
                    return definition;
                }
            }
        }

        return null;
    }

    private CommandRejectionReason TryCommitStorage(
        ClientSession session,
        InventoryOperation operation,
        Func<IGameStore, CancellationToken, Task<StorageResult>> commit)
    {
        CharacterSession character = session.Character!;
        var job = new PersistenceJob<StorageResult>(
            CommitNameOf(operation),
            session.Connection,
            character.Character.Value,
            commit,
            (outcome, result) => CompleteStorageCommit(character, outcome, result),
            operation.OperationId.ToString());
        if (!m_persistence.TryEnqueue(job))
        {
            return CommandRejectionReason.ServiceUnavailable;
        }

        character.Operation = operation;
        return CommandRejectionReason.None;
    }

    private void CompleteStorageCommit(CharacterSession character, PersistenceOutcome outcome, StorageResult result)
    {
        if (outcome == PersistenceOutcome.Succeeded)
        {
            SettleStorage(character, result);
            return;
        }

        Unsettle(character);
    }

    private void CompleteStorageLookup(CharacterSession character, PersistenceOutcome outcome, StorageResult? found)
    {
        if (outcome != PersistenceOutcome.Succeeded)
        {
            uint notBefore = outcome == PersistenceOutcome.Failed ? m_tick + m_failedLookupDelayTicks : m_tick;
            m_unsettled.Add((character, notBefore));
            return;
        }

        if (found == null)
        {
            Refuse(character, CommandRejectionReason.ServiceUnavailable);
            return;
        }

        SettleStorage(character, found);
    }

    // The owner hears the bag's row and the coins, then the storage's row, which went up by one revision (Network
    // Protocol §9). A refusal means the client's view of storage was out of date, which a read again puts right: a full
    // side is 5, anything else 1.
    private void SettleStorage(CharacterSession character, StorageResult result)
    {
        if (result.Status != InventoryStatus.Committed)
        {
            Refuse(
                character,
                result.Status == InventoryStatus.InventoryFull
                    ? CommandRejectionReason.InventoryFull
                    : CommandRejectionReason.InvalidTarget);
            return;
        }

        uint prior = character.Inventory.Revision;
        StoredItem[] changed = result.Row == null ? Array.Empty<StoredItem>() : new[] { result.Row };
        InventoryEntry[] rows = character.Inventory.Apply(
            new InventoryResult(InventoryStatus.Committed, result.InventoryRevision, result.Coins, changed));
        m_sender.SendInventoryChange(character, prior, rows);
        StoredStorageItem stored = result.StorageRow!;
        ClientSession? owner = character.Connection;
        if (owner != null && owner.State == SessionState.InWorld)
        {
            m_sender.Send(
                owner.Connection,
                new StorageChanged(result.StorageRevision - 1, result.StorageRevision, EntryOf(stored)));
        }

        InventoryOperation operation = character.Operation!;
        long connection = owner?.Connection.Value ?? 0;
        if (operation.Kind == InventoryOperationKind.StorageDeposit)
        {
            LogDeposited(
                m_logger,
                character.Character.Value,
                connection,
                operation.Quantity,
                stored.ItemDefinitionId,
                operation.Coins,
                operation.OperationId,
                null);
            m_instruments.RecordStorageMove(Deposited);
            if (operation.Coins > 0)
            {
                m_instruments.RecordCoins(StorageFee, operation.Coins);
            }
        }
        else
        {
            LogWithdrawn(
                m_logger,
                character.Character.Value,
                connection,
                operation.Quantity,
                stored.ItemDefinitionId,
                operation.OperationId,
                null);
            m_instruments.RecordStorageMove(Withdrawn);
        }

        Finish(character);
    }

    /// <summary>
    ///     Checks a read of the market board at the Broker (Gameplay Systems §11.5) and, when it passes, queues it. One
    ///     read is in flight per character, and one asked meanwhile is answered by it, as a resync is. The caller has
    ///     already refused a dead, leaving, or trading character.
    /// </summary>
    public CommandRejectionReason TryOpenMarket(ClientSession session, EntityId npc, uint commandSequence)
    {
        CommandRejectionReason reach = NpcReach.Check(session, npc, m_npcReach, out NpcEntity? broker);
        if (reach != CommandRejectionReason.None)
        {
            return reach;
        }

        if (!broker!.Definition.KeepsMarket)
        {
            return CommandRejectionReason.InvalidTarget;
        }

        CharacterSession character = session.Character!;
        if (character.IsReadingMarket)
        {
            return CommandRejectionReason.None;
        }

        if (!TryReadMarket(
                character,
                m_marketTerms,
                (outcome, market, at) => CompleteMarketRead(character, commandSequence, outcome, market, at)))
        {
            return CommandRejectionReason.ServiceUnavailable;
        }

        character.IsReadingMarket = true;
        return CommandRejectionReason.None;
    }

    /// <summary>
    ///     Checks a listing at the Broker (Gameplay Systems §11.5, in order) and, when it passes, queues its commit, which
    ///     settles the item's price, takes the fee at it, and refuses a ninth listing or a fee the coins cannot pay. The
    ///     caller has already refused a dead, leaving, or trading character.
    /// </summary>
    public CommandRejectionReason TryListOnMarket(
        ClientSession session,
        EntityId npc,
        long inventoryItem,
        uint quantity,
        uint commandSequence)
    {
        CharacterSession character = session.Character!;
        if (character.HasInventoryWork)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        CommandRejectionReason reach = NpcReach.Check(session, npc, m_npcReach, out NpcEntity? broker);
        if (reach != CommandRejectionReason.None)
        {
            return reach;
        }

        if (!broker!.Definition.KeepsMarket || !character.Inventory.TryGetRow(inventoryItem, out InventoryEntry row))
        {
            return CommandRejectionReason.InvalidTarget;
        }

        // A refined or carded row is one of a kind, which the board, selling units of an item, cannot carry
        // (Gameplay Systems §11.5, §11.7).
        ItemMarket? terms = m_content.Items[row.Item].Market;
        if (row.Slot != EquipmentSlot.None
            || row.RefineLevel > 0
            || row.Cards.Count > 0
            || terms == null
            || quantity > row.Quantity)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        if (character.Inventory.Coins < MarketTerms.MinimumFee)
        {
            return CommandRejectionReason.NotEnoughCoins;
        }

        var operation = new InventoryOperation(
            InventoryOperationKind.MarketList,
            commandSequence,
            Guid.NewGuid(),
            item: row.Item,
            quantity: (int)quantity);
        var commit = new MarketListCommit(
            operation.OperationId,
            character.Character.Value,
            inventoryItem,
            row.Item.Value,
            (int)quantity,
            terms,
            m_pricePeriodMs,
            m_listingDurationMs,
            MarketTerms.MaxListings,
            m_time.GetUtcNow().UtcDateTime);
        return TryCommitMarket(
            session,
            operation,
            (store, cancellation) => store.CommitMarketListAsync(commit, cancellation));
    }

    /// <summary>
    ///     Checks a purchase on the board (Gameplay Systems §11.5, in order) and, when it passes, queues its commit, which
    ///     takes the oldest units other accounts listed at the settled price and refuses a price past the limit or too few
    ///     units. The caller has already refused a dead, leaving, or trading character.
    /// </summary>
    public CommandRejectionReason TryBuyOnMarket(
        ClientSession session,
        EntityId npc,
        ItemDefinitionId item,
        uint quantity,
        uint priceLimit,
        uint commandSequence)
    {
        CharacterSession character = session.Character!;
        if (character.HasInventoryWork)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        CommandRejectionReason reach = NpcReach.Check(session, npc, m_npcReach, out NpcEntity? broker);
        if (reach != CommandRejectionReason.None)
        {
            return reach;
        }

        if (!broker!.Definition.KeepsMarket
            || !m_content.Items.TryGetValue(item, out ItemDefinition? definition)
            || definition!.Market == null)
        {
            return CommandRejectionReason.InvalidTarget;
        }

        // Up to the stack limit, so a purchase adds at most one row, the one the ledger names when an answer is lost.
        if (quantity > definition.StackLimit)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        if (character.Inventory.Coins < (long)quantity * priceLimit)
        {
            return CommandRejectionReason.NotEnoughCoins;
        }

        if (!HasRoomFor(character.Inventory, definition, quantity))
        {
            return CommandRejectionReason.InventoryFull;
        }

        var operation = new InventoryOperation(
            InventoryOperationKind.MarketBuy,
            commandSequence,
            Guid.NewGuid(),
            item: item,
            quantity: (int)quantity);

        // The coins cover the limit, so it is within the coin cap.
        var commit = new MarketBuyCommit(
            operation.OperationId,
            character.Character.Value,
            item.Value,
            (int)quantity,
            (int)priceLimit,
            definition.StackLimit,
            PickupSystem.MaxInventoryRows,
            definition.Market,
            m_pricePeriodMs,
            m_time.GetUtcNow().UtcDateTime);
        return TryCommitMarket(
            session,
            operation,
            (store, cancellation) => store.CommitMarketBuyAsync(commit, cancellation));
    }

    /// <summary>
    ///     Checks a collection at the Broker (Gameplay Systems §11.5) and, when it passes, queues its commit. The server
    ///     keeps no view of the board, so the commit checks that the listing is the character's, the coin cap, and the
    ///     room in the bag. The caller has already refused a dead, leaving, or trading character.
    /// </summary>
    public CommandRejectionReason TryCollectListing(
        ClientSession session,
        EntityId npc,
        long listing,
        uint commandSequence)
    {
        CharacterSession character = session.Character!;
        if (character.HasInventoryWork)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        CommandRejectionReason reach = NpcReach.Check(session, npc, m_npcReach, out NpcEntity? broker);
        if (reach != CommandRejectionReason.None)
        {
            return reach;
        }

        if (!broker!.Definition.KeepsMarket)
        {
            return CommandRejectionReason.InvalidTarget;
        }

        var operation = new InventoryOperation(
            InventoryOperationKind.MarketCollect,
            commandSequence,
            Guid.NewGuid(),
            listing: listing);
        var commit = new MarketCollectCommit(
            operation.OperationId,
            character.Character.Value,
            listing,
            m_stackLimits,
            m_marketTerms,
            PickupSystem.MaxInventoryRows,
            m_pricePeriodMs,
            m_time.GetUtcNow().UtcDateTime);
        return TryCommitMarket(
            session,
            operation,
            (store, cancellation) => store.CommitMarketCollectAsync(commit, cancellation));
    }

    private CommandRejectionReason TryCommitMarket(
        ClientSession session,
        InventoryOperation operation,
        Func<IGameStore, CancellationToken, Task<MarketResult>> commit)
    {
        CharacterSession character = session.Character!;
        var job = new PersistenceJob<MarketResult>(
            CommitNameOf(operation),
            session.Connection,
            character.Character.Value,
            commit,
            (outcome, result) => CompleteMarketCommit(character, outcome, result),
            operation.OperationId.ToString());
        if (!m_persistence.TryEnqueue(job))
        {
            return CommandRejectionReason.ServiceUnavailable;
        }

        character.Operation = operation;
        return CommandRejectionReason.None;
    }

    private void CompleteMarketCommit(CharacterSession character, PersistenceOutcome outcome, MarketResult result)
    {
        if (outcome == PersistenceOutcome.Succeeded)
        {
            SettleMarket(character, result);
            return;
        }

        Unsettle(character);
    }

    // The owner hears the bag's row and the coins, then what became of its listing, then the item's entry and its
    // listings again; each seller a purchase paid hears of its sale while a connection controls it in the world
    // (Network Protocol §9).
    private void SettleMarket(CharacterSession character, MarketResult result)
    {
        if (result.Status != InventoryStatus.Committed)
        {
            Refuse(character, MarketRefusalOf(character, result));
            return;
        }

        uint prior = character.Inventory.Revision;
        InventoryEntry[] rows = character.Inventory.Apply(
            new InventoryResult(InventoryStatus.Committed, result.InventoryRevision, result.Coins, result.Rows));
        m_sender.SendInventoryChange(character, prior, rows);
        InventoryOperation operation = character.Operation!;
        var item = new ItemDefinitionId(result.ItemDefinitionId);
        long id = character.Character.Value;
        switch (operation.Kind)
        {
            case InventoryOperationKind.MarketList:
                Tell(
                    character,
                    new MarketEvent(
                        MarketEventKind.Listed,
                        result.ListingId,
                        item,
                        (uint)result.Quantity,
                        (uint)result.CoinsMoved));
                LogListed(
                    m_logger,
                    id,
                    result.Quantity,
                    item.Value,
                    result.ListingId,
                    result.CoinsMoved,
                    operation.OperationId,
                    null);
                m_instruments.RecordMarketListing();
                m_instruments.RecordCoins(MarketFee, result.CoinsMoved);
                break;
            case InventoryOperationKind.MarketBuy:
                LogMarketSold(
                    m_logger,
                    id,
                    result.Quantity,
                    item.Value,
                    result.CoinsMoved,
                    string.Join(", ", result.Fills.Select(fill => fill.ListingId)),
                    operation.OperationId,
                    null);
                m_instruments.RecordMarketSales(result.Fills.Count);
                foreach (MarketFill fill in result.Fills)
                {
                    TellSeller(fill, item);
                }

                break;
            default:
                Tell(
                    character,
                    new MarketEvent(
                        MarketEventKind.Collected,
                        result.ListingId,
                        item,
                        (uint)result.Quantity,
                        (uint)result.CoinsMoved));
                LogCollected(
                    m_logger,
                    id,
                    result.ListingId,
                    result.Quantity,
                    item.Value,
                    result.CoinsMoved,
                    operation.OperationId,
                    null);
                if (result.Tax > 0)
                {
                    m_instruments.RecordCoins(MarketTax, result.Tax);
                }

                break;
        }

        PushMarket(character, result.ItemDefinitionId);
        Finish(character);
    }

    // Each refusal of the store has its reason (Network Protocol §11). A collection of an item the loaded content lacks
    // is logged and refused as unavailable; its listing is kept (Persistence §8).
    private CommandRejectionReason MarketRefusalOf(CharacterSession character, MarketResult result)
    {
        switch (result.Status)
        {
            case InventoryStatus.NotEnoughCoins:
                return CommandRejectionReason.NotEnoughCoins;
            case InventoryStatus.CoinCapReached:
                return CommandRejectionReason.CoinCapReached;
            case InventoryStatus.ListingCapReached:
                return CommandRejectionReason.RequirementNotMet;
            case InventoryStatus.InventoryFull:
                return CommandRejectionReason.InventoryFull;
            case InventoryStatus.Refused
                when character.Operation!.Kind == InventoryOperationKind.MarketCollect
                && result.ItemDefinitionId.Length > 0:
                LogMarketContentMismatch(
                    m_logger,
                    character.Character.Value,
                    character.Connection?.Connection.Value ?? 0,
                    character.Operation.Listing,
                    result.ItemDefinitionId,
                    null);
                return CommandRejectionReason.ServiceUnavailable;
            default:
                return CommandRejectionReason.InvalidTarget;
        }
    }

    private void Tell(CharacterSession character, MarketEvent notice)
    {
        ClientSession? owner = character.Connection;
        if (owner != null && owner.State == SessionState.InWorld)
        {
            m_sender.Send(owner.Connection, notice);
        }
    }

    // A sale reaches its seller alone and never names the buyer (Network Protocol §9).
    private void TellSeller(MarketFill fill, ItemDefinitionId item)
    {
        if (m_sessions.TryGetByCharacter(new CharacterId(fill.SellerCharacterId), out ClientSession? seller)
            && seller!.State == SessionState.InWorld)
        {
            m_sender.Send(
                seller.Connection,
                new MarketEvent(MarketEventKind.Sold, fill.ListingId, item, (uint)fill.Units, (uint)fill.Gross));
        }
    }

    // After its own commit a character hears the item's entry of the board and its listings again (Network Protocol
    // §9): a read of that one item, or of every listable item when a lookup could not say which. A read that fails
    // sends nothing; an open reads again.
    private void PushMarket(CharacterSession character, string item)
    {
        TryReadMarket(
            character,
            MarketItemsOf(item),
            (outcome, market, at) =>
            {
                ClientSession? owner = character.Connection;
                if (outcome == PersistenceOutcome.Succeeded && owner != null && owner.State == SessionState.InWorld)
                {
                    SendMarket(owner, market, at);
                }
            });
    }

    // An item the content no longer lists has no entry to read; the character's listings are read all the same.
    private IReadOnlyDictionary<string, ItemMarket> MarketItemsOf(string item)
    {
        if (item.Length == 0)
        {
            return m_marketTerms;
        }

        var items = new Dictionary<string, ItemMarket>(StringComparer.Ordinal);
        if (m_marketTerms.TryGetValue(item, out ItemMarket? terms))
        {
            items.Add(item, terms);
        }

        return items;
    }

    // Keyed to no character, so no waiting checkpoint of the reader is pulled forward (Persistence §5).
    private bool TryReadMarket(
        CharacterSession character,
        IReadOnlyDictionary<string, ItemMarket> items,
        Action<PersistenceOutcome, StoredMarket, DateTime> complete)
    {
        DateTime at = m_time.GetUtcNow().UtcDateTime;
        var read = new MarketRead(character.Character.Value, items, m_pricePeriodMs, at);
        var job = new PersistenceJob<StoredMarket>(
            MarketReadOperation,
            character.Connection?.Connection ?? default,
            0,
            (store, cancellation) => store.ReadMarketAsync(read, cancellation),
            (outcome, market) => complete(outcome, market, at));
        return m_persistence.TryEnqueue(job);
    }

    // The read answers the command that asked for it, or refuses it with 6 when the database did not answer
    // (Persistence §9).
    private void CompleteMarketRead(
        CharacterSession character,
        uint commandSequence,
        PersistenceOutcome outcome,
        StoredMarket market,
        DateTime at)
    {
        character.IsReadingMarket = false;
        ClientSession? owner = character.Connection;
        if (owner == null || owner.State != SessionState.InWorld)
        {
            return;
        }

        if (outcome != PersistenceOutcome.Succeeded)
        {
            owner.RefusedCommands++;
            m_audit.CommandRefused(owner, InboundEventKind.MarketOpen, CommandRejectionReason.ServiceUnavailable);
            m_sender.Send(
                owner.Connection,
                new CommandRejected(commandSequence, CommandRejectionReason.ServiceUnavailable));
            return;
        }

        SendMarket(owner, market, at);
    }

    // The board's entries in as many messages as they need, then every listing of the reader, which ends the read.
    private void SendMarket(ClientSession owner, StoredMarket market, DateTime at)
    {
        var entries = new MarketBoardEntry[market.Entries.Count];
        for (int index = 0; index < entries.Length; index++)
        {
            StoredMarketEntry entry = market.Entries[index];
            entries[index] = new MarketBoardEntry(
                new ItemDefinitionId(entry.ItemDefinitionId),
                (uint)entry.Price,
                (uint)Math.Min(entry.Stock, uint.MaxValue));
        }

        foreach (MarketBoard part in MarketBoard.CreateParts(entries))
        {
            m_sender.Send(owner.Connection, part);
        }

        MarketListingEntry[] listings = market.Listings
            .Take(MarketMine.MaxEntries)
            .Select(listing => ListingOf(listing, at))
            .ToArray();
        m_sender.Send(owner.Connection, new MarketMine(listings));
    }

    // What collecting the listing would pay now, its sales less the tax, and the whole seconds it still sells for.
    private static MarketListingEntry ListingOf(StoredMarketListing listing, DateTime at)
    {
        double secondsLeft = Math.Ceiling((listing.ExpiresAt - at).TotalSeconds);
        return new MarketListingEntry(
            listing.Id,
            new ItemDefinitionId(listing.ItemDefinitionId),
            (uint)listing.UnitsLeft,
            (uint)(listing.UnitsListed - listing.UnitsLeft),
            (uint)(listing.GrossCoins - MarketPricing.Tax(listing.GrossCoins)),
            secondsLeft > 0 ? (uint)Math.Min(secondsLeft, uint.MaxValue) : 0);
    }


    private CommandRejectionReason TryCommit(
        ClientSession session,
        InventoryOperation operation,
        Func<IGameStore, CancellationToken, Task<InventoryResult>> commit)
    {
        CharacterSession character = session.Character!;
        var job = new PersistenceJob<InventoryResult>(
            CommitNameOf(operation),
            session.Connection,
            character.Character.Value,
            commit,
            (outcome, result) => CompleteCommit(character, outcome, result),
            operation.OperationId.ToString());
        if (!m_persistence.TryEnqueue(job))
        {
            return CommandRejectionReason.ServiceUnavailable;
        }

        character.Operation = operation;
        return CommandRejectionReason.None;
    }

    private void CompleteCommit(CharacterSession character, PersistenceOutcome outcome, InventoryResult result)
    {
        if (outcome == PersistenceOutcome.Succeeded)
        {
            Settle(character, result);
            return;
        }

        Unsettle(character);
    }

    // The commit may have happened; only the ledger can say.
    private void Unsettle(CharacterSession character)
    {
        InventoryOperation operation = character.Operation!;
        LogUnsettled(
            m_logger,
            operation.Kind,
            operation.OperationId,
            character.Character.Value,
            character.Connection?.Connection.Value ?? 0,
            null);
        if (!TryQueueLookup(character))
        {
            m_unsettled.Add((character, m_tick));
        }
    }

    private bool TryQueueLookup(CharacterSession character)
    {
        InventoryOperation operation = character.Operation!;
        long id = character.Character.Value;
        if (IsStorage(operation))
        {
            var storageLookup = new PersistenceJob<StorageResult?>(
                LookupNameOf(operation),
                character.Connection?.Connection ?? default,
                id,
                (store, cancellation) => store.FindStorageOperationAsync(operation.OperationId, id, cancellation),
                (outcome, found) => CompleteStorageLookup(character, outcome, found),
                operation.OperationId.ToString());
            return m_persistence.TryEnqueue(storageLookup);
        }

        ReportedRow[] reportRows = ReportRows(operation);

        // A change that took nothing off keeps no ledger row, so the stored job says whether it committed
        // (Persistence §5).
        Func<IGameStore, CancellationToken, Task<InventoryResult?>> find =
            operation.Kind == InventoryOperationKind.JobChange
                ? (store, cancellation) =>
                    store.FindJobChangeAsync(operation.OperationId, id, operation.Job.Value, cancellation)
                : (store, cancellation) =>
                    store.FindOperationAsync(operation.OperationId, id, reportRows, cancellation);
        var lookup = new PersistenceJob<InventoryResult?>(
            LookupNameOf(operation),
            character.Connection?.Connection ?? default,
            id,
            find,
            (outcome, found) => CompleteLookup(character, outcome, found),
            operation.OperationId.ToString());
        return m_persistence.TryEnqueue(lookup);
    }

    private void CompleteLookup(CharacterSession character, PersistenceOutcome outcome, InventoryResult? found)
    {
        if (outcome != PersistenceOutcome.Succeeded)
        {
            uint notBefore = outcome == PersistenceOutcome.Failed ? m_tick + m_failedLookupDelayTicks : m_tick;
            m_unsettled.Add((character, notBefore));
            return;
        }

        if (found == null)
        {
            Refuse(character, CommandRejectionReason.ServiceUnavailable);
            return;
        }

        Settle(character, found);
    }

    private void Settle(CharacterSession character, InventoryResult result)
    {
        // The database refused what the server's settled copy allowed: that copy is out of date, which nothing the
        // character sent could cause. A refine's refusals each keep their reason (Network Protocol §11).
        if (result.Status != InventoryStatus.Committed)
        {
            Refuse(
                character,
                character.Operation!.Kind switch
                {
                    InventoryOperationKind.Refine => RefineRefusalOf(result.Status),
                    InventoryOperationKind.InsertCard => CardRefusalOf(result.Status),
                    InventoryOperationKind.RemoveCard => CardRefusalOf(result.Status),
                    _ => CommandRejectionReason.InvalidTarget
                });
            return;
        }

        uint prior = character.Inventory.Revision;
        long weaponBefore = character.Inventory.WornIn(EquipmentSlot.Weapon);
        InventoryEntry[] rows = character.Inventory.Apply(result);
        InventoryOperation operation = character.Operation!;

        // A job change that took nothing off leaves the inventory as it was.
        if (operation.Kind != InventoryOperationKind.JobChange || result.InventoryRevision != prior)
        {
            m_sender.SendInventoryChange(character, prior, rows);
        }

        long connection = character.Connection?.Connection.Value ?? 0;
        switch (operation.Kind)
        {
            case InventoryOperationKind.Equip:
            case InventoryOperationKind.Unequip:
                Wear(character);
                break;
            case InventoryOperationKind.Consume:
                Restore(character.Player, m_content.Items[operation.Item].Effect!, operation.Deaths);
                break;
            case InventoryOperationKind.Buy:
                LogBought(
                    m_logger,
                    character.Character.Value,
                    connection,
                    operation.Quantity,
                    operation.Item.Value,
                    operation.Coins,
                    operation.OperationId,
                    null);
                m_instruments.RecordCoins(BuyOperation, operation.Coins);
                break;
            case InventoryOperationKind.Sell:
                LogSold(
                    m_logger,
                    character.Character.Value,
                    connection,
                    operation.Quantity,
                    operation.Item.Value,
                    operation.Coins,
                    operation.OperationId,
                    null);
                m_instruments.RecordCoins(SellOperation, operation.Coins);
                break;
            case InventoryOperationKind.QuestReward:
                m_progression.CompleteQuest(character, operation.Quest, operation.OperationId);
                m_instruments.RecordCoins(QuestRewardOperation, operation.Coins);
                break;
            case InventoryOperationKind.JobChange:
                ChangeJob(character, operation.Job, rows);
                break;
            case InventoryOperationKind.MarketList:
            case InventoryOperationKind.MarketBuy:
            case InventoryOperationKind.MarketCollect:
                // Only a lost answer settles here: the ledger names no listing, fee, or sale, so no notice goes out and
                // the board's read tells the rest (Network Protocol §9).
                PushMarket(character, operation.Item.Value);
                break;
            case InventoryOperationKind.Refine:
                Refined(character, operation);
                break;
            case InventoryOperationKind.InsertCard:
                CardInserted(character, operation);
                break;
            case InventoryOperationKind.RemoveCard:
                CardRemoved(character, operation);
                break;
            default:
                throw NotAnItemAction(operation);
        }

        // An unequip, a swap, or a job change's unequip ends a converter's element (Gameplay Systems §9.1).
        if (character.Inventory.WornIn(EquipmentSlot.Weapon) != weaponBefore)
        {
            m_statusEffects.EndAttackElement(character.Player);
        }

        Finish(character);
    }

    private static CommandRejectionReason RefineRefusalOf(InventoryStatus status)
    {
        return status switch
        {
            InventoryStatus.MaterialShort => CommandRejectionReason.RequirementNotMet,
            InventoryStatus.RowWorn => CommandRejectionReason.NotAllowedNow,
            InventoryStatus.NotEnoughCoins => CommandRejectionReason.NotEnoughCoins,
            InventoryStatus.InventoryFull => CommandRejectionReason.InventoryFull,
            _ => CommandRejectionReason.InvalidTarget
        };
    }

    // A worn row or one without a free slot is 3, coins short 10, a bag without room 5; a row gone or changed is 1
    // (Gameplay Systems §11.7).
    private static CommandRejectionReason CardRefusalOf(InventoryStatus status)
    {
        return status switch
        {
            InventoryStatus.RowWorn => CommandRejectionReason.NotAllowedNow,
            InventoryStatus.NoFreeSlot => CommandRejectionReason.NotAllowedNow,
            InventoryStatus.NotEnoughCoins => CommandRejectionReason.NotEnoughCoins,
            InventoryStatus.InventoryFull => CommandRejectionReason.InventoryFull,
            _ => CommandRejectionReason.InvalidTarget
        };
    }

    // The owner hears its bag's change alone; nothing is announced, and a worn row never changes, so no statistic does
    // (Gameplay Systems §11.7).
    private void CardInserted(CharacterSession character, InventoryOperation operation)
    {
        ReportedRow row = operation.Reported!;
        int slot = character.Inventory.TryGetRow(row.Id, out InventoryEntry held) ? held.Cards.Count : 0;
        LogCardInserted(
            m_logger,
            character.Character.Value,
            operation.Item.Value,
            row.Id,
            row.ItemDefinitionId,
            slot,
            operation.OperationId,
            null);
        m_instruments.RecordCardInserted();
    }

    private void CardRemoved(CharacterSession character, InventoryOperation operation)
    {
        LogCardRemoved(
            m_logger,
            character.Character.Value,
            operation.Item.Value,
            operation.Reported!.Id,
            operation.Quantity,
            operation.Coins,
            operation.OperationId,
            null);
        m_instruments.RecordCardRemoved();
        m_instruments.RecordCoins(CardRemovalCoins, operation.Coins);
    }

    // The refiner alone hears the outcome, after its bag's change, a lost answer's too: the outcome is the operation's
    // (Network Protocol §9). A worn row never refines, so nothing else changes.
    private void Refined(CharacterSession character, InventoryOperation operation)
    {
        RefineAttempt attempt = operation.Refine!;
        ClientSession? owner = character.Connection;
        if (owner != null && owner.State == SessionState.InWorld)
        {
            m_sender.Send(
                owner.Connection,
                new RefineOutcome(
                    attempt.InventoryItem,
                    attempt.Item,
                    (byte)attempt.LevelBefore,
                    (byte)attempt.LevelAfter,
                    attempt.Outcome));
        }

        LogRefined(
            m_logger,
            character.Character.Value,
            attempt.Item.Value,
            attempt.LevelBefore,
            attempt.LevelAfter,
            RefineOutcomes.WordOf(attempt.Outcome),
            operation.OperationId,
            null);
        if (attempt.Outcome == RefineOutcomeKind.Destroyed && attempt.Cards.Count > 0)
        {
            LogCardsReturned(
                m_logger,
                character.Character.Value,
                attempt.Item.Value,
                string.Join(", ", attempt.Cards.Select(card => card.Value)),
                operation.OperationId,
                null);
        }

        m_instruments.RecordRefine(attempt.Outcome);
        m_instruments.RecordCoins(RefineOperation, operation.Coins);
    }

    // Once committed, a cast ends, the job changes with what the commit left worn, the owner hears of new maximums, is
    // owed its skill list and its sheet, and those who see the character get its new body (Gameplay Systems §6.1).
    // Status effects run out unchanged.
    private void ChangeJob(CharacterSession character, JobDefinitionId job, InventoryEntry[] takenOff)
    {
        PlayerEntity player = character.Player;
        int maxHealth = player.MaxHealth;
        int maxSpirit = player.MaxSpirit;
        m_combat.InterruptCast(player);
        WornEquipment.Dress(player, m_content, character.Inventory);
        m_builds.ChangeJob(
            player,
            character.Connection?.Connection ?? default,
            job,
            takenOff.Length > 0 ? takenOff[0].Item : default);
        if (player.MaxHealth != maxHealth || player.MaxSpirit != maxSpirit)
        {
            m_sender.SendHealth(player);
        }

        if (character.Connection != null)
        {
            character.Connection.NeedsSkillList = true;
        }
    }

    // A use restores what its item gives once its commit returns, capped at the maximums, starts its status from that
    // tick, and ends the ailments it cures (Gameplay Systems §11.2). A character that died meanwhile gets nothing, even one already respawned: the
    // unit is spent either way.
    private void Restore(PlayerEntity player, ItemEffect effect, int deathsAtUse)
    {
        if (player.IsDead || player.Deaths != deathsAtUse)
        {
            return;
        }

        if (effect.Health > 0 || effect.Spirit > 0)
        {
            player.CurrentHealth = Math.Min(player.MaxHealth, player.CurrentHealth + effect.Health);
            player.CurrentSpirit = Math.Min(player.MaxSpirit, player.CurrentSpirit + effect.Spirit);
            m_sender.SendHealth(player);
        }

        if (effect.Status is StatusDefinitionId status)
        {
            // Commits settle while the commands drain, before this tick's persistence phase moves m_tick on, so the
            // tick under way is the one after it, and its time is the status system's for that tick.
            long now = (long)m_tick * MillisecondsPerSecond / m_tickRate;
            m_statusEffects.Apply(player, status, default, now + effect.StatusDurationMs);
        }

        m_statusEffects.Cure(player, effect.Cures);
    }

    // The statistics follow the slots as committed (Gameplay Systems §2); the owner hears of new maximums as of any
    // other change of its HP or SP.
    private void Wear(CharacterSession character)
    {
        PlayerEntity player = character.Player;
        int maxHealth = player.MaxHealth;
        int maxSpirit = player.MaxSpirit;
        WornEquipment.Dress(player, m_content, character.Inventory);
        m_stats.Recalculate(player, m_content.Jobs[player.Job]);
        if (player.MaxHealth != maxHealth || player.MaxSpirit != maxSpirit)
        {
            m_sender.SendHealth(player);
        }
    }

    private void Refuse(CharacterSession character, CommandRejectionReason reason)
    {
        InventoryOperation operation = character.Operation!;
        ClientSession? owner = character.Connection;
        if (owner != null && owner.State == SessionState.InWorld)
        {
            owner.RefusedCommands++;
            m_audit.CommandRefused(owner, CommandOf(operation), reason);
            m_sender.Send(owner.Connection, new CommandRejected(operation.CommandSequence, reason));
        }

        Finish(character);
    }

    private void Finish(CharacterSession character)
    {
        bool wasProgressCommit = character.Operation?.Kind == InventoryOperationKind.QuestReward
            || character.Operation?.Kind == InventoryOperationKind.JobChange;
        character.Operation = null;

        // Checkpoints taken while a turn-in or a job change was in flight left the levels and experience to it
        // (Persistence §6), the reward's own level-up among them; once it is over, what the character holds is saved
        // at once. A logout or a removal waiting for it writes its own final checkpoint.
        if (wasProgressCommit && !character.IsLoggingOut && !character.IsExpelled && !character.IsRemovalDeferred)
        {
            m_lifetime.QueueCheckpoint(character);
        }

        Settled?.Invoke(character);
        m_lifetime.OnOperationSettled(character);
    }
}
}
