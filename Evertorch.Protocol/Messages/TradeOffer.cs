using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Sets how much of one row of the player's own bag its open trade offers (Gameplay Systems §16); 0 takes the row
///     back. Row 0 means coins, at most those held.
/// </summary>
public sealed class TradeOffer
{
    public const int EncodedLength = sizeof(ushort) + sizeof(long) + 2 * sizeof(uint);

    public TradeOffer(long inventoryItem, uint quantity, uint commandSequence)
    {
        if (inventoryItem < 0 || quantity > ContentLimits.MaxCurrency)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inventoryItem),
                "A row is 0 for coins or a row's ID, and a quantity stops at the coin cap.");
        }

        InventoryItem = inventoryItem;
        Quantity = quantity;
        CommandSequence = commandSequence;
    }

    /// <summary>
    ///     The row offered, or 0 for coins.
    /// </summary>
    public long InventoryItem { get; }

    public bool IsCoins => InventoryItem == 0;

    public uint Quantity { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including a negative row and a quantity past the coin cap.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out TradeOffer? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.TradeOffer)
            || !reader.TryReadInt64(out long inventoryItem)
            || !reader.TryReadUInt32(out uint quantity)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || inventoryItem < 0
            || quantity > ContentLimits.MaxCurrency)
        {
            return false;
        }

        message = new TradeOffer(inventoryItem, quantity, sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.TradeOffer);
        writer.WriteInt64(InventoryItem);
        writer.WriteUInt32(Quantity);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
