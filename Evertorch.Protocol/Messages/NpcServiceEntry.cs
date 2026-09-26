using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One item an NPC trades (Network Protocol §6): what it sells the item for and what it pays for one, each 0 when it
///     does not.
/// </summary>
public readonly struct NpcServiceEntry
{
    public NpcServiceEntry(ItemDefinitionId item, uint buyPrice, uint sellPrice)
    {
        Item = item;
        BuyPrice = buyPrice;
        SellPrice = sellPrice;
    }

    public ItemDefinitionId Item { get; }

    /// <summary>
    ///     The coins a buyer pays for one; 0 when the NPC does not sell the item.
    /// </summary>
    public uint BuyPrice { get; }

    /// <summary>
    ///     The coins the NPC pays for one; 0 when it does not buy the item.
    /// </summary>
    public uint SellPrice { get; }
}
}
