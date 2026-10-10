using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Buys a quantity of <see cref="Item" /> on the board at the Broker <see cref="Npc" />, at the board's price but at
///     most <see cref="PriceLimit" /> a unit (Gameplay Systems §11.5). Answered after the commit.
/// </summary>
public sealed class MarketBuy
{
    public const int MaxEncodedLength =
        sizeof(ushort) + sizeof(long) + sizeof(ushort) + ProtocolLimits.MaxDefinitionIdBytes + 3 * sizeof(uint);

    public MarketBuy(EntityId npc, ItemDefinitionId item, uint quantity, uint priceLimit, uint commandSequence)
    {
        if (quantity == 0 || priceLimit == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A quantity and a price limit are at least 1.");
        }

        if (item.Value.Length == 0)
        {
            throw new ArgumentException("A purchase names its item.", nameof(item));
        }

        Npc = npc;
        Item = item;
        Quantity = quantity;
        PriceLimit = priceLimit;
        CommandSequence = commandSequence;
    }

    public EntityId Npc { get; }

    public ItemDefinitionId Item { get; }

    public uint Quantity { get; }

    /// <summary>
    ///     The most the buyer pays a unit: a board whose price passed it refuses the purchase.
    /// </summary>
    public uint PriceLimit { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including an item ID that breaks the ID rule and a quantity or a price limit
    ///     below 1.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out MarketBuy? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.MarketBuy)
            || !reader.TryReadInt64(out long npc)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string itemText)
            || !reader.TryReadUInt32(out uint quantity)
            || !reader.TryReadUInt32(out uint priceLimit)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || quantity == 0
            || priceLimit == 0
            || !ItemDefinitionId.TryCreate(itemText, out ItemDefinitionId item))
        {
            return false;
        }

        message = new MarketBuy(new EntityId(npc), item, quantity, priceLimit, sequence);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + sizeof(long)
            + WireText.GetEncodedLength(Item.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + 3 * sizeof(uint);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.MarketBuy);
        writer.WriteInt64(Npc.Value);
        writer.WriteString(Item.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(Quantity);
        writer.WriteUInt32(PriceLimit);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
