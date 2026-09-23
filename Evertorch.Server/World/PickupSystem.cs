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

    private static readonly Action<ILogger, Guid, long, Exception?> LogUnsettled =
        LoggerMessage.Define<Guid, long>(
            LogLevel.Warning,
            new EventId(4007, "PickupUnsettled"),
            "The commit of drop {Drop} for character {Character} gave no answer; the drop stays reserved until the "
            + "ledger says what happened.");

    private readonly SessionRegistry m_sessions;
    private readonly PersistenceWorker m_persistence;
    private readonly MessageSender m_sender;
    private readonly CharacterLifetime m_lifetime;
    private readonly IReadOnlyDictionary<ItemDefinitionId, ItemDefinition> m_items;
    private readonly TimeProvider m_time;
    private readonly ILogger<PickupSystem> m_logger;
    private readonly float m_reach;
    private readonly uint m_priorityTicks;
    private readonly List<CharacterSession> m_unsettled = new();

    public PickupSystem(
        SessionRegistry sessions,
        PersistenceWorker persistence,
        MessageSender sender,
        CharacterLifetime lifetime,
        ServerContent content,
        TimeProvider time,
        IOptions<WorldOptions> worldOptions,
        IOptions<SimulationOptions> simulation,
        ILogger<PickupSystem> logger)
    {
        m_sessions = sessions;
        m_persistence = persistence;
        m_sender = sender;
        m_lifetime = lifetime;
        m_items = content.Items;
        m_time = time;
        m_logger = logger;
        m_reach = worldOptions.Value.PickupRange + worldOptions.Value.AttackRangeTolerance;
        m_priorityTicks = (uint)((long)LootPriorityMs * simulation.Value.TickRate / MillisecondsPerSecond);
    }

    public TickPhase Phase => TickPhase.SchedulePersistence;

    /// <summary>
    ///     Asks the ledger again about every commit whose answer was lost, once the database takes work again.
    /// </summary>
    public void Execute(in TickContext context)
    {
        for (int index = m_unsettled.Count - 1; index >= 0; index--)
        {
            if (TryQueueLookup(m_unsettled[index]))
            {
                m_unsettled.RemoveAt(index);
            }
        }
    }

    /// <summary>
    ///     A pickup finished, one way or the other, and its character may now log out or leave.
    /// </summary>
    public event Action<CharacterSession>? Settled;

    /// <summary>
    ///     Checks a pickup in the order of Gameplay Systems §11 and, when it passes, reserves the drop and queues its
    ///     commit. The caller has already refused a dead or leaving character.
    /// </summary>
    public CommandRejectionReason TryStart(ClientSession session, EntityId target, uint commandSequence, uint tick)
    {
        CharacterSession character = session.Character!;
        // One pickup at a time per character keeps its inventory changes in commit order.
        if (character.Pickup != null)
        {
            return CommandRejectionReason.Busy;
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
        var pickup = new PendingPickup(drop, commandSequence);
        var job = new PersistenceJob<PickupResult>(
            "pickup",
            session.Connection,
            character.Character.Value,
            (store, cancellation) => store.CommitPickupAsync(commit, cancellation),
            (outcome, result) => CompleteCommit(character, outcome, result));
        if (!m_persistence.TryEnqueue(job))
        {
            return CommandRejectionReason.ServiceUnavailable;
        }

        drop.ReservedBy = character.Character;
        character.Pickup = pickup;
        return CommandRejectionReason.None;
    }

    private static float HorizontalDistance(WorldPosition a, WorldPosition b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private void CompleteCommit(CharacterSession character, PersistenceOutcome outcome, PickupResult result)
    {
        if (outcome == PersistenceOutcome.Succeeded)
        {
            Settle(character, result);
            return;
        }

        // The commit may have happened; only the ledger can say.
        LogUnsettled(m_logger, character.Pickup!.Drop.DropId, character.Character.Value, null);
        if (!TryQueueLookup(character))
        {
            m_unsettled.Add(character);
        }
    }

    private bool TryQueueLookup(CharacterSession character)
    {
        Guid dropId = character.Pickup!.Drop.DropId;
        long id = character.Character.Value;
        var lookup = new PersistenceJob<PickupResult?>(
            "pickup lookup",
            character.Connection?.Connection ?? default,
            id,
            (store, cancellation) => store.FindPickupAsync(dropId, id, cancellation),
            (outcome, found) => CompleteLookup(character, outcome, found));
        return m_persistence.TryEnqueue(lookup);
    }

    private void CompleteLookup(CharacterSession character, PersistenceOutcome outcome, PickupResult? found)
    {
        if (outcome != PersistenceOutcome.Succeeded)
        {
            m_unsettled.Add(character);
            return;
        }

        if (found == null)
        {
            Release(character, CommandRejectionReason.ServiceUnavailable);
            return;
        }

        Settle(character, found);
    }

    private void Settle(CharacterSession character, PickupResult result)
    {
        switch (result.Status)
        {
            case PickupStatus.Committed:
                PickUp(character, result);
                break;
            case PickupStatus.InventoryFull:
                Release(character, CommandRejectionReason.InventoryFull);
                break;
            default:
                // Committed to someone else before this drop existed here; it is stale, and it goes.
                ItemDropEntity stale = character.Pickup!.Drop;
                character.Map.Remove(stale);
                Release(character, CommandRejectionReason.InvalidTarget);
                break;
        }
    }

    private void PickUp(CharacterSession character, PickupResult result)
    {
        ItemDropEntity drop = character.Pickup!.Drop;
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
        ClientSession? owner = character.Connection;
        if (result.Row == null)
        {
            character.Inventory.Revision = result.InventoryRevision;
            if (owner != null)
            {
                owner.NeedsInventorySnapshot = true;
            }
        }
        else
        {
            var row = new InventoryEntry(result.Row.Id, drop.Item, (uint)result.Row.Quantity);
            character.Inventory.Apply(result.InventoryRevision, row);
            if (owner != null && owner.State == SessionState.InWorld)
            {
                m_sender.Send(owner.Connection, new InventoryChanged(prior, result.InventoryRevision, new[] { row }));
            }
        }

        Finish(character);
    }

    private void Release(CharacterSession character, CommandRejectionReason reason)
    {
        PendingPickup pickup = character.Pickup!;
        pickup.Drop.ReservedBy = default;
        ClientSession? owner = character.Connection;
        if (owner != null && owner.State == SessionState.InWorld)
        {
            owner.RefusedCommands++;
            m_sender.Send(owner.Connection, new CommandRejected(pickup.CommandSequence, reason));
        }

        Finish(character);
    }

    private void Finish(CharacterSession character)
    {
        character.Pickup = null;
        Settled?.Invoke(character);
        m_lifetime.OnPickupSettled(character);
    }
}
}
