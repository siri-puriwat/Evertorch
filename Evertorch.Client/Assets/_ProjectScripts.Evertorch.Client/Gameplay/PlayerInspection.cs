using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     A whole inspection of another player (Gameplay Systems §11.1): what each of the eight positions holds, with its
///     refine level and cards, read at the player's appearance revision <see cref="Revision" />.
/// </summary>
public sealed class PlayerInspection
{
    private readonly InspectedItem[] m_items;

    public PlayerInspection(EntityId entity, uint revision, IReadOnlyList<InspectedItem> items)
    {
        Entity = entity;
        Revision = revision;
        m_items = new InspectedItem[items.Count];
        for (int index = 0; index < m_items.Length; index++)
        {
            m_items[index] = items[index];
        }
    }

    public EntityId Entity { get; }

    public uint Revision { get; }

    /// <summary>What each position holds, in position order from the weapon.</summary>
    public IReadOnlyList<InspectedItem> Items => m_items;

    public InspectedItem ItemIn(EquipmentSlot slot)
    {
        return m_items[(int)slot - 1];
    }
}
}
