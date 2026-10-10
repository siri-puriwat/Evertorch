using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One item on the board as one player sees it: the board's price, which travels on purpose as a shop's prices do,
///     and the units of other accounts' listings the player may buy (Network Protocol §6).
/// </summary>
public readonly struct MarketBoardEntry
{
    public MarketBoardEntry(ItemDefinitionId item, uint price, uint stock)
    {
        Item = item;
        Price = price;
        Stock = stock;
    }

    public ItemDefinitionId Item { get; }

    /// <summary>
    ///     What a unit costs now, at least 1 coin.
    /// </summary>
    public uint Price { get; }

    public uint Stock { get; }

    internal int GetEncodedLength()
    {
        return WireText.GetEncodedLength(Item.Value, ProtocolLimits.MaxDefinitionIdBytes) + 2 * sizeof(uint);
    }

    internal void Write(ref WireWriter writer)
    {
        writer.WriteString(Item.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(Price);
        writer.WriteUInt32(Stock);
    }

    internal static bool TryRead(ref WireReader reader, out MarketBoardEntry entry)
    {
        entry = default;
        if (!reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string itemText)
            || !reader.TryReadUInt32(out uint price)
            || !reader.TryReadUInt32(out uint stock)
            || price == 0
            || !ItemDefinitionId.TryCreate(itemText, out ItemDefinitionId item))
        {
            return false;
        }

        entry = new MarketBoardEntry(item, price, stock);
        return true;
    }
}
}
