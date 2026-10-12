using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Takes the card <see cref="Card" /> out of slot <see cref="Slot" /> of the bag's row <see cref="InventoryItem" />
///     at the Smith <see cref="Npc" />, for the card's fee (Gameplay Systems §11.7). The card is the one the client
///     shows at that slot; the server refuses a row holding another there. Answered after the commit.
/// </summary>
public sealed class RemoveCard
{
    public const int MaxEncodedLength = sizeof(ushort) + 2 * sizeof(long) + sizeof(byte)
        + WireText.LengthPrefixBytes + ProtocolLimits.MaxDefinitionIdBytes + sizeof(uint);

    public RemoveCard(EntityId npc, long inventoryItem, byte slot, ItemDefinitionId card, uint commandSequence)
    {
        if (npc.Value <= 0 || inventoryItem <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inventoryItem), "An NPC's and a row's IDs are at least 1.");
        }

        if (slot < 1 || slot > ProtocolLimits.MaxCardsPerItem)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "A slot is 1 to 4.");
        }

        if (card == default)
        {
            throw new ArgumentException("A card has an item ID.", nameof(card));
        }

        Npc = npc;
        InventoryItem = inventoryItem;
        Slot = slot;
        Card = card;
        CommandSequence = commandSequence;
    }

    public EntityId Npc { get; }

    public long InventoryItem { get; }

    /// <summary>The card's slot, from 1.</summary>
    public byte Slot { get; }

    public ItemDefinitionId Card { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including an NPC's or a row's ID below 1, a slot outside 1 to 4, and a card
    ///     that breaks the ID rule.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out RemoveCard? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.RemoveCard)
            || !reader.TryReadInt64(out long npc)
            || !reader.TryReadInt64(out long row)
            || !reader.TryReadByte(out byte slot)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string cardText)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || npc <= 0
            || row <= 0
            || slot < 1
            || slot > ProtocolLimits.MaxCardsPerItem
            || !ItemDefinitionId.TryCreate(cardText, out ItemDefinitionId card))
        {
            return false;
        }

        message = new RemoveCard(new EntityId(npc), row, slot, card, sequence);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort) + 2 * sizeof(long) + sizeof(byte)
            + WireText.GetEncodedLength(Card.Value, ProtocolLimits.MaxDefinitionIdBytes) + sizeof(uint);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.RemoveCard);
        writer.WriteInt64(Npc.Value);
        writer.WriteInt64(InventoryItem);
        writer.WriteByte(Slot);
        writer.WriteString(Card.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
