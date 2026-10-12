using System;

namespace Evertorch.Protocol
{
/// <summary>
///     Inserts the card in the bag's row <see cref="CardItem" /> into the bag's row <see cref="InventoryItem" />
///     (Gameplay Systems §11.7). No NPC is named and nothing is paid; answered after the commit.
/// </summary>
public sealed class InsertCard
{
    public const int EncodedLength = sizeof(ushort) + 2 * sizeof(long) + sizeof(uint);

    public InsertCard(long cardItem, long inventoryItem, uint commandSequence)
    {
        if (cardItem <= 0 || inventoryItem <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inventoryItem), "A row's ID is at least 1.");
        }

        CardItem = cardItem;
        InventoryItem = inventoryItem;
        CommandSequence = commandSequence;
    }

    /// <summary>The row holding the card, one of which goes in.</summary>
    public long CardItem { get; }

    /// <summary>The row the card goes into.</summary>
    public long InventoryItem { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including a row's ID below 1.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out InsertCard? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.InsertCard)
            || !reader.TryReadInt64(out long card)
            || !reader.TryReadInt64(out long row)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || card <= 0
            || row <= 0)
        {
            return false;
        }

        message = new InsertCard(card, row, sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.InsertCard);
        writer.WriteInt64(CardItem);
        writer.WriteInt64(InventoryItem);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
