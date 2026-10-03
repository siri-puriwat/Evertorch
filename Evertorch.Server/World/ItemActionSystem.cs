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
///     Equips, unequips, uses, buys, and sells items, turns in quests, changes jobs, and reads, fills, and empties the
///     account's storage (Gameplay Systems §2.2, §6.1, §11.1–§11.4; Persistence §5). The checks happen on the tick thread;
///     the inventory and the coins change only through
///     the
///     commit, and the server's copy of them, the statistics, HP and SP, the quests, and the owner learn of it once its
///     result is back. A commit whose answer was lost is settled from the ledger before the character may start another
///     inventory operation.
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

    private static readonly Action<ILogger, long, long, int, string, long, Guid, Exception?> LogSold =
        LoggerMessage.Define<long, long, int, string, long, Guid>(
            LogLevel.Information,
            new EventId(1008, "ItemSold"),
            "Character {Character} on connection {Connection} sold {Quantity} {Item} for {Coins} coins (operation "
            + "{OperationId}).");

    private readonly PersistenceWorker m_persistence;
    private readonly MessageSender m_sender;
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
    private readonly float m_npcReach;
    private readonly uint m_failedLookupDelayTicks;
    private readonly List<(CharacterSession Character, uint NotBefore)> m_unsettled = new();
    private readonly IReadOnlyDictionary<string, int> m_stackLimits;
    private uint m_tick;

    public ItemActionSystem(
        PersistenceWorker persistence,
        MessageSender sender,
        CharacterLifetime lifetime,
        CharacterStats stats,
        ServerContent content,
        TimeProvider time,
        IOptions<SimulationOptions> simulation,
        IOptions<WorldOptions> world,
        ServerInstruments instruments,
        CharacterProgression progression,
        CharacterBuilds builds,
        CombatSystem combat,
        AuditLog audit,
        ILogger<ItemActionSystem> logger)
    {
        m_persistence = persistence;
        m_sender = sender;
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
        m_npcReach = NpcInteraction.Range + world.Value.AttackRangeTolerance;
        m_stackLimits =
            content.Items.Values.ToDictionary(item => item.Id.Value, item => item.StackLimit, StringComparer.Ordinal);
        m_failedLookupDelayTicks =
            (uint)((long)FailedLookupDelayMs * simulation.Value.TickRate / MillisecondsPerSecond);
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
    ///     Checks an equip (Gameplay Systems §11.1) and, when it passes, queues its commit: the row goes into the slot its
    ///     item fills, and a row the slot held comes out of it. A weapon of a type the job cannot wield is refused with
    ///     <see cref="CommandRejectionReason.RequirementNotMet" />. The caller has already refused a dead or leaving
    ///     character.
    /// </summary>
    public CommandRejectionReason TryEquip(ClientSession session, long inventoryItem, uint commandSequence)
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

        ItemDefinition item = m_content.Items[row.Item];
        EquipmentSlot slot = item.Slot;
        long worn = character.Inventory.WornIn(slot);
        if (slot == EquipmentSlot.None || worn == inventoryItem)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        WeaponType? weaponType = item.Equipment?.WeaponType;
        if (weaponType.HasValue && !m_content.Jobs[character.Player.Job].CanWield(weaponType.Value))
        {
            return CommandRejectionReason.RequirementNotMet;
        }

        var operation = new InventoryOperation(
            InventoryOperationKind.Equip,
            commandSequence,
            Guid.NewGuid(),
            displacedRow: worn);
        var commit = new EquipCommit(
            operation.OperationId,
            character.Character.Value,
            inventoryItem,
            CharacterInventory.StoredNameOf(slot),
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

        if (m_content.Items[row.Item].Effect == null)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        var operation = new InventoryOperation(
            InventoryOperationKind.Consume,
            commandSequence,
            Guid.NewGuid(),
            item: row.Item);
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

    // The ledger names the row an operation acted on; the row a swap took out of its slot is named here.
    private static long[] ReportRows(InventoryOperation operation)
    {
        return operation.DisplacedRow == 0 ? Array.Empty<long>() : new[] { operation.DisplacedRow };
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
            _ => throw NotAnItemAction(operation)
        };
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
            (byte)row.RefineLevel);
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

    private string? UnknownItemIn(StoredStorage storage)
    {
        foreach (StoredStorageItem row in storage.Items)
        {
            if (!ItemDefinitionId.TryCreate(row.ItemDefinitionId, out ItemDefinitionId item)
                || !m_content.Items.ContainsKey(item))
            {
                return row.ItemDefinitionId;
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

        long[] reportRows = ReportRows(operation);

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
        // character sent could cause.
        if (result.Status != InventoryStatus.Committed)
        {
            Refuse(character, CommandRejectionReason.InvalidTarget);
            return;
        }

        uint prior = character.Inventory.Revision;
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
                Restore(character.Player, m_content.Items[operation.Item].Effect!);
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
            default:
                throw NotAnItemAction(operation);
        }

        Finish(character);
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
        player.Weapon = EquipmentIn(character.Inventory, EquipmentSlot.Weapon);
        player.SetWornWeapon(ItemIn(character.Inventory, EquipmentSlot.Weapon));
        player.Armor = EquipmentIn(character.Inventory, EquipmentSlot.Armor);
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

    // A use restores what its item gives once its commit returns, capped at the maximums (Gameplay Systems §11.2). A
    // character that died meanwhile gets nothing back: the unit is spent either way.
    private void Restore(PlayerEntity player, ItemEffect effect)
    {
        if (player.IsDead)
        {
            return;
        }

        player.CurrentHealth = Math.Min(player.MaxHealth, player.CurrentHealth + effect.Health);
        player.CurrentSpirit = Math.Min(player.MaxSpirit, player.CurrentSpirit + effect.Spirit);
        m_sender.SendHealth(player);
    }

    // The statistics follow the slots as committed (Gameplay Systems §2); the owner hears of new maximums as of any
    // other change of its HP or SP.
    private void Wear(CharacterSession character)
    {
        PlayerEntity player = character.Player;
        int maxHealth = player.MaxHealth;
        int maxSpirit = player.MaxSpirit;
        player.Weapon = EquipmentIn(character.Inventory, EquipmentSlot.Weapon);
        player.SetWornWeapon(ItemIn(character.Inventory, EquipmentSlot.Weapon));
        player.Armor = EquipmentIn(character.Inventory, EquipmentSlot.Armor);
        m_stats.Recalculate(player, m_content.Jobs[player.Job]);
        if (player.MaxHealth != maxHealth || player.MaxSpirit != maxSpirit)
        {
            m_sender.SendHealth(player);
        }
    }

    private static ItemDefinitionId? ItemIn(CharacterInventory inventory, EquipmentSlot slot)
    {
        return inventory.TryGetRow(inventory.WornIn(slot), out InventoryEntry row) ? row.Item : null;
    }

    private ItemEquipment? EquipmentIn(CharacterInventory inventory, EquipmentSlot slot)
    {
        return inventory.TryGetRow(inventory.WornIn(slot), out InventoryEntry row)
            ? m_content.Items[row.Item].Equipment
            : null;
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
