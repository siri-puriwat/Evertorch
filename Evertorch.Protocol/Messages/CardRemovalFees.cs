using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     What the Smith <see cref="Npc" /> takes to take each card out (Network Protocol §6, §9), sent right after its
///     services and its refine tables at each of its spawns. The fees travel on purpose, as a shop's prices do.
/// </summary>
public sealed class CardRemovalFees
{
    /// <summary>
    ///     The most cards one message holds, all the content may hold: 70 bytes each with the longest ID.
    /// </summary>
    public const int MaxEntries = ContentLimits.MaxCards;

    public CardRemovalFees(EntityId npc, IReadOnlyList<CardRemovalFee> entries)
    {
        if (npc.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(npc), "An NPC's ID is at least 1.");
        }

        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        if (entries.Count < 1 || entries.Count > MaxEntries || !IsEachCardOnce(entries))
        {
            throw new ArgumentException("A list names 1 to 14 cards, each once.", nameof(entries));
        }

        Npc = npc;
    }

    public EntityId Npc { get; }

    /// <summary>
    ///     Each card with its fee, ordered by card ID.
    /// </summary>
    public IReadOnlyList<CardRemovalFee> Entries { get; }

    /// <summary>
    ///     False for a malformed message, including an NPC's ID below 1, no entry or more than 14, a card listed twice
    ///     or breaking the ID rule, and a fee above 1,000,000.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out CardRemovalFees? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (source.Length > ProtocolLimits.MaxReliableMessageBytes
            || !reader.TryReadOpcode(MessageOpcode.CardRemovalFees)
            || !reader.TryReadInt64(out long npc)
            || !reader.TryReadByte(out byte count)
            || npc <= 0
            || count < 1
            || count > MaxEntries)
        {
            return false;
        }

        var entries = new CardRemovalFee[count];
        for (int index = 0; index < count; index++)
        {
            if (!reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string cardText)
                || !reader.TryReadUInt32(out uint fee)
                || fee > ContentLimits.MaxCardRemovalFee
                || !ItemDefinitionId.TryCreate(cardText, out ItemDefinitionId card))
            {
                return false;
            }

            entries[index] = new CardRemovalFee(card, fee);
        }

        if (!reader.IsAtEnd || !IsEachCardOnce(entries))
        {
            return false;
        }

        message = new CardRemovalFees(new EntityId(npc), entries);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = sizeof(ushort) + sizeof(long) + sizeof(byte);
        foreach (CardRemovalFee entry in Entries)
        {
            length += WireText.GetEncodedLength(entry.Card.Value, ProtocolLimits.MaxDefinitionIdBytes) + sizeof(uint);
        }

        return length;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.CardRemovalFees);
        writer.WriteInt64(Npc.Value);
        writer.WriteByte((byte)Entries.Count);
        foreach (CardRemovalFee entry in Entries)
        {
            writer.WriteString(entry.Card.Value, ProtocolLimits.MaxDefinitionIdBytes);
            writer.WriteUInt32(entry.Fee);
        }

        return writer.Position;
    }

    private static bool IsEachCardOnce(IReadOnlyList<CardRemovalFee> entries)
    {
        var seen = new HashSet<ItemDefinitionId>();
        foreach (CardRemovalFee entry in entries)
        {
            if (!seen.Add(entry.Card))
            {
                return false;
            }
        }

        return true;
    }
}
}
