using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One part of what the Smith <see cref="Npc" /> takes to take each card out (Network Protocol §6, §9), sent with the
///     other parts right after its services and its refine tables at each of its spawns. The fees travel in card-ID
///     order over one to eight parts of at most fourteen cards, every card once over all the parts, and the client takes
///     them whole or not at all. They travel on purpose, as a shop's prices do.
/// </summary>
public sealed class CardRemovalFees
{
    public const int MaxParts = 8;

    /// <summary>
    ///     The most cards one part holds: 70 bytes each with the longest ID, so a full part takes 993 bytes.
    /// </summary>
    public const int MaxEntries = 14;

    private const int HeaderLength = sizeof(ushort) + sizeof(long) + 3 * sizeof(byte);

    public CardRemovalFees(EntityId npc, byte part, byte partCount, IReadOnlyList<CardRemovalFee> entries)
    {
        if (npc.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(npc), "An NPC's ID is at least 1.");
        }

        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        if (partCount == 0 || partCount > MaxParts || part >= partCount || entries.Count < 1
            || entries.Count > MaxEntries || !IsEachCardOnce(entries))
        {
            throw new ArgumentException(
                "A part is numbered below its count of at most 8, and names 1 to 14 cards, each once.",
                nameof(entries));
        }

        Npc = npc;
        Part = part;
        PartCount = partCount;
        if (GetEncodedLength() > ProtocolLimits.MaxReliableMessageBytes)
        {
            throw new ArgumentException("A part fits one reliable message.", nameof(entries));
        }
    }

    public EntityId Npc { get; }

    /// <summary>The part's position, from 0.</summary>
    public byte Part { get; }

    public byte PartCount { get; }

    /// <summary>
    ///     Each card of the part with its fee, ordered by card ID.
    /// </summary>
    public IReadOnlyList<CardRemovalFee> Entries { get; }

    /// <summary>
    ///     The fees of <paramref name="fees" />, ordered by card ID, in the consecutive parts that carry them, each filled
    ///     to fourteen cards or until the next card would pass a part's bytes.
    /// </summary>
    public static IReadOnlyList<CardRemovalFees> CreateParts(EntityId npc, IReadOnlyList<CardRemovalFee> fees)
    {
        if (fees == null)
        {
            throw new ArgumentNullException(nameof(fees));
        }

        if (fees.Count == 0)
        {
            throw new ArgumentException("A list of fees names at least one card.", nameof(fees));
        }

        List<List<CardRemovalFee>> slices = WireParts.Pack(fees, HeaderLength, MaxEntries, EntryLength);
        if (slices.Count > MaxParts)
        {
            throw new ArgumentException("The fees fit eight parts.", nameof(fees));
        }

        var parts = new CardRemovalFees[slices.Count];
        for (int part = 0; part < parts.Length; part++)
        {
            parts[part] = new CardRemovalFees(npc, (byte)part, (byte)parts.Length, slices[part]);
        }

        return parts;
    }

    /// <summary>
    ///     False for a malformed part, including an NPC's ID below 1, a part at or past its count, a count of 0 or above
    ///     8, no entry or more than 14, a card listed twice or breaking the ID rule, and a fee above 1,000,000.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out CardRemovalFees? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (source.Length > ProtocolLimits.MaxReliableMessageBytes
            || !reader.TryReadOpcode(MessageOpcode.CardRemovalFees)
            || !reader.TryReadInt64(out long npc)
            || !reader.TryReadByte(out byte part)
            || !reader.TryReadByte(out byte partCount)
            || !reader.TryReadByte(out byte count)
            || npc <= 0
            || partCount == 0
            || partCount > MaxParts
            || part >= partCount
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

        message = new CardRemovalFees(new EntityId(npc), part, partCount, entries);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = HeaderLength;
        foreach (CardRemovalFee entry in Entries)
        {
            length += EntryLength(entry);
        }

        return length;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.CardRemovalFees);
        writer.WriteInt64(Npc.Value);
        writer.WriteByte(Part);
        writer.WriteByte(PartCount);
        writer.WriteByte((byte)Entries.Count);
        foreach (CardRemovalFee entry in Entries)
        {
            writer.WriteString(entry.Card.Value, ProtocolLimits.MaxDefinitionIdBytes);
            writer.WriteUInt32(entry.Fee);
        }

        return writer.Position;
    }

    private static int EntryLength(CardRemovalFee entry)
    {
        return WireText.GetEncodedLength(entry.Card.Value, ProtocolLimits.MaxDefinitionIdBytes) + sizeof(uint);
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
