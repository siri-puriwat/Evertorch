using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Face-to-face trades (Gameplay Systems §16): the requests that wait for an answer, in memory for
///     <see cref="RequestLifetimeMs" />, and the trades they open, each holding both traders still with their bags
///     frozen until it ends. Once a tick, after the boss's prizes, every request and trade whose traders no longer stand
///     together ends. Tick thread only.
/// </summary>
public sealed class TradeSystem : ITickPhase
{
    public const int RequestLifetimeMs = 30000;
    private const int MillisecondsPerSecond = 1000;
    private const string Cancelled = "cancelled";
    private const string Parted = "parted";
    private const int FailedLookupDelayMs = 1000;
    private const string Refused = "refused";
    private const string Unsaved = "unsaved";


    private static readonly Action<ILogger, long, long, string, Exception?> LogEnded =
        LoggerMessage.Define<long, long, string>(
            LogLevel.Information,
            new EventId(1028, "TradeEnded"),
            "The trade between characters {First} and {Second} ended: {Reason}.");

    private static readonly Action<ILogger, long, long, Guid, Exception?> LogCompleted =
        LoggerMessage.Define<long, long, Guid>(
            LogLevel.Information,
            new EventId(1027, "TradeCompleted"),
            "Characters {First} and {Second} traded (operation {Operation}).");

    private static readonly Action<ILogger, Guid, long, long, Exception?> LogUnsettled =
        LoggerMessage.Define<Guid, long, long>(
            LogLevel.Warning,
            new EventId(4013, "TradeUnsettled"),
            "The commit of trade {Operation} between characters {First} and {Second} gave no answer; both wait until "
            + "the database says what happened.");

    private readonly SessionRegistry m_sessions;
    private readonly PersistenceWorker m_persistence;
    private readonly CharacterLifetime m_lifetime;
    private readonly IReadOnlyDictionary<ItemDefinitionId, ItemDefinition> m_items;
    private readonly TimeProvider m_time;
    private readonly uint m_failedLookupDelayTicks;
    private readonly List<(OpenTrade Trade, uint NotBefore)> m_unsettled = new();
    private readonly List<OpenTrade> m_ready = new();

    private readonly MessageSender m_sender;
    private readonly CombatSystem m_combat;
    private readonly ServerInstruments m_instruments;
    private readonly ILogger<TradeSystem> m_logger;
    private readonly float m_reach;
    private readonly uint m_requestTicks;
    private readonly Dictionary<long, PendingTrade> m_byRequester = new();
    private readonly Dictionary<long, PendingTrade> m_byPartner = new();
    private readonly Dictionary<long, OpenTrade> m_trades = new();
    private readonly List<PendingTrade> m_endedRequests = new();
    private readonly List<OpenTrade> m_endedTrades = new();
    private uint m_tick;

    public TradeSystem(
        SessionRegistry sessions,
        MessageSender sender,
        CombatSystem combat,
        PersistenceWorker persistence,
        CharacterLifetime lifetime,
        ServerContent content,
        TimeProvider time,
        ServerInstruments instruments,
        IOptions<WorldOptions> world,
        IOptions<SimulationOptions> simulation,
        ILogger<TradeSystem> logger)
    {
        m_sessions = sessions;
        m_sender = sender;
        m_combat = combat;
        m_persistence = persistence;
        m_lifetime = lifetime;
        m_items = content.Items;
        m_time = time;
        m_instruments = instruments;
        m_logger = logger;
        m_reach = NpcInteraction.Range + world.Value.AttackRangeTolerance;
        m_requestTicks = (uint)((long)RequestLifetimeMs * simulation.Value.TickRate / MillisecondsPerSecond);
        m_failedLookupDelayTicks =
            (uint)((long)FailedLookupDelayMs * simulation.Value.TickRate / MillisecondsPerSecond);
    }


    public int PendingRequests => m_byRequester.Count;

    public int OpenTrades => m_trades.Count / 2;

    public TickPhase Phase => TickPhase.SchedulePersistence;

    /// <summary>
    ///     Ends each request whose time is up, telling its requester, and each request and trade whose two characters
    ///     no longer stand together: both in the world with the connection they had at its opening, alive, not logging
    ///     out or removed, on one map instance (Gameplay Systems §16).
    /// </summary>
    public void Execute(in TickContext context)
    {
        m_tick = context.Tick;
        for (int index = m_unsettled.Count - 1; index >= 0; index--)
        {
            (OpenTrade waiting, uint notBefore) = m_unsettled[index];
            if (unchecked((int)(m_tick - notBefore)) >= 0 && TryQueueLookup(waiting))
            {
                m_unsettled.RemoveAt(index);
            }
        }

        m_endedRequests.Clear();
        foreach (PendingTrade request in m_byRequester.Values)
        {
            if (unchecked((int)(m_tick - request.ExpiresTick)) >= 0)
            {
                m_endedRequests.Add(request);
                Tell(request.Requester, new TradeEvent(TradeEventKind.Expired, request.Partner.Name));
            }
            else if (!AreTogether(request.Requester, request.Partner))
            {
                m_endedRequests.Add(request);
            }
        }

        foreach (PendingTrade request in m_endedRequests)
        {
            Forget(request);
        }

        m_endedTrades.Clear();
        foreach (OpenTrade trade in m_trades.Values)
        {
            if (!trade.IsCommitting && !m_endedTrades.Contains(trade) && !AreTogether(trade.First, trade.Second))
            {
                m_endedTrades.Add(trade);
            }
        }

        foreach (OpenTrade trade in m_endedTrades)
        {
            End(trade, Parted);
        }

        m_ready.Clear();
        foreach (OpenTrade trade in m_trades.Values)
        {
            if (!trade.IsCommitting
                && !m_ready.Contains(trade)
                && trade.First.Offer.IsConfirmed
                && trade.Second.Offer.IsConfirmed
                && !trade.First.Character.HasInventoryWork
                && !trade.Second.Character.HasInventoryWork)
            {
                m_ready.Add(trade);
            }
        }

        foreach (OpenTrade trade in m_ready)
        {
            StartCommit(trade);
        }
    }

    /// <summary>
    ///     A trade's commit finished, one way or the other, for this trader: it may now log out, leave, or cross once
    ///     nothing else of its inventory is in flight.
    /// </summary>
    public event Action<CharacterSession>? Settled;

    /// <summary>
    ///     Settling one trader threw: the connection's session is closed, as a fault inside its own boundary would close
    ///     it, while the other trader settles on.
    /// </summary>
    public event Action<ConnectionId, Exception>? SettleFaulted;

    /// <summary>
    ///     Whether the character has a trade open, which holds it still and freezes its bag (Gameplay Systems §16).
    /// </summary>
    public bool IsTrading(CharacterSession character)
    {
        return m_trades.ContainsKey(IdOf(character));
    }

    /// <summary>
    ///     Asks the reachable character <paramref name="name" /> to trade. The caller has refused a dead character and
    ///     one logging out.
    /// </summary>
    public CommandRejectionReason TryRequest(ClientSession session, string name)
    {
        CharacterSession requester = session.Character!;
        if (!m_sessions.TryGetReachable(name, out CharacterSession? partner))
        {
            return CommandRejectionReason.InvalidTarget;
        }

        if (ReferenceEquals(partner, requester))
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        CommandRejectionReason refusal = CheckPair(requester, partner!);
        if (refusal != CommandRejectionReason.None)
        {
            return refusal;
        }

        if (m_byPartner.TryGetValue(IdOf(partner!), out PendingTrade? held)
            && !ReferenceEquals(held.Requester.Character, requester))
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        if (m_byRequester.TryGetValue(IdOf(requester), out PendingTrade? earlier))
        {
            Forget(earlier);
        }

        var request = new PendingTrade(
            new Trader(requester, session),
            new Trader(partner!, partner!.Connection!),
            m_tick + m_requestTicks);
        m_byRequester.Add(IdOf(requester), request);
        m_byPartner.Add(IdOf(partner), request);
        Tell(request.Partner, new TradeEvent(TradeEventKind.Requested, request.Requester.Name));
        return CommandRejectionReason.None;
    }

    /// <summary>
    ///     Answers the request from <paramref name="requesterName" />. A decline tells the requester at once; an accept
    ///     checks everything again, the range included, and opens the trade for both.
    /// </summary>
    public CommandRejectionReason TryReply(ClientSession session, string requesterName, bool isAccepted)
    {
        CharacterSession partner = session.Character!;
        if (!m_byPartner.TryGetValue(IdOf(partner), out PendingTrade? request)
            || !string.Equals(request.Requester.Name, requesterName, StringComparison.OrdinalIgnoreCase))
        {
            return CommandRejectionReason.InvalidTarget;
        }

        if (!isAccepted)
        {
            Forget(request);
            Tell(request.Requester, new TradeEvent(TradeEventKind.Declined, request.Partner.Name));
            return CommandRejectionReason.None;
        }

        if (!AreTogether(request.Requester, request.Partner) || !ReferenceEquals(session, request.Partner.Connection))
        {
            Forget(request);
            return CommandRejectionReason.InvalidTarget;
        }

        CommandRejectionReason refusal = CheckPair(request.Requester.Character, partner);
        if (refusal == CommandRejectionReason.ItemActionInFlight)
        {
            return refusal;
        }

        Forget(request);
        if (refusal != CommandRejectionReason.None)
        {
            return refusal;
        }

        Open(request.Requester, request.Partner);
        return CommandRejectionReason.None;
    }

    /// <summary>
    ///     Sets how much of the sender's row <paramref name="inventoryItem" />, or of its coins for row 0, its open
    ///     trade offers; 0 takes it back.
    /// </summary>
    public CommandRejectionReason TryOffer(ClientSession session, long inventoryItem, uint quantity)
    {
        if (!TryGetOwnSide(session, out OpenTrade? trade, out TradeOffering? side) || side!.IsLocked)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        CharacterInventory inventory = session.Character!.Inventory;
        if (inventoryItem == 0)
        {
            if (quantity > inventory.Coins)
            {
                return CommandRejectionReason.NotAllowedNow;
            }

            side.Coins = quantity;
        }
        else
        {
            if (!inventory.TryGetRow(inventoryItem, out InventoryEntry row))
            {
                return CommandRejectionReason.InvalidTarget;
            }

            if (row.Slot != EquipmentSlot.None
                || quantity > row.Quantity
                || (quantity > 0 && !side.Offers(inventoryItem) && side.RowCount >= TradeSide.MaxEntries))
            {
                return CommandRejectionReason.NotAllowedNow;
            }

            side.Set(inventoryItem, quantity);
        }

        ShowSide(trade!, side);
        return CommandRejectionReason.None;
    }

    /// <summary>
    ///     Freezes the sender's own offer; there is no unlock.
    /// </summary>
    public CommandRejectionReason TryLock(ClientSession session)
    {
        if (!TryGetOwnSide(session, out OpenTrade? trade, out TradeOffering? side) || side!.IsLocked)
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        side.IsLocked = true;
        ShowSide(trade!, side);
        return CommandRejectionReason.None;
    }

    /// <summary>
    ///     Confirms the trade, only while both offers are locked and at least one offers something; once both confirm,
    ///     the next tick pass starts the commit.
    /// </summary>
    public CommandRejectionReason TryConfirm(ClientSession session)
    {
        if (!TryGetOwnSide(session, out OpenTrade? trade, out TradeOffering? side)
            || side!.IsConfirmed
            || !trade!.First.Offer.IsLocked
            || !trade.Second.Offer.IsLocked
            || (trade.First.Offer.IsEmpty && trade.Second.Offer.IsEmpty))
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        side.IsConfirmed = true;
        ShowSide(trade, side);
        return CommandRejectionReason.None;
    }

    /// <summary>
    ///     Ends the sender's open trade for both, or withdraws its own request, until the commit starts.
    /// </summary>
    public CommandRejectionReason TryCancel(ClientSession session)
    {
        CharacterSession character = session.Character!;
        if (m_trades.TryGetValue(IdOf(character), out OpenTrade? trade))
        {
            if (trade.IsCommitting)
            {
                return CommandRejectionReason.NotAllowedNow;
            }

            End(trade, Cancelled);
            return CommandRejectionReason.None;
        }

        if (m_byRequester.TryGetValue(IdOf(character), out PendingTrade? request))
        {
            Forget(request);
            return CommandRejectionReason.None;
        }

        return CommandRejectionReason.NotAllowedNow;
    }

    private void Open(Trader requester, Trader partner)
    {
        bool isRequesterFirst = IdOf(requester.Character) < IdOf(partner.Character);
        var trade = new OpenTrade(
            Guid.NewGuid(),
            isRequesterFirst ? requester : partner,
            isRequesterFirst ? partner : requester);
        foreach (Trader trader in new[] { trade.First, trade.Second })
        {
            m_trades.Add(IdOf(trader.Character), trade);
            PlayerEntity player = trader.Character.Player;
            player.IsTrading = true;
            player.Combat.IsAutoAttacking = false;
            m_combat.InterruptCast(player);
            ForgetAllOf(trader.Character);
        }

        Tell(trade.First, new TradeEvent(TradeEventKind.Opened, trade.Second.Name));
        Tell(trade.Second, new TradeEvent(TradeEventKind.Opened, trade.First.Name));
        ShowSide(trade, trade.First.Offer);
        ShowSide(trade, trade.Second.Offer);
    }

    // Ends an open trade: one that never committed tells both traders it was cancelled; one the commit refused has told
    // them why already. Either way both may move again.
    private void End(OpenTrade trade, string reason, bool isCancelled = true)
    {
        Close(trade);
        if (isCancelled)
        {
            Tell(trade.First, new TradeEvent(TradeEventKind.Cancelled, trade.Second.Name));
            Tell(trade.Second, new TradeEvent(TradeEventKind.Cancelled, trade.First.Name));
        }

        LogEnded(m_logger, IdOf(trade.First.Character), IdOf(trade.Second.Character), reason, null);
        m_instruments.RecordTradeEnded(reason);
    }

    // Both have confirmed and neither has inventory work: the offers are checked here first, then each trader gets its
    // own operation under the trade's ID and one job commits both (Gameplay Systems §16; Persistence §5).
    private void StartCommit(OpenTrade trade)
    {
        TradeCommit commit = CommitOf(trade);
        var settlement = new TradeSettlement(
            commit,
            HoldingsOf(trade.First.Character).Concat(HoldingsOf(trade.Second.Character)),
            trade.First.Character.Inventory.Coins,
            trade.Second.Character.Inventory.Coins);
        if (settlement.Status != TradeStatus.Committed)
        {
            Fail(trade, settlement.Status, settlement.RefusedCharacterId!.Value);
            return;
        }

        CharacterSession first = trade.First.Character;
        var job = new PersistenceJob<TradeResult>(
            "trade",
            first.Connection?.Connection ?? default,
            IdOf(first),
            (store, cancellation) => store.CommitTradeAsync(commit, cancellation),
            (outcome, result) => CompleteCommit(trade, outcome, result),
            trade.Id.ToString());
        if (!m_persistence.TryEnqueue(job))
        {
            // Nothing was asked of the database: the trade stays open, and both may confirm again.
            trade.First.Offer.IsConfirmed = false;
            trade.Second.Offer.IsConfirmed = false;
            Tell(trade.First, new TradeEvent(TradeEventKind.Unsaved, trade.Second.Name,
                CommandRejectionReason.ServiceUnavailable));
            Tell(trade.Second, new TradeEvent(TradeEventKind.Unsaved, trade.First.Name,
                CommandRejectionReason.ServiceUnavailable));
            ShowSide(trade, trade.First.Offer);
            ShowSide(trade, trade.Second.Offer);
            return;
        }

        trade.IsCommitting = true;
        first.Operation = new InventoryOperation(InventoryOperationKind.Trade, 0, trade.Id);
        trade.Second.Character.Operation = new InventoryOperation(InventoryOperationKind.Trade, 0, trade.Id);
    }

    private TradeCommit CommitOf(OpenTrade trade)
    {
        var limits = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Trader trader in new[] { trade.First, trade.Second })
        {
            foreach ((long row, uint _) in trader.Offer.Rows)
            {
                if (trader.Character.Inventory.TryGetRow(row, out InventoryEntry held))
                {
                    limits[held.Item.Value] = m_items[held.Item].StackLimit;
                }
            }
        }

        return new TradeCommit(
            trade.Id,
            OfferOf(trade.First),
            OfferOf(trade.Second),
            limits,
            PickupSystem.MaxInventoryRows,
            m_time.GetUtcNow().UtcDateTime);
    }

    private static TraderOffer OfferOf(Trader trader)
    {
        return new TraderOffer(
            IdOf(trader.Character),
            trader.Offer.Rows.Select(offered => new TradeLine(offered.Row, (int)offered.Quantity)).ToList(),
            trader.Offer.Coins);
    }

    private static IEnumerable<TradeSettlement.Holding> HoldingsOf(CharacterSession character)
    {
        return character.Inventory.Rows.Select(row => new TradeSettlement.Holding(
            row.InventoryItem,
            IdOf(character),
            row.Item.Value,
            (int)row.Quantity,
            row.Slot != EquipmentSlot.None));
    }

    private void CompleteCommit(OpenTrade trade, PersistenceOutcome outcome, TradeResult result)
    {
        if (outcome == PersistenceOutcome.Succeeded)
        {
            Settle(trade, result);
            return;
        }

        // The commit may have happened; only the trades table can say.
        LogUnsettled(m_logger, trade.Id, IdOf(trade.First.Character), IdOf(trade.Second.Character), null);
        if (!TryQueueLookup(trade))
        {
            m_unsettled.Add((trade, m_tick));
        }
    }

    private bool TryQueueLookup(OpenTrade trade)
    {
        Guid id = trade.Id;
        long first = IdOf(trade.First.Character);
        long second = IdOf(trade.Second.Character);
        var lookup = new PersistenceJob<TradeResult?>(
            "trade lookup",
            trade.First.Character.Connection?.Connection ?? default,
            first,
            (store, cancellation) => store.FindTradeAsync(id, first, second, cancellation),
            (outcome, found) => CompleteLookup(trade, outcome, found),
            id.ToString());
        return m_persistence.TryEnqueue(lookup);
    }

    private void CompleteLookup(OpenTrade trade, PersistenceOutcome outcome, TradeResult? found)
    {
        if (outcome != PersistenceOutcome.Succeeded)
        {
            uint notBefore = outcome == PersistenceOutcome.Failed ? m_tick + m_failedLookupDelayTicks : m_tick;
            m_unsettled.Add((trade, notBefore));
            return;
        }

        if (found != null)
        {
            Settle(trade, found);
            return;
        }

        // The trade never committed: it ends with nothing moved.
        Finish(trade, () =>
        {
            Tell(trade.First, new TradeEvent(TradeEventKind.Failed, trade.Second.Name,
                CommandRejectionReason.ServiceUnavailable));
            Tell(trade.Second, new TradeEvent(TradeEventKind.Failed, trade.First.Name,
                CommandRejectionReason.ServiceUnavailable));
            End(trade, Unsaved, false);
        });
    }

    private void Settle(OpenTrade trade, TradeResult result)
    {
        Finish(trade, () =>
        {
            if (result.Status != TradeStatus.Committed)
            {
                Fail(trade, result.Status, result.RefusedCharacterId!.Value);
                return;
            }

            SettleSide(trade.First, result.First, trade.Second.Name);
            SettleSide(trade.Second, result.Second, trade.First.Name);
            LogCompleted(m_logger, IdOf(trade.First.Character), IdOf(trade.Second.Character), trade.Id, null);
            m_instruments.RecordTradeCompleted(trade.First.Offer.Coins + trade.Second.Offer.Coins);
        });
    }

    // Each trader is settled inside a fault boundary of its own, so one that throws never leaves the other stuck; the
    // trade closes and both operations clear whatever happened (System Architecture §8).
    private void Finish(OpenTrade trade, Action settle)
    {
        try
        {
            settle();
        }
        finally
        {
            Close(trade);
            foreach (Trader trader in new[] { trade.First, trade.Second })
            {
                trader.Character.Operation = null;
                Settled?.Invoke(trader.Character);
                m_lifetime.OnOperationSettled(trader.Character);
            }
        }
    }

    private void SettleSide(Trader trader, TraderInventory inventory, string partnerName)
    {
        CharacterSession character = trader.Character;
        try
        {
            character.Inventory.Replace(inventory.InventoryRevision, inventory.Coins, inventory.Rows);

            // A whole inventory, never a change: a trade may change more rows than a change holds, and the client takes
            // a snapshot it did not ask for without a shop's line (Network Protocol §9).
            ClientSession? owner = character.Connection;
            if (owner != null && owner.State == SessionState.InWorld)
            {
                foreach (InventorySnapshot part in character.Inventory.CreateSnapshot())
                {
                    m_sender.Send(owner.Connection, part);
                }

                m_sender.Send(owner.Connection, new TradeEvent(TradeEventKind.Completed, partnerName));
            }
        }
        catch (Exception exception)
        {
            SettleFaulted?.Invoke(character.Connection?.Connection ?? default, exception);
        }
    }

    private void Fail(OpenTrade trade, TradeStatus status, long refusedCharacterId)
    {
        string named = refusedCharacterId == IdOf(trade.First.Character) ? trade.First.Name : trade.Second.Name;
        CommandRejectionReason reason = status switch
        {
            TradeStatus.InventoryFull => CommandRejectionReason.InventoryFull,
            TradeStatus.CoinCapReached => CommandRejectionReason.CoinCapReached,
            _ => CommandRejectionReason.NotAllowedNow
        };
        Tell(trade.First, new TradeEvent(TradeEventKind.Failed, named, reason));
        Tell(trade.Second, new TradeEvent(TradeEventKind.Failed, named, reason));
        End(trade, Refused, false);
    }

    private void Close(OpenTrade trade)
    {
        foreach (Trader trader in new[] { trade.First, trade.Second })
        {
            m_trades.Remove(IdOf(trader.Character));
            trader.Character.Player.IsTrading = false;
        }
    }

    // The side's owner sees it with its rows' IDs, the partner without them.
    private void ShowSide(OpenTrade trade, TradeOffering side)
    {
        Trader owner = side == trade.First.Offer ? trade.First : trade.Second;
        Trader partner = side == trade.First.Offer ? trade.Second : trade.First;
        Show(owner, side.ToMessage(owner.Character.Inventory, TradeSideOwner.Own));
        Show(partner, side.ToMessage(owner.Character.Inventory, TradeSideOwner.Partner));
    }

    private bool TryGetOwnSide(ClientSession session, out OpenTrade? trade, out TradeOffering? side)
    {
        side = null;
        if (!m_trades.TryGetValue(IdOf(session.Character!), out trade) || trade.IsCommitting)
        {
            return false;
        }

        side = ReferenceEquals(trade.First.Character, session.Character) ? trade.First.Offer : trade.Second.Offer;
        return true;
    }

    // Who may trade with whom, checked at the request and again at the reply: neither dead, logging out, or trading,
    // on one map instance and within reach, and neither with inventory work.
    private CommandRejectionReason CheckPair(CharacterSession requester, CharacterSession partner)
    {
        if (requester.Player.IsDead
            || requester.IsLoggingOut
            || IsTrading(requester)
            || partner.Player.IsDead
            || partner.IsLoggingOut
            || partner.IsExpelled
            || IsTrading(partner))
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        if (!ReferenceEquals(requester.Map, partner.Map) || Distance(requester, partner) > m_reach)
        {
            return CommandRejectionReason.OutOfRange;
        }

        return requester.HasInventoryWork || partner.HasInventoryWork
            ? CommandRejectionReason.ItemActionInFlight
            : CommandRejectionReason.None;
    }

    // Both still in the world with the connection each had when the request or the trade began, reachable, alive,
    // neither logging out nor removed, on one map instance.
    private bool AreTogether(Trader first, Trader second)
    {
        return IsPresent(first) && IsPresent(second) && ReferenceEquals(first.Character.Map, second.Character.Map);
    }

    private bool IsPresent(Trader trader)
    {
        CharacterSession character = trader.Character;
        return m_sessions.TryGetCharacter(character.Character, out CharacterSession? registered)
            && ReferenceEquals(registered, character)
            && ReferenceEquals(character.Connection, trader.Connection)
            && !character.Player.IsDead
            && !character.IsLoggingOut
            && !character.IsExpelled;
    }

    private static float Distance(CharacterSession first, CharacterSession second)
    {
        WorldPosition a = first.Player.Position;
        WorldPosition b = second.Player.Position;
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private void Forget(PendingTrade request)
    {
        m_byRequester.Remove(IdOf(request.Requester.Character));
        m_byPartner.Remove(IdOf(request.Partner.Character));
    }

    // A character that opens a trade can answer no other request, and its own request ends.
    private void ForgetAllOf(CharacterSession character)
    {
        if (m_byRequester.TryGetValue(IdOf(character), out PendingTrade? asked))
        {
            Forget(asked);
        }

        if (m_byPartner.TryGetValue(IdOf(character), out PendingTrade? asking))
        {
            Forget(asking);
        }
    }

    private void Tell(Trader trader, TradeEvent message)
    {
        if (ReferenceEquals(trader.Character.Connection, trader.Connection))
        {
            m_sender.Send(trader.Connection.Connection, message);
        }
    }

    private void Show(Trader trader, TradeSide message)
    {
        if (ReferenceEquals(trader.Character.Connection, trader.Connection))
        {
            m_sender.Send(trader.Connection.Connection, message);
        }
    }

    private static long IdOf(CharacterSession character)
    {
        return character.Character.Value;
    }

    private sealed class Trader
    {
        public Trader(CharacterSession character, ClientSession connection)
        {
            Character = character;
            Connection = connection;
        }

        public CharacterSession Character { get; }

        /// <summary>
        ///     The connection that controlled the character when its request or trade began; another one ends it.
        /// </summary>
        public ClientSession Connection { get; }

        public string Name => Character.Player.Name;

        public TradeOffering Offer { get; } = new();
    }

    private sealed class PendingTrade
    {
        public PendingTrade(Trader requester, Trader partner, uint expiresTick)
        {
            Requester = requester;
            Partner = partner;
            ExpiresTick = expiresTick;
        }

        public Trader Requester { get; }

        public Trader Partner { get; }

        public uint ExpiresTick { get; }
    }

    private sealed class OpenTrade
    {
        public OpenTrade(Guid id, Trader first, Trader second)
        {
            Id = id;
            First = first;
            Second = second;
        }

        /// <summary>
        ///     The trade's ID, its commit's operation ID.
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        ///     The trader with the lower character ID.
        /// </summary>
        public Trader First { get; }

        public Trader Second { get; }

        public bool IsCommitting { get; set; }
    }
}
}
