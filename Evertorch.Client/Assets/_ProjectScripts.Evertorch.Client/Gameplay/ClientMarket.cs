using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The market board as the Broker last told this character (Gameplay Systems §11.5): each listable item's price and
///     the units the character may buy, upserted as entries arrive, and the character's own listings, taken whole, which
///     end a read. A refused listing, purchase, or collection (1) means what is shown is out of date, and so does a sale
///     of one of the character's listings, so the window reads again.
/// </summary>
public sealed class ClientMarket
{
    private readonly Dictionary<ItemDefinitionId, MarketBoardEntry> m_entries = new();
    private readonly List<MarketBoardEntry> m_board = new();

    /// <summary>
    ///     Every item the board has told of, in item order.
    /// </summary>
    public IReadOnlyList<MarketBoardEntry> Board => m_board;

    /// <summary>
    ///     The character's listings as the last read gave them; a new list with each read.
    /// </summary>
    public IReadOnlyList<MarketListingEntry> Listings { get; private set; } = Array.Empty<MarketListingEntry>();

    /// <summary>
    ///     A whole read has been taken and nothing has made it stale since.
    /// </summary>
    public bool IsCurrent { get; private set; }

    /// <summary>
    ///     A read was asked for and has not been answered.
    /// </summary>
    public bool IsReading { get; private set; }

    /// <summary>
    ///     The last read was refused; nothing reads again until the Broker is asked anew.
    /// </summary>
    public bool IsReadRefused { get; private set; }

    /// <summary>
    ///     Goes up with every change of what is known, so a window can tell when to show it again.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    ///     Raised for each notice of one of the character's listings: listed, sold, or collected.
    /// </summary>
    public event Action<MarketEvent>? NoticeReceived;

    /// <summary>
    ///     Whether the board takes <paramref name="item" />: every listable item has an entry once read.
    /// </summary>
    public bool TryGetEntry(ItemDefinitionId item, out MarketBoardEntry entry)
    {
        return m_entries.TryGetValue(item, out entry);
    }

    public void BeginRead()
    {
        IsReading = true;
        IsReadRefused = false;
        Version++;
    }

    public void OnBoard(MarketBoard board)
    {
        if (board == null)
        {
            throw new ArgumentNullException(nameof(board));
        }

        foreach (MarketBoardEntry entry in board.Entries)
        {
            m_entries[entry.Item] = entry;
        }

        m_board.Clear();
        m_board.AddRange(m_entries.Values);
        m_board.Sort((left, right) => string.CompareOrdinal(left.Item.Value, right.Item.Value));
        Version++;
    }

    public void OnMine(MarketMine mine)
    {
        if (mine == null)
        {
            throw new ArgumentNullException(nameof(mine));
        }

        Listings = new List<MarketListingEntry>(mine.Entries);
        IsCurrent = true;
        IsReading = false;
        Version++;
    }

    public void OnNotice(MarketEvent notice)
    {
        if (notice == null)
        {
            throw new ArgumentNullException(nameof(notice));
        }

        // A sale changed a listing this character was shown; its own commands are followed by the board's push.
        if (notice.Kind == MarketEventKind.Sold)
        {
            IsCurrent = false;
            Version++;
        }

        NoticeReceived?.Invoke(notice);
    }

    /// <summary>
    ///     A refused read ends the wait for it and leaves the board unread. A command the commit refused with 1, a price
    ///     past its limit, too few units, or a listing gone, means what is shown is out of date; any other refusal says
    ///     nothing of the board.
    /// </summary>
    public void OnRefused(MarketCommand command, CommandRejectionReason reason)
    {
        if (command == MarketCommand.Open)
        {
            IsReading = false;
            IsCurrent = false;
            IsReadRefused = true;
        }
        else if (reason == CommandRejectionReason.InvalidTarget)
        {
            IsCurrent = false;
        }
        else
        {
            return;
        }

        Version++;
    }
}
}
