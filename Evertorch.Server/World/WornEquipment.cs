using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;

namespace Evertorch.Server
{
/// <summary>
///     What a player's worn pieces add (Gameplay Systems §2, §11.1), the eight positions visited once each in position
///     order: the weapon's attack, its refine bonus and the worn cards' attack as a flat attack, a staff's refine bonus
///     and the cards' magic attack to magic attack, every other piece's defense with its refine bonus and the cards'
///     defense as hard defense, every piece's statistic bonuses, and every other piece's attack-speed penalty, a
///     shield's. Combat and the statistics read it and the sheet shows it, so the three never disagree.
/// </summary>
public sealed class WornEquipment
{
    public static readonly WornEquipment None = new(0, 0, 0, 0);

    public WornEquipment(
        int weaponAttack,
        int flatAttack,
        int magicAttack,
        int hardDefense,
        PrimaryStats bonus = default,
        int attackSpeedPenalty = 0)
    {
        WeaponAttack = weaponAttack;
        FlatAttack = flatAttack;
        MagicAttack = magicAttack;
        HardDefense = hardDefense;
        Bonus = bonus;
        AttackSpeedPenalty = attackSpeedPenalty;
    }

    /// <summary>The worn weapon's own attack, which its draw varies; 0 unarmed.</summary>
    public int WeaponAttack { get; }

    /// <summary>The worn weapon's refine bonus and the worn cards' attack, added after the weapon's draw.</summary>
    public int FlatAttack { get; }

    /// <summary>A worn staff's refine bonus and the worn cards' magic attack, with any weapon.</summary>
    public int MagicAttack { get; }

    /// <summary>Every worn piece but the weapon's defense with its refine bonus, and the worn cards' defense.</summary>
    public int HardDefense { get; }

    /// <summary>Every worn piece's statistic bonuses, summed; the cards' are the player's card bonus.</summary>
    public PrimaryStats Bonus { get; }

    /// <summary>
    ///     What every worn piece but the weapon adds to the weapon's attack-speed penalty, or to the job's unarmed one: a
    ///     shield's (Gameplay Systems §7).
    /// </summary>
    public int AttackSpeedPenalty { get; }

    /// <summary>
    ///     What the given worn pieces add, each at its refine level, with their cards; a level past its table counts the
    ///     table's last bonus, and an item without a table counts none.
    /// </summary>
    public static WornEquipment Of(IReadOnlyList<WornPiece> pieces, CardBonus? cards = null)
    {
        CardBonus added = cards ?? CardBonus.None;
        int weaponAttack = 0;
        int weaponBonus = 0;
        int staffBonus = 0;
        int defense = 0;
        int penalty = 0;
        PrimaryStats bonus = default;
        foreach (WornPiece piece in pieces)
        {
            ItemEquipment? equipment = piece.Item.Equipment;
            int refineBonus = RefineRules.BonusAt(piece.Table, piece.RefineLevel);
            if (piece.Slot == EquipmentSlot.Weapon)
            {
                weaponAttack = equipment?.Attack ?? 0;
                weaponBonus = refineBonus;
                staffBonus = equipment?.WeaponType == WeaponType.Staff ? refineBonus : 0;
            }
            else
            {
                defense += (equipment?.Defense ?? 0) + refineBonus;
                penalty += equipment?.AttackSpeedPenalty ?? 0;
            }

            bonus = CharacterStats.Plus(bonus, equipment?.Bonus);
        }

        return new WornEquipment(
            weaponAttack,
            weaponBonus + added.Attack,
            staffBonus + added.MagicAttack,
            defense + added.Defense,
            bonus,
            penalty);
    }

    /// <summary>
    ///     What the cards in the given worn rows add, summed in the rows' order, so the body armor's first card that gives
    ///     an element gives its wearer's; a card the content no longer holds as a card adds nothing.
    /// </summary>
    public static CardBonus CardsOf(ServerContent content, IEnumerable<IReadOnlyList<ItemDefinitionId>> rows)
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
    ///     The one place a player's worn pieces are set (Gameplay Systems §11.1): at the load and at every change of what
    ///     is worn. <paramref name="worn" /> holds the worn rows in position order; the pieces' values, the weapon seen in
    ///     hand, the cards, and what they add follow them.
    /// </summary>
    public static void Dress(PlayerEntity player, ServerContent content, IReadOnlyList<InventoryEntry> worn)
    {
        var pieces = new List<WornPiece>(worn.Count);
        var cards = new List<IReadOnlyList<ItemDefinitionId>>(worn.Count);
        ItemDefinitionId? weapon = null;
        ItemEquipment? weaponEquipment = null;
        ItemEquipment? armorEquipment = null;
        foreach (InventoryEntry row in worn)
        {
            ItemDefinition item = content.Items[row.Item];
            pieces.Add(new WornPiece(row.Slot, item, TableOf(content, item), row.RefineLevel));
            cards.Add(row.Cards);
            if (row.Slot == EquipmentSlot.Weapon)
            {
                weapon = row.Item;
                weaponEquipment = item.Equipment;
            }
            else if (row.Slot == EquipmentSlot.Armor)
            {
                armorEquipment = item.Equipment;
            }
        }

        CardBonus bonus = CardsOf(content, cards);
        player.Weapon = weaponEquipment;
        player.SetWornWeapon(weapon);
        player.Armor = armorEquipment;
        player.CardBonus = bonus;
        player.Equipment = Of(pieces, bonus);
    }

    /// <summary>
    ///     <see cref="Dress(PlayerEntity, ServerContent, IReadOnlyList{InventoryEntry})" /> from the character's settled
    ///     inventory.
    /// </summary>
    public static void Dress(PlayerEntity player, ServerContent content, CharacterInventory inventory)
    {
        Dress(player, content, WornRowsOf(inventory));
    }

    /// <summary>The rows <paramref name="inventory" /> wears, in position order.</summary>
    public static IReadOnlyList<InventoryEntry> WornRowsOf(CharacterInventory inventory)
    {
        var worn = new List<InventoryEntry>(EquipmentPositions.All.Count);
        foreach (EquipmentSlot slot in EquipmentPositions.All)
        {
            if (inventory.TryGetRow(inventory.WornIn(slot), out InventoryEntry row))
            {
                worn.Add(row);
            }
        }

        return worn;
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
