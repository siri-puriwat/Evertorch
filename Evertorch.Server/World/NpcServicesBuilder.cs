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
///     §11.3); it gives every quest that names it.
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
                (uint)quest.Currency))
            .ToArray();
        return new NpcServices(entity, entries, offers);
    }
}
}
