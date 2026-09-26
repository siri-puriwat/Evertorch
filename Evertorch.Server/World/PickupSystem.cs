using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Picks up item drops (Gameplay Systems §11, Persistence §5). The checks and the reservation happen on the tick
///     thread; the item reaches the inventory only through the commit, and the world removes the drop and tells
///     anyone only once the commit's result is back. A commit whose result was lost is settled from the ledger
///     before the reservation is released, so a drop is never both left in the world and in an inventory.
/// </summary>
public sealed class PickupSystem : ITickPhase
{
    /// <summary>
    ///     Rows a character's inventory holds at most (Persistence §4).
    /// </summary>
    public const int MaxInventoryRows = 100;

    /// <summary>
    ///     How long a drop belongs only to the character whose hit killed the monster (drops research note §3).
    /// </summary>
    public const int LootPriorityMs = 3000;

    private const int MillisecondsPerSecond = 1000;

    // A lookup that failed for a reason other than an outage is asked again only after this long, not on every tick
    // with an error each time (Persistence §9).
    private const int FailedLookupDelayMs = 1000;

    private static readonly Action<ILogger, Guid, long, long, Exception?> LogUnsettled =
        LoggerMessage.Define<Guid, long, long>(
            LogLevel.Warning,
            new EventId(4008, "PickupUnsettled"),
            "The commit of drop {OperationId} for character {Character} on connection {Connection} gave no answer; the "
            + "drop stays reserved until the ledger says what happened.");

    private readonly SessionRegistry m_sessions;
    private readonly PersistenceWorker m_persistence;
    private readonly MessageSender m_sender;
    private readonly CharacterLifetime m_lifetime;
    private readonly IReadOnlyDictionary<ItemDefinitionId, ItemDefinition> m_items;
    private readonly TimeProvider m_time;
    private readonly AuditLog m_audit;
    private readonly ILogger<PickupSystem> m_logger;
    private readonly float m_reach;
    private readonly uint m_priorityTicks;
    private readonly uint m_failedLookupDelayTicks;
    private readonly List<(CharacterSession Character, uint NotBefore)> m_unsettled = new();
    private uint m_tick;

    public PickupSystem(
        SessionRegistry sessions,
        PersistenceWorker persistence,
        MessageSender sender,
        CharacterLifetime lifetime,
        ServerContent content,
        TimeProvider time,
        IOptions<WorldOptions> worldOptions,
        IOptions<SimulationOptions> simulation,
        AuditLog audit,
        ILogger<PickupSystem> logger)
    {
        m_sessions = sessions;
        m_persistence = persistence;
        m_sender = sender;
        m_lifetime = lifetime;
        m_items = content.Items;
        m_time = time;
        m_audit = audit;
        m_logger = logger;
        m_reach = worldOptions.Value.PickupRange + worldOptions.Value.AttackRangeTolerance;
        m_priorityTicks = (uint)((long)LootPriorityMs * simulation.Value.TickRate / MillisecondsPerSecond);
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
    ///     A pickup finished, one way or the other, and its character may now log out, leave, or cross.
    /// </summary>
    public event Action<CharacterSession>? Settled;

    /// <summary>
    ///     Checks a pickup in the order of Gameplay Systems §11 and, when it passes, reserves the drop and queues its
    ///     commit. The caller has already refused a dead or leaving character.
    /// </summary>
    public CommandRejectionReason TryStart(ClientSession session, EntityId target, uint commandSequence, uint tick)
    {
        CharacterSession character = session.Character!;
        // One inventory operation at a time per character keeps its inventory changes in commit order. Reason 7 keeps
        // its meaning for a pickup's own business; another operation in flight is reason 9 (Network Protocol §11).
        if (character.Operation != null)
        {
            return character.Operation.Kind == InventoryOperationKind.Pickup
                ? CommandRejectionReason.Busy
                : CommandRejectionReason.ItemActionInFlight;
        }

        if (!character.Map.TryGetEntity(target, out WorldEntity? entity)
            || entity is not ItemDropEntity drop
            || !session.KnownEntities.Contains(target)
            || !m_items.TryGetValue(drop.Item, out ItemDefinition? item))
        {
            return CommandRejectionReason.InvalidTarget;
        }

        if (drop.IsReserved)
        {
            return CommandRejectionReason.Busy;
        }

        if (drop.Priority != default
            && drop.Priority != character.Character
            && tick - drop.DroppedTick < m_priorityTicks)
        {
            return CommandRejectionReason.LootPriority;
        }

        if (HorizontalDistance(character.Player.Position, drop.Position) > m_reach)
        {
            return CommandRejectionReason.OutOfRange;
        }

        var commit = new PickupCommit(
            drop.DropId,
            character.Character.Value,
            drop.Item.Value,
            (int)drop.Amount,
            item!.StackLimit,
            MaxInventoryRows,
            m_time.GetUtcNow().UtcDateTime);
        var pickup = InventoryOperation.ForPickup(drop, commandSequence);
        var job = new PersistenceJob<InventoryResult>(
            "pickup",
            session.Connection,
            character.Character.Value,
            (store, cancellation) => store.CommitPickupAsync(commit, cancellation),
            (outcome, result) => CompleteCommit(character, outcome, result),
            drop.DropId.ToString());
        if (!m_persistence.TryEnqueue(job))
        {
            return CommandRejectionReason.ServiceUnavailable;
        }

        drop.ReservedBy = character.Character;
        character.Operation = pickup;
        return CommandRejectionReason.None;
    }

    private static float HorizontalDistance(WorldPosition a, WorldPosition b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private void CompleteCommit(CharacterSession character, PersistenceOutcome outcome, InventoryResult result)
    {
        if (outcome == PersistenceOutcome.Succeeded)
        {
            Settle(character, result);
            return;
        }

        // The commit may have happened; only the ledger can say.
        LogUnsettled(
            m_logger,
            character.Operation!.OperationId,
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
        Guid dropId = character.Operation!.OperationId;
        long id = character.Character.Value;
        var lookup = new PersistenceJob<InventoryResult?>(
            "pickup lookup",
            character.Connection?.Connection ?? default,
            id,
            (store, cancellation) => store.FindOperationAsync(dropId, id, Array.Empty<long>(), cancellation),
            (outcome, found) => CompleteLookup(character, outcome, found),
            dropId.ToString());
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
            Release(character, CommandRejectionReason.ServiceUnavailable);
            return;
        }

        Settle(character, found);
    }

    private void Settle(CharacterSession character, InventoryResult result)
    {
        switch (result.Status)
        {
            case InventoryStatus.Committed:
                PickUp(character, result);
                break;
            case InventoryStatus.InventoryFull:
                Release(character, CommandRejectionReason.InventoryFull);
                break;
            default:
                // Committed to someone else before this drop existed here; it is stale, and it goes.
                ItemDropEntity stale = character.Operation!.Drop!;
                character.Map.Remove(stale);
                Release(character, CommandRejectionReason.InvalidTarget);
                break;
        }
    }

    private void PickUp(CharacterSession character, InventoryResult result)
    {
        ItemDropEntity drop = character.Operation!.Drop!;
        MapInstance map = character.Map;
        PlayerEntity picker = character.Player;
        foreach (ClientSession session in m_sessions.Sessions)
        {
            if (session.State != SessionState.InWorld || session.Map != map || !session.KnownEntities.Remove(drop.Id))
            {
                continue;
            }

            EntityId recipient = session.Knows(picker.Id) ? picker.Id : default;
            m_sender.Send(session.Connection, new ItemPickedUp(drop.Id, recipient, drop.Item, drop.Amount));
            m_sender.Send(session.Connection, new EntityDespawn(drop.Id, DespawnReason.PickedUp));
        }

        map.Remove(drop);
        uint prior = character.Inventory.Revision;
        InventoryEntry[] rows = character.Inventory.Apply(result);
        m_sender.SendInventoryChange(character, prior, rows);
        Finish(character);
    }

    private void Release(CharacterSession character, CommandRejectionReason reason)
    {
        InventoryOperation pickup = character.Operation!;
        pickup.Drop!.ReservedBy = default;
        ClientSession? owner = character.Connection;
        if (owner != null && owner.State == SessionState.InWorld)
        {
            owner.RefusedCommands++;
            m_audit.CommandRefused(owner, InboundEventKind.Pickup, reason);
            m_sender.Send(owner.Connection, new CommandRejected(pickup.CommandSequence, reason));
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
