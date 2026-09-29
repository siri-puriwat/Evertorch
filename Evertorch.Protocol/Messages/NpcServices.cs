using System;
using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     What an NPC offers, sent right after every spawn of the NPC to a client (Network Protocol §6, §9): each item it
///     trades with the price it sells for and the price it pays, each quest it gives with its objective and reward,
///     whether it resets a build, and each job change it offers.
///     A shop's prices and a quest's terms travel on purpose, because the player must see them (Content Pipeline §5).
/// </summary>
public sealed class NpcServices
{
    /// <summary>
    ///     The most items one message holds: 74 bytes each with the longest ID, beside the 14 of the header.
    /// </summary>
    public const int MaxEntries = 13;

    /// <summary>
    ///     The most quests one message holds: 154 bytes each with the longest IDs.
    /// </summary>
    public const int MaxOffers = 6;

    /// <summary>
    ///     The most job changes one message holds: 134 bytes each with the longest IDs.
    /// </summary>
    public const int MaxJobChanges = 7;

    /// <summary>
    ///     What one reliable message may carry, LiteNetLib's first MTU less its header; the content keeps every NPC's
    ///     services within it (Content Pipeline §4).
    /// </summary>
    public const int MaxEncodedLength = 1020;

    // The services byte after the offers: bit 0, the build's reset; the other bits are not yet defined.
    private const byte ResetService = 1;

    public NpcServices(
        EntityId npc,
        IReadOnlyList<NpcServiceEntry> entries,
        IReadOnlyList<NpcQuestOffer> offers,
        bool offersReset = false,
        IReadOnlyList<NpcJobChangeOffer>? jobChanges = null)
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

        jobChanges ??= Array.Empty<NpcJobChangeOffer>();
        if (jobChanges.Count > MaxJobChanges)
        {
            throw new ArgumentException(
                $"An NPC's services hold at most {MaxJobChanges} job changes.",
                nameof(jobChanges));
        }

        Npc = npc;
        Entries = entries;
        Offers = offers;
        OffersReset = offersReset;
        JobChanges = jobChanges;
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

    /// <summary>
    ///     Whether the NPC resets a character's build (Gameplay Systems §6.1).
    /// </summary>
    public bool OffersReset { get; }

    /// <summary>
    ///     The job changes the NPC offers, ordered by job ID; the NPC changes jobs when there is at least one
    ///     (Gameplay Systems §6.1).
    /// </summary>
    public IReadOnlyList<NpcJobChangeOffer> JobChanges { get; }

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
                || !reader.TryReadUInt64(out ulong jobExperience)
                || !reader.TryReadUInt32(out uint coins)
                || count == 0
                || (baseExperience == 0 && jobExperience == 0 && coins == 0)
                || !QuestDefinitionId.TryCreate(questText, out QuestDefinitionId quest)
                || !MonsterDefinitionId.TryCreate(monsterText, out MonsterDefinitionId monster)
                || ContainsQuest(offers, index, quest))
            {
                return false;
            }

            offers[index] = new NpcQuestOffer(quest, monster, count, baseExperience, jobExperience, coins);
        }

        if (!reader.TryReadByte(out byte services)
            || (services & ~ResetService) != 0
            || !reader.TryReadByte(out byte jobChangeCount)
            || jobChangeCount > MaxJobChanges)
        {
            return false;
        }

        var jobChanges = new NpcJobChangeOffer[jobChangeCount];
        for (int index = 0; index < jobChangeCount; index++)
        {
            if (!reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string jobText)
                || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string fromText)
                || !reader.TryReadUInt16(out ushort level)
                || level == 0
                || !JobDefinitionId.TryCreate(jobText, out JobDefinitionId job)
                || !JobDefinitionId.TryCreate(fromText, out JobDefinitionId from)
                || job == from
                || ContainsJob(jobChanges, index, job))
            {
                return false;
            }

            jobChanges[index] = new NpcJobChangeOffer(job, from, level);
        }

        if (!reader.IsAtEnd)
        {
            return false;
        }

        message = new NpcServices(new EntityId(npc), entries, offers, (services & ResetService) != 0, jobChanges);
        return true;
    }

    public int GetEncodedLength()
    {
        int length = sizeof(ushort) + sizeof(long) + 4 * sizeof(byte);
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
                + 2 * sizeof(ulong)
                + sizeof(uint);
        }

        foreach (NpcJobChangeOffer change in JobChanges)
        {
            length += WireText.GetEncodedLength(change.Job.Value, ProtocolLimits.MaxDefinitionIdBytes)
                + WireText.GetEncodedLength(change.FromJob.Value, ProtocolLimits.MaxDefinitionIdBytes)
                + sizeof(ushort);
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
            writer.WriteUInt64(offer.JobExperience);
            writer.WriteUInt32(offer.Coins);
        }

        writer.WriteByte(OffersReset ? ResetService : (byte)0);
        writer.WriteByte((byte)JobChanges.Count);
        foreach (NpcJobChangeOffer change in JobChanges)
        {
            writer.WriteString(change.Job.Value, ProtocolLimits.MaxDefinitionIdBytes);
            writer.WriteString(change.FromJob.Value, ProtocolLimits.MaxDefinitionIdBytes);
            writer.WriteUInt16(change.Level);
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

    private static bool ContainsJob(NpcJobChangeOffer[] changes, int count, JobDefinitionId job)
    {
        for (int index = 0; index < count; index++)
        {
            if (changes[index].Job == job)
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
