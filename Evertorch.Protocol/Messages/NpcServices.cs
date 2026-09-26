using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     What an NPC offers, sent right after every spawn of the NPC to a client (Network Protocol §6, §9): each item it
///     trades with the price it sells for and the price it pays, and each quest it gives with its objective and reward.
///     A shop's prices and a quest's terms travel on purpose, because the player must see them (Content Pipeline §5).
/// </summary>
public sealed class NpcServices
{
    /// <summary>
    ///     The most items one message holds: 74 bytes each with the longest ID, beside the 12 of the header.
    /// </summary>
    public const int MaxEntries = 13;

    /// <summary>
    ///     The most quests one message holds: 146 bytes each with the longest IDs.
    /// </summary>
    public const int MaxOffers = 6;

    /// <summary>
    ///     What one reliable message may carry, LiteNetLib's first MTU less its header; the content keeps every NPC's
    ///     services within it (Content Pipeline §4).
    /// </summary>
    public const int MaxEncodedLength = 1020;

    public NpcServices(EntityId npc, IReadOnlyList<NpcServiceEntry> entries, IReadOnlyList<NpcQuestOffer> offers)
    {
        if (entries == null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        if (offers == null)
        {
            throw new ArgumentNullException(nameof(offers));
        }

        if (entries.Count > MaxEntries)
        {
            throw new ArgumentException($"An NPC's services hold at most {MaxEntries} items.", nameof(entries));
        }

        if (offers.Count > MaxOffers)
        {
            throw new ArgumentException($"An NPC's services hold at most {MaxOffers} quests.", nameof(offers));
        }

        Npc = npc;
        Entries = entries;
        Offers = offers;
        if (GetEncodedLength() > MaxEncodedLength)
        {
            throw new ArgumentException($"An NPC's services must fit {MaxEncodedLength} bytes.", nameof(entries));
        }
    }

    public EntityId Npc { get; }

    /// <summary>
    ///     The items the NPC trades, ordered by item ID.
    /// </summary>
    public IReadOnlyList<NpcServiceEntry> Entries { get; }

    /// <summary>
    ///     The quests the NPC gives, ordered by quest ID.
    /// </summary>
    public IReadOnlyList<NpcQuestOffer> Offers { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out NpcServices? message)
    {
        message = null;
        var reader = new WireReader(source);

        // Counts within their limits can still add up to more than the constructor accepts.
        if (source.Length > MaxEncodedLength
            || !reader.TryReadOpcode(MessageOpcode.NpcServices)
            || !reader.TryReadInt64(out long npc)
            || !reader.TryReadByte(out byte entryCount)
            || entryCount > MaxEntries)
        {
            return false;
        }

        var entries = new NpcServiceEntry[entryCount];
        for (int index = 0; index < entryCount; index++)
        {
            if (!reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string itemText)
                || !reader.TryReadUInt32(out uint buyPrice)
                || !reader.TryReadUInt32(out uint sellPrice)
                || (buyPrice == 0 && sellPrice == 0)
                || !ItemDefinitionId.TryCreate(itemText, out ItemDefinitionId item)
                || ContainsItem(entries, index, item))
            {
                return false;
            }

            entries[index] = new NpcServiceEntry(item, buyPrice, sellPrice);
        }

        if (!reader.TryReadByte(out byte offerCount) || offerCount > MaxOffers)
        {
            return false;
        }

        var offers = new NpcQuestOffer[offerCount];
        for (int index = 0; index < offerCount; index++)
        {
            if (!reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string questText)
                || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string monsterText)
                || !reader.TryReadUInt16(out ushort count)
                || !reader.TryReadUInt64(out ulong baseExperience)
                || !reader.TryReadUInt32(out uint coins)
                || count == 0
                || (baseExperience == 0 && coins == 0)
                || !QuestDefinitionId.TryCreate(questText, out QuestDefinitionId quest)
                || !MonsterDefinitionId.TryCreate(monsterText, out MonsterDefinitionId monster)
                || ContainsQuest(offers, index, quest))
            {
                return false;
            }

            offers[index] = new NpcQuestOffer(quest, monster, count, baseExperience, coins);
        }

        if (!reader.IsAtEnd)
        {
            return false;
        }

        message = new NpcServices(new EntityId(npc), entries, offers);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = sizeof(ushort) + sizeof(long) + sizeof(byte) + sizeof(byte);
        foreach (NpcServiceEntry entry in Entries)
        {
            length += WireText.GetEncodedLength(entry.Item.Value, ProtocolLimits.MaxDefinitionIdBytes)
                + 2 * sizeof(uint);
        }

        foreach (NpcQuestOffer offer in Offers)
        {
            length += WireText.GetEncodedLength(offer.Quest.Value, ProtocolLimits.MaxDefinitionIdBytes)
                + WireText.GetEncodedLength(offer.Monster.Value, ProtocolLimits.MaxDefinitionIdBytes)
                + sizeof(ushort)
                + sizeof(ulong)
                + sizeof(uint);
        }

        return length;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.NpcServices);
        writer.WriteInt64(Npc.Value);
        writer.WriteByte((byte)Entries.Count);
        foreach (NpcServiceEntry entry in Entries)
        {
            writer.WriteString(entry.Item.Value, ProtocolLimits.MaxDefinitionIdBytes);
            writer.WriteUInt32(entry.BuyPrice);
            writer.WriteUInt32(entry.SellPrice);
        }

        writer.WriteByte((byte)Offers.Count);
        foreach (NpcQuestOffer offer in Offers)
        {
            writer.WriteString(offer.Quest.Value, ProtocolLimits.MaxDefinitionIdBytes);
            writer.WriteString(offer.Monster.Value, ProtocolLimits.MaxDefinitionIdBytes);
            writer.WriteUInt16(offer.Count);
            writer.WriteUInt64(offer.BaseExperience);
            writer.WriteUInt32(offer.Coins);
        }

        return writer.Position;
    }

    private static bool ContainsItem(NpcServiceEntry[] entries, int count, ItemDefinitionId item)
    {
        for (int index = 0; index < count; index++)
        {
            if (entries[index].Item == item)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsQuest(NpcQuestOffer[] offers, int count, QuestDefinitionId quest)
    {
        for (int index = 0; index < count; index++)
        {
            if (offers[index].Quest == quest)
            {
                return true;
            }
        }

        return false;
    }
}
}
