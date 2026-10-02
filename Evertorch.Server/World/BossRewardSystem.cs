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
///     Takes a boss's prize into its most valuable player's bag (Gameplay Systems §11; Persistence §5): the first
///     inventory operation the server starts without a command. Each character's grants go one at a time, each only
///     when no other inventory operation of the character is in flight, and wait out an outage. A full bag, or a
///     commit the ledger never saw that no outage cut short, puts the prize at the player's feet instead, under the
///     grant's operation ID, so the ledger forbids it twice; the player alone may pick it up for
///     <see cref="PrizePriorityMs" />. A commit an outage cut short and the ledger never saw is made again, under the
///     same ID.
/// </summary>
public sealed class BossRewardSystem : ITickPhase
{
    /// <summary>
    ///     How long a prize at the player's feet belongs to that player alone (bosses and dungeons research note).
    /// </summary>
    public const int PrizePriorityMs = 10_000;

    private const int MillisecondsPerSecond = 1000;
    private const string Bag = "bag";
    private const string Feet = "feet";

    // A lookup that failed for a reason other than an outage is asked again only after this long (Persistence §9).
    private const int FailedLookupDelayMs = 1000;

    private static readonly Action<ILogger, string, int, long, Guid, Exception?> LogGranted =
        LoggerMessage.Define<string, int, long, Guid>(
            LogLevel.Information,
            new EventId(1025, "BossRewardGranted"),
            "The boss's prize, {Item} x {Amount}, reached character {Character}'s bag (operation {Operation}).");

    private static readonly Action<ILogger, string, int, long, Guid, Exception?> LogDropped =
        LoggerMessage.Define<string, int, long, Guid>(
            LogLevel.Information,
            new EventId(1026, "BossRewardDropped"),
            "The boss's prize, {Item} x {Amount}, lies at character {Character}'s feet (operation {Operation}).");

    private static readonly Action<ILogger, Guid, long, Exception?> LogUnsettled =
        LoggerMessage.Define<Guid, long>(
            LogLevel.Warning,
            new EventId(4012, "BossRewardUnsettled"),
            "The commit of prize {Operation} for character {Character} gave no answer; it waits until the ledger says "
            + "what happened.");

    private readonly SessionRegistry m_sessions;
    private readonly PersistenceWorker m_persistence;
    private readonly MessageSender m_sender;
    private readonly CharacterLifetime m_lifetime;
    private readonly WorldSimulation m_world;
    private readonly IReadOnlyDictionary<ItemDefinitionId, ItemDefinition> m_items;
    private readonly TimeProvider m_time;
    private readonly ServerInstruments m_instruments;
    private readonly ILogger<BossRewardSystem> m_logger;
    private readonly int m_tickRate;
    private readonly int m_lifetimeMs;
    private readonly uint m_failedLookupDelayTicks;
    private readonly List<(CharacterSession Character, uint NotBefore)> m_unsettled = new();
    private readonly List<CharacterSession> m_waiting = new();
    private readonly HashSet<CharacterSession> m_cutShort = new();
    private uint m_tick;

    public BossRewardSystem(
        SessionRegistry sessions,
        PersistenceWorker persistence,
        MessageSender sender,
        CharacterLifetime lifetime,
        WorldSimulation world,
        ServerContent content,
        TimeProvider time,
        ServerInstruments instruments,
        IOptions<WorldOptions> worldOptions,
        IOptions<SimulationOptions> simulation,
        ILogger<BossRewardSystem> logger)
    {
        m_sessions = sessions;
        m_persistence = persistence;
        m_sender = sender;
        m_lifetime = lifetime;
        m_world = world;
        m_items = content.Items;
        m_time = time;
        m_instruments = instruments;
        m_logger = logger;
        m_tickRate = simulation.Value.TickRate;
        m_lifetimeMs = worldOptions.Value.ItemDropLifetimeMs;
        m_failedLookupDelayTicks = (uint)((long)FailedLookupDelayMs * m_tickRate / MillisecondsPerSecond);
    }

    public TickPhase Phase => TickPhase.SchedulePersistence;

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

        // A grant starts only when the character's inventory is free; in an outage it waits, never dropping a prize
        // for a passing condition.
        m_waiting.Clear();
        foreach (CharacterSession character in m_sessions.Characters)
        {
            if (character.Operation == null && character.PendingGrants.Count > 0)
            {
                m_waiting.Add(character);
            }
        }

        foreach (CharacterSession character in m_waiting)
        {
            TryStart(character);
        }
    }

    /// <summary>
    ///     A grant finished, one way or the other; the character may now log out, leave, or cross once nothing else
    ///     of its inventory is in flight.
    /// </summary>
    public event Action<CharacterSession>? Settled;

    private void TryStart(CharacterSession character)
    {
        BossGrant grant = character.PendingGrants.Peek();
        var operation = new InventoryOperation(
            InventoryOperationKind.BossReward,
            0,
            grant.OperationId,
            item: grant.Item,
            quantity: grant.Amount);
        var commit = new GrantCommit(
            operation.OperationId,
            character.Character.Value,
            grant.Item.Value,
            grant.Amount,
            m_items[grant.Item].StackLimit,
            PickupSystem.MaxInventoryRows,
            m_time.GetUtcNow().UtcDateTime);
        var job = new PersistenceJob<InventoryResult>(
            "boss reward",
            character.Connection?.Connection ?? default,
            character.Character.Value,
            (store, cancellation) => store.CommitGrantAsync(commit, cancellation),
            (outcome, result) => CompleteCommit(character, outcome, result),
            operation.OperationId.ToString());
        if (m_persistence.TryEnqueue(job))
        {
            character.Operation = operation;
        }
    }

    private void CompleteCommit(CharacterSession character, PersistenceOutcome outcome, InventoryResult result)
    {
        if (outcome == PersistenceOutcome.Succeeded)
        {
            Settle(character, result);
            return;
        }

        // The commit may have happened; only the ledger can say.
        LogUnsettled(m_logger, character.Operation!.OperationId, character.Character.Value, null);
        if (outcome == PersistenceOutcome.Unavailable)
        {
            m_cutShort.Add(character);
        }

        if (!TryQueueLookup(character))
        {
            m_unsettled.Add((character, m_tick));
        }
    }

    private bool TryQueueLookup(CharacterSession character)
    {
        Guid operationId = character.Operation!.OperationId;
        long id = character.Character.Value;
        var lookup = new PersistenceJob<InventoryResult?>(
            "boss reward lookup",
            character.Connection?.Connection ?? default,
            id,
            (store, cancellation) => store.FindOperationAsync(operationId, id, Array.Empty<long>(), cancellation),
            (outcome, found) => CompleteLookup(character, outcome, found),
            operationId.ToString());
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

        // A rare item never lies on the ground through a passing outage: a commit an outage cut short is made again.
        // It keeps its operation ID: a commit that timed out may still land after the lookup, and the store, which
        // locks the character before it reads the ledger, then answers the second with the first.
        bool wasCutShort = m_cutShort.Remove(character);
        if (found == null && wasCutShort)
        {
            character.Operation = null;
            return;
        }

        if (found == null)
        {
            PlaceAtFeet(character);
            return;
        }

        Settle(character, found);
    }

    private void Settle(CharacterSession character, InventoryResult result)
    {
        switch (result.Status)
        {
            case InventoryStatus.Committed:
                IntoTheBag(character, result);
                break;
            case InventoryStatus.InventoryFull:
                PlaceAtFeet(character);
                break;
            default:
                // Another's operation holds this ID, so the prize already lies somewhere: it is not made twice.
                Finish(character);
                break;
        }
    }

    private void IntoTheBag(CharacterSession character, InventoryResult result)
    {
        InventoryOperation operation = character.Operation!;
        uint prior = character.Inventory.Revision;
        InventoryEntry[] rows = character.Inventory.Apply(result);
        m_sender.SendInventoryChange(character, prior, rows);
        LogGranted(
            m_logger,
            operation.Item.Value,
            operation.Quantity,
            character.Character.Value,
            operation.OperationId,
            null);
        m_instruments.RecordBossReward(Bag);
        Finish(character);
    }

    // The prize's drop takes the grant's operation ID as its own, so the ledger's unique index lets it into one bag
    // only, and the most valuable player alone may take it for a while (Gameplay Systems §11).
    private void PlaceAtFeet(CharacterSession character)
    {
        InventoryOperation operation = character.Operation!;
        // Only a completion places a prize, and completions run first in a tick, before Execute takes its number.
        uint tick = m_tick + 1;
        long now = (long)(tick - 1) * MillisecondsPerSecond / m_tickRate;
        m_world.SpawnItemDrop(
            character.Map,
            operation.Item,
            (uint)operation.Quantity,
            character.Player.Position,
            tick,
            now + m_lifetimeMs,
            character.Character,
            operation.OperationId,
            PrizePriorityMs);
        LogDropped(
            m_logger,
            operation.Item.Value,
            operation.Quantity,
            character.Character.Value,
            operation.OperationId,
            null);
        m_instruments.RecordBossReward(Feet);
        Finish(character);
    }

    private void Finish(CharacterSession character)
    {
        character.PendingGrants.Dequeue();
        character.Operation = null;
        Settled?.Invoke(character);
        m_lifetime.OnOperationSettled(character);
    }
}
}
