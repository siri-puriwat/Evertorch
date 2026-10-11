using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;

namespace Evertorch.Server
{
/// <summary>
///     What a player's worn weapon and armor add in combat (Gameplay Systems §11.1): the weapon's attack, its refine
///     bonus as a flat attack, a staff's refine bonus to magic attack too, and the armor's defense with its refine bonus
///     as hard defense. Combat reads it at each hit and the sheet shows it, so the two never disagree.
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

    /// <summary>The worn weapon's refine bonus, added after its draw.</summary>
    public int FlatAttack { get; }

    /// <summary>A worn staff's refine bonus to magic attack.</summary>
    public int MagicAttack { get; }

    /// <summary>The worn armor's defense with its refine bonus.</summary>
    public int HardDefense { get; }

    /// <summary>
    ///     What the given worn items add, each at its refine level; a level past its table counts the table's last bonus,
    ///     and an item without a table counts none.
    /// </summary>
    public static WornEquipment Of(
        ItemDefinition? weapon,
        RefineTableDefinition? weaponTable,
        int weaponLevel,
        ItemDefinition? armor,
        RefineTableDefinition? armorTable,
        int armorLevel)
    {
        int weaponBonus = weapon == null ? 0 : RefineRules.BonusAt(weaponTable, weaponLevel);
        bool isStaff = weapon?.Equipment?.WeaponType == WeaponType.Staff;
        return new WornEquipment(
            weapon?.Equipment?.Attack ?? 0,
            weaponBonus,
            isStaff ? weaponBonus : 0,
            armor == null ? 0 : (armor.Equipment?.Defense ?? 0) + RefineRules.BonusAt(armorTable, armorLevel));
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
        int armorLevel)
    {
        ItemDefinition? weaponItem = weapon.HasValue ? content.Items[weapon.Value] : null;
        ItemDefinition? armorItem = armor.HasValue ? content.Items[armor.Value] : null;
        player.Weapon = weaponItem?.Equipment;
        player.SetWornWeapon(weapon);
        player.Armor = armorItem?.Equipment;
        player.Equipment = Of(
            weaponItem,
            TableOf(content, weaponItem),
            weaponLevel,
            armorItem,
            TableOf(content, armorItem),
            armorLevel);
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
            hasArmor ? armor.RefineLevel : 0);
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
