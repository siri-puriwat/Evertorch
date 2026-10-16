using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     Turns an NPC's content into the services its clients are sent (Network Protocol §6): an NPC with a shop trades
///     its stock at the stock's prices and buys every item whose sell price is above 0 at that price (Gameplay Systems
///     §11.3); it gives every quest that names it, it resets a build when its content says so, and when it changes
///     jobs, it offers every first job at its base job's cap (§6.1). A Storekeeper says it keeps storage (§11.4), a
///     Broker that it keeps the board (§11.5), and a Smith that it refines (§11.6) and takes cards out (§11.7).
/// </summary>
public static class NpcServicesBuilder
{
    public static NpcServices Build(EntityId entity, NpcDefinition npc, ServerContent content)
    {
        var entries = new List<NpcServiceEntry>();
        if (npc.HasShop)
        {
            var traded = new SortedSet<string>(StringComparer.Ordinal);
            foreach (ShopEntry stock in npc.Shop)
            {
                traded.Add(stock.Item.Value);
            }

            foreach (ItemDefinition item in content.Items.Values.Where(item => item.SellPrice > 0))
            {
                traded.Add(item.Id.Value);
            }

            foreach (string id in traded)
            {
                var item = new ItemDefinitionId(id);
                ShopEntry? stock = npc.Shop.FirstOrDefault(entry => entry.Item == item);
                entries.Add(
                    new NpcServiceEntry(item, (uint)(stock?.Price ?? 0), (uint)content.Items[item].SellPrice));
            }
        }

        NpcQuestOffer[] offers = content.Quests.Values
            .Where(quest => quest.Giver == npc.Id)
            .OrderBy(quest => quest.Id.Value, StringComparer.Ordinal)
            .Select(quest => new NpcQuestOffer(
                quest.Id,
                quest.Monster,
                (ushort)quest.Count,
                (ulong)quest.BaseExperience,
                (ulong)quest.JobExperience,
                (uint)quest.Currency))
            .ToArray();
        NpcJobChangeOffer[] jobChanges = npc.OffersJobChange
            ? content.Jobs.Values
                .Where(job => job.BaseJob.HasValue)
                .OrderBy(job => job.Id.Value, StringComparer.Ordinal)
                .Select(job => new NpcJobChangeOffer(
                    job.Id,
                    job.BaseJob!.Value,
                    (ushort)JobCap(content, content.Jobs[job.BaseJob.Value])))
                .ToArray()
            : Array.Empty<NpcJobChangeOffer>();
        return new NpcServices(
            entity,
            entries,
            offers,
            npc.OffersReset,
            jobChanges,
            npc.KeepsStorage,
            npc.KeepsMarket,
            npc.Refines,
            RemovesCards(npc, content));
    }

    // A refiner takes cards out when the content holds any, so the fees it sends always follow the services that
    // promise them.
    private static bool RemovesCards(NpcDefinition npc, ServerContent content)
    {
        return npc.Refines && content.Items.Values.Any(item => item.Card != null);
    }

    /// <summary>
    ///     What a refiner takes to take each card out (Network Protocol §9), ordered by card ID, in the parts that carry
    ///     it; none for an NPC that does not refine, or when the content holds no card. The Smith that refines also takes
    ///     cards out.
    /// </summary>
    public static IReadOnlyList<CardRemovalFees> BuildCardRemovalFees(
        EntityId entity,
        NpcDefinition npc,
        ServerContent content)
    {
        if (!npc.Refines)
        {
            return Array.Empty<CardRemovalFees>();
        }

        CardRemovalFee[] fees = content.Items.Values
            .Where(item => item.Card != null)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .Select(item => new CardRemovalFee(item.Id, (uint)item.Card!.RemovalFee))
            .ToArray();
        return fees.Length == 0 ? Array.Empty<CardRemovalFees>() : CardRemovalFees.CreateParts(entity, fees);
    }

    /// <summary>
    ///     A refiner's tables (Network Protocol §9): each table that an item names, in the order of their IDs, with its
    ///     items in theirs; none for an NPC that does not refine.
    /// </summary>
    public static IReadOnlyList<RefineTable> BuildRefineTables(
        EntityId entity,
        NpcDefinition npc,
        ServerContent content)
    {
        if (!npc.Refines)
        {
            return Array.Empty<RefineTable>();
        }

        var tables = new List<RefineTable>();
        foreach (RefineTableDefinition table in content.RefineTables.Values
                     .OrderBy(table => table.Id.Value, StringComparer.Ordinal))
        {
            ItemDefinitionId[] items = content.Items.Values
                .Where(item => item.Refine == table.Id)
                .Select(item => item.Id)
                .OrderBy(item => item.Value, StringComparer.Ordinal)
                .ToArray();
            if (items.Length == 0)
            {
                continue;
            }

            tables.Add(
                new RefineTable(
                    entity,
                    (byte)tables.Count,
                    table.Material,
                    (byte)table.MaterialQuantity,
                    table.OnFailure,
                    (byte)table.DowngradeBy,
                    table.Steps.Select(step => new RefineTableStep((byte)step.Chance, (uint)step.Coins)).ToArray(),
                    items));
        }

        return tables;
    }

    /// <summary>
    ///     The job level at which <paramref name="job" />'s table ends: a table of n entries caps it at n + 1.
    /// </summary>
    public static int JobCap(ServerContent content, JobDefinition job)
    {
        return content.ExperienceTables[job.JobExperienceTable].Levels.Count + 1;
    }
}
}
