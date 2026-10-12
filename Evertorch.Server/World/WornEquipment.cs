using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;

namespace Evertorch.Server
{
/// <summary>
///     What a player's worn weapon and armor add in combat (Gameplay Systems §11.1): the weapon's attack, its refine
///     bonus and the worn cards' attack as a flat attack, a staff's refine bonus and the cards' magic attack to magic
///     attack, and the armor's defense with its refine bonus and the cards' defense as hard defense. Combat reads it at
///     each hit and the sheet shows it, so the two never disagree.
/// </summary>
public sealed class WornEquipment
{
    public static readonly WornEquipment None = new(0, 0, 0, 0);

    public WornEquipment(int weaponAttack, int flatAttack, int magicAttack, int hardDefense)
    {
        WeaponAttack = weaponAttack;
        FlatAttack = flatAttack;
        MagicAttack = magicAttack;
        HardDefense = hardDefense;
    }

    /// <summary>The worn weapon's own attack, which its draw varies; 0 unarmed.</summary>
    public int WeaponAttack { get; }

    /// <summary>The worn weapon's refine bonus and the worn cards' attack, added after the weapon's draw.</summary>
    public int FlatAttack { get; }

    /// <summary>A worn staff's refine bonus and the worn cards' magic attack, with any weapon.</summary>
    public int MagicAttack { get; }

    /// <summary>The worn armor's defense with its refine bonus, and the worn cards' defense.</summary>
    public int HardDefense { get; }

    /// <summary>
    ///     What the given worn items add, each at its refine level, with their cards; a level past its table counts the
    ///     table's last bonus, and an item without a table counts none.
    /// </summary>
    public static WornEquipment Of(
        ItemDefinition? weapon,
        RefineTableDefinition? weaponTable,
        int weaponLevel,
        ItemDefinition? armor,
        RefineTableDefinition? armorTable,
        int armorLevel,
        CardBonus? cards = null)
    {
        CardBonus added = cards ?? CardBonus.None;
        int weaponBonus = weapon == null ? 0 : RefineRules.BonusAt(weaponTable, weaponLevel);
        bool isStaff = weapon?.Equipment?.WeaponType == WeaponType.Staff;
        int armorDefense = armor == null
            ? 0
            : (armor.Equipment?.Defense ?? 0) + RefineRules.BonusAt(armorTable, armorLevel);
        return new WornEquipment(
            weapon?.Equipment?.Attack ?? 0,
            weaponBonus + added.Attack,
            (isStaff ? weaponBonus : 0) + added.MagicAttack,
            armorDefense + added.Defense);
    }

    /// <summary>
    ///     What the cards in the given worn rows add, summed; a card the content no longer holds as a card adds nothing.
    /// </summary>
    public static CardBonus CardsOf(ServerContent content, params IReadOnlyList<ItemDefinitionId>[] rows)
    {
        CardBonus sum = CardBonus.None;
        foreach (IReadOnlyList<ItemDefinitionId> cards in rows)
        {
            foreach (ItemDefinitionId card in cards)
            {
                if (content.Items.TryGetValue(card, out ItemDefinition? item) && item.Card != null)
                {
                    sum = sum.Plus(item.Card.Bonus);
                }
            }
        }

        return sum;
    }

    /// <summary>
    ///     The one place a player's worn items are set (Gameplay Systems §11.1): at the load and at every change of what is
    ///     worn. The worn items' values, the weapon seen in hand, and what they add in combat follow the given rows.
    /// </summary>
    public static void Dress(
        PlayerEntity player,
        ServerContent content,
        ItemDefinitionId? weapon,
        int weaponLevel,
        ItemDefinitionId? armor,
        int armorLevel,
        IReadOnlyList<ItemDefinitionId>? weaponCards = null,
        IReadOnlyList<ItemDefinitionId>? armorCards = null)
    {
        ItemDefinition? weaponItem = weapon.HasValue ? content.Items[weapon.Value] : null;
        ItemDefinition? armorItem = armor.HasValue ? content.Items[armor.Value] : null;
        CardBonus cards = CardsOf(
            content,
            weaponItem == null ? Array.Empty<ItemDefinitionId>() : weaponCards ?? Array.Empty<ItemDefinitionId>(),
            armorItem == null ? Array.Empty<ItemDefinitionId>() : armorCards ?? Array.Empty<ItemDefinitionId>());
        player.Weapon = weaponItem?.Equipment;
        player.SetWornWeapon(weapon);
        player.Armor = armorItem?.Equipment;
        player.CardBonus = cards;
        player.Equipment = Of(
            weaponItem,
            TableOf(content, weaponItem),
            weaponLevel,
            armorItem,
            TableOf(content, armorItem),
            armorLevel,
            cards);
    }

    /// <summary>
    ///     <see cref="Dress(PlayerEntity, ServerContent, ItemDefinitionId?, int, ItemDefinitionId?, int)" /> from the
    ///     character's settled inventory.
    /// </summary>
    public static void Dress(PlayerEntity player, ServerContent content, CharacterInventory inventory)
    {
        bool hasWeapon = inventory.TryGetRow(inventory.WornIn(EquipmentSlot.Weapon), out InventoryEntry weapon);
        bool hasArmor = inventory.TryGetRow(inventory.WornIn(EquipmentSlot.Armor), out InventoryEntry armor);
        Dress(
            player,
            content,
            hasWeapon ? weapon.Item : null,
            hasWeapon ? weapon.RefineLevel : 0,
            hasArmor ? armor.Item : null,
            hasArmor ? armor.RefineLevel : 0,
            hasWeapon ? weapon.Cards : null,
            hasArmor ? armor.Cards : null);
    }

    /// <summary>
    ///     What an inspection of the player <paramref name="entity" /> shows (Gameplay Systems §11.7): the worn weapon and
    ///     armor in the character's settled inventory, each with its refine level and cards.
    /// </summary>
    public static InspectedEquipment InspectionOf(EntityId entity, CharacterInventory inventory)
    {
        return new InspectedEquipment(
            entity,
            InspectedIn(inventory, EquipmentSlot.Weapon),
            InspectedIn(inventory, EquipmentSlot.Armor));
    }

    private static InspectedItem InspectedIn(CharacterInventory inventory, EquipmentSlot slot)
    {
        return inventory.TryGetRow(inventory.WornIn(slot), out InventoryEntry row)
            ? new InspectedItem(row.Item, row.RefineLevel, row.Cards)
            : default;
    }

    /// <summary>
    ///     The refine table of <paramref name="item" />, or null when it names none or the content lacks it.
    /// </summary>
    public static RefineTableDefinition? TableOf(ServerContent content, ItemDefinition? item)
    {
        return item?.Refine is RefineDefinitionId table
            && content.RefineTables.TryGetValue(table, out RefineTableDefinition? found)
                ? found
                : null;
    }
}
}
