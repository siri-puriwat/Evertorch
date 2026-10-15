using System;
using System.Collections.Generic;

namespace Evertorch.Game
{
/// <summary>
///     Where each category of item is worn (Gameplay Systems §11.1): a weapon, armor, a headgear, a shield, a garment,
///     and footwear each in a position of its own, and an accessory in either accessory position. The one check the
///     content, the server, and the client share, so none of them decides it alone.
/// </summary>
public static class EquipmentPositions
{
    /// <summary>The eight positions in their order on the wire, the weapon first.</summary>
    public static readonly IReadOnlyList<EquipmentSlot> All = new[]
    {
        EquipmentSlot.Weapon, EquipmentSlot.Armor, EquipmentSlot.Headgear, EquipmentSlot.Shield,
        EquipmentSlot.Garment, EquipmentSlot.Footwear, EquipmentSlot.Accessory1, EquipmentSlot.Accessory2
    };

    private static readonly IReadOnlyList<EquipmentSlot> WeaponSlots = new[] { EquipmentSlot.Weapon };
    private static readonly IReadOnlyList<EquipmentSlot> ArmorSlots = new[] { EquipmentSlot.Armor };
    private static readonly IReadOnlyList<EquipmentSlot> HeadgearSlots = new[] { EquipmentSlot.Headgear };
    private static readonly IReadOnlyList<EquipmentSlot> ShieldSlots = new[] { EquipmentSlot.Shield };
    private static readonly IReadOnlyList<EquipmentSlot> GarmentSlots = new[] { EquipmentSlot.Garment };
    private static readonly IReadOnlyList<EquipmentSlot> FootwearSlots = new[] { EquipmentSlot.Footwear };

    private static readonly IReadOnlyList<EquipmentSlot> AccessorySlots = new[]
    {
        EquipmentSlot.Accessory1, EquipmentSlot.Accessory2
    };

    /// <summary>
    ///     The positions an item of <paramref name="type" /> may be worn in, in their order; none for an item that is not
    ///     equipment.
    /// </summary>
    public static IReadOnlyList<EquipmentSlot> Of(ItemType type)
    {
        return type switch
        {
            ItemType.Weapon => WeaponSlots,
            ItemType.Armor => ArmorSlots,
            ItemType.Headgear => HeadgearSlots,
            ItemType.Shield => ShieldSlots,
            ItemType.Garment => GarmentSlots,
            ItemType.Footwear => FootwearSlots,
            ItemType.Accessory => AccessorySlots,
            _ => Array.Empty<EquipmentSlot>()
        };
    }

    /// <summary>Whether an item of <paramref name="type" /> is worn at all.</summary>
    public static bool IsEquipment(ItemType type)
    {
        return Of(type).Count > 0;
    }

    /// <summary>Whether an item of <paramref name="type" /> may be worn in <paramref name="slot" />.</summary>
    public static bool Fits(ItemType type, EquipmentSlot slot)
    {
        foreach (EquipmentSlot position in Of(type))
        {
            if (position == slot)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="slot" /> is one of the eight positions, not <see cref="EquipmentSlot.None" />.</summary>
    public static bool IsPosition(EquipmentSlot slot)
    {
        return slot >= EquipmentSlot.Weapon && slot <= EquipmentSlot.Accessory2;
    }

    /// <summary>The category <paramref name="slot" /> takes; both accessory positions take accessories.</summary>
    public static ItemType CategoryOf(EquipmentSlot slot)
    {
        return slot switch
        {
            EquipmentSlot.Weapon => ItemType.Weapon,
            EquipmentSlot.Armor => ItemType.Armor,
            EquipmentSlot.Headgear => ItemType.Headgear,
            EquipmentSlot.Shield => ItemType.Shield,
            EquipmentSlot.Garment => ItemType.Garment,
            EquipmentSlot.Footwear => ItemType.Footwear,
            EquipmentSlot.Accessory1 => ItemType.Accessory,
            EquipmentSlot.Accessory2 => ItemType.Accessory,
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Not a position.")
        };
    }
}
}
