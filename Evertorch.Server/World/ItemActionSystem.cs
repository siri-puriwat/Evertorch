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
///     Equips, unequips, uses, buys, and sells items, and turns in quests (Gameplay Systems §2.2, §6.1, §11.1–§11.3;
///     Persistence §5). The checks happen on the tick thread; the inventory and the coins change only through the
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
    private const int MillisecondsPerSecond = 1000;

    // A lookup that failed for a reason other than an outage is asked again only after this long, not on every tick
    // with an error each time (Persistence §9).
    private const int FailedLookupDelayMs = 1000;

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
    private readonly float m_npcReach;
    private readonly uint m_failedLookupDelayTicks;
    private readonly List<(CharacterSession Character, uint NotBefore)> m_unsettled = new();
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
        m_npcReach = NpcInteraction.Range + world.Value.AttackRangeTolerance;
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
    ///     item fills, and a row the slot held comes out of it. The caller has already refused a dead or leaving
    ///     character.
    /// </summary>
    public CommandRejectionReason TryEquip(ClientSession session, long inventoryItem, uint commandSequence)
    {
        CharacterSession character = session.Character!;
        // First: only a settled inventory answers for the database, and one operation at a time keeps the changes in
        // commit order (Persistence §7).
        if (character.Operation != null)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        if (!character.Inventory.TryGetRow(inventoryItem, out InventoryEntry row))
        {
            return CommandRejectionReason.InvalidTarget;
        }

        EquipmentSlot slot = m_content.Items[row.Item].Slot;
        long worn = character.Inventory.WornIn(slot);
        if (slot == EquipmentSlot.None || worn == inventoryItem)
        {
            return CommandRejectionReason.NotAllowedNow;
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
        if (character.Operation != null)
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
        if (character.Operation != null)
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
        if (character.Operation != null)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        CommandRejectionReason reach = NpcReach.Check(session, npc, m_npcReach, out NpcEntity? trader);
        if (reach != CommandRejectionReason.None)
        {
            return reach;
        }

        ShopEntry? stock = trader!.Definition.Shop.FirstOrDefault(entry => entry.Item == item);
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
        if (character.Operation != null)
        {
            return CommandRejectionReason.ItemActionInFlight;
        }

        CommandRejectionReason reach = NpcReach.Check(session, npc, m_npcReach, out NpcEntity? trader);
        if (reach != CommandRejectionReason.None)
        {
            return reach;
        }

        if (!trader!.Definition.HasShop || !character.Inventory.TryGetRow(inventoryItem, out InventoryEntry row))
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
    ///     commit, which carries the level and experience the reward leaves. The caller has already refused a dead or
    ///     leaving character.
    /// </summary>
    public CommandRejectionReason TryCompleteQuest(
        ClientSession session,
        EntityId npc,
        QuestDefinitionId quest,
        uint commandSequence)
    {
        CharacterSession character = session.Character!;
        if (character.Operation != null)
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
        var operation = new InventoryOperation(
            InventoryOperationKind.QuestReward,
            commandSequence,
            Guid.NewGuid(),
            coins: definition.Currency,
            quest: quest);
        var commit = new QuestRewardCommit(
            operation.OperationId,
            character.Character.Value,
            quest.Value,
            entry.Progress,
            definition.Count,
            definition.Currency,
            carried.Level,
            carried.Experience,
            m_time.GetUtcNow().UtcDateTime);
        return TryCommit(
            session,
            operation,
            (store, cancellation) => store.CommitQuestRewardAsync(commit, cancellation));
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
            _ => throw NotAnItemAction(operation)
        };
    }

    // A pickup is the pickup system's; an operation of any other kind reaching here is a server bug.
    private static InvalidOperationException NotAnItemAction(InventoryOperation operation)
    {
        return new InvalidOperationException($"A {operation.Kind} operation is not an item action.");
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

        // The commit may have happened; only the ledger can say.
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
        long[] reportRows = ReportRows(operation);
        var lookup = new PersistenceJob<InventoryResult?>(
            LookupNameOf(operation),
            character.Connection?.Connection ?? default,
            id,
            (store, cancellation) => store.FindOperationAsync(operation.OperationId, id, reportRows, cancellation),
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
        m_sender.SendInventoryChange(character, prior, rows);
        InventoryOperation operation = character.Operation!;
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
            default:
                throw NotAnItemAction(operation);
        }

        Finish(character);
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
        player.Armor = EquipmentIn(character.Inventory, EquipmentSlot.Armor);
        m_stats.Recalculate(player, m_content.Jobs[player.Job]);
        if (player.MaxHealth != maxHealth || player.MaxSpirit != maxSpirit)
        {
            m_sender.SendHealth(player);
        }
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
        bool wasTurnIn = character.Operation?.Kind == InventoryOperationKind.QuestReward;
        character.Operation = null;

        // Checkpoints taken while a turn-in was in flight left the level and experience to it (Persistence §6), the
        // reward's own level-up among them; once it is over, what the character holds is saved at once. A logout or
        // a removal waiting for it writes its own final checkpoint.
        if (wasTurnIn && !character.IsLoggingOut && !character.IsExpelled && !character.IsRemovalDeferred)
        {
            m_lifetime.QueueCheckpoint(character);
        }

        Settled?.Invoke(character);
        m_lifetime.OnOperationSettled(character);
    }
}
}
