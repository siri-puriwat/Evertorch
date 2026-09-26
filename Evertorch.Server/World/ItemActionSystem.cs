using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Equips, unequips, and uses items (Gameplay Systems §11.1, §11.2; Persistence §5). The checks happen on the tick
///     thread; the inventory changes only through the commit, and the server's copy of it, the statistics, HP and SP,
///     and the owner learn of it once its result is back. A commit whose answer was lost is settled from the ledger
///     before the character may start another inventory operation.
/// </summary>
public sealed class ItemActionSystem : ITickPhase
{
    private const string EquipOperation = "equip";
    private const string UnequipOperation = "unequip";
    private const string EquipLookup = "equip lookup";
    private const string UnequipLookup = "unequip lookup";
    private const string ConsumeOperation = "consume";
    private const string ConsumeLookup = "consume lookup";
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

    private readonly PersistenceWorker m_persistence;
    private readonly MessageSender m_sender;
    private readonly CharacterLifetime m_lifetime;
    private readonly CharacterStats m_stats;
    private readonly ServerContent m_content;
    private readonly TimeProvider m_time;
    private readonly AuditLog m_audit;
    private readonly ILogger<ItemActionSystem> m_logger;
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
            _ => InboundEventKind.UseItem
        };
    }

    private static string CommitNameOf(InventoryOperation operation)
    {
        return operation.Kind switch
        {
            InventoryOperationKind.Equip => EquipOperation,
            InventoryOperationKind.Unequip => UnequipOperation,
            _ => ConsumeOperation
        };
    }

    private static string LookupNameOf(InventoryOperation operation)
    {
        return operation.Kind switch
        {
            InventoryOperationKind.Equip => EquipLookup,
            InventoryOperationKind.Unequip => UnequipLookup,
            _ => ConsumeLookup
        };
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
        if (operation.Kind == InventoryOperationKind.Consume)
        {
            Restore(character.Player, m_content.Items[operation.Item].Effect!);
        }
        else
        {
            Wear(character);
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
        character.Operation = null;
        Settled?.Invoke(character);
        m_lifetime.OnOperationSettled(character);
    }
}
}
