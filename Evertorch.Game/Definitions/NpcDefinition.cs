using System;
using System.Collections.Generic;

namespace Evertorch.Game
{
/// <summary>
///     An NPC (Gameplay Systems §6.1): its name and, when it keeps a shop, the items the shop sells. An NPC with a shop
///     also buys every item whose sell price is above 0 (Gameplay Systems §11.3), and an NPC's quests are the quests
///     that name it as their giver.
/// </summary>
public sealed class NpcDefinition
{
    public NpcDefinition(
        NpcDefinitionId id,
        string displayName,
        IReadOnlyList<ShopEntry>? shop = null,
        bool offersReset = false)
    {
        Id = id;
        DisplayName = displayName;
        Shop = shop ?? Array.Empty<ShopEntry>();
        OffersReset = offersReset;
    }

    public NpcDefinitionId Id { get; }

    public string DisplayName { get; }

    /// <summary>
    ///     What the NPC sells, in authored order; empty when it keeps no shop.
    /// </summary>
    public IReadOnlyList<ShopEntry> Shop { get; }

    public bool HasShop => Shop.Count > 0;

    /// <summary>
    ///     Whether the NPC is a Guildmaster, who returns every stat and skill point (Gameplay Systems §6.1).
    /// </summary>
    public bool OffersReset { get; }
}
}
