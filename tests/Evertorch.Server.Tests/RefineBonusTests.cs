using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     What a refine level adds (Gameplay Systems §11.1): a weapon's bonus as a flat attack after its draw, a staff's to
///     magic attack too, armor's to hard defense; one helper sets it from the worn rows at the load and at every change
///     of what is worn; a level past its table counts the table's last bonus, an item without a table none, and the load
///     logs either.
/// </summary>
[TestFixture]
public sealed class RefineBonusTests
{
    private const string Sword = "item.weapon.training_sword";
    private const string WeaponSlot = "Weapon";
    private static readonly ServerContent Content = ServerContentLoader.Load(PackageFixture.BuildRepositoryPackage());

    private static ItemDefinition Item(string id)
    {
        return Content.Items[new ItemDefinitionId(id)];
    }

    private static (int, int, int, int) Parts(WornEquipment worn)
    {
        return (worn.WeaponAttack, worn.FlatAttack, worn.MagicAttack, worn.HardDefense);
    }

    private static WornEquipment Of(string? weapon, int weaponLevel, string? armor, int armorLevel)
    {
        ItemDefinition? weaponItem = weapon == null ? null : Item(weapon);
        ItemDefinition? armorItem = armor == null ? null : Item(armor);
        return WornEquipment.Of(
            weaponItem,
            WornEquipment.TableOf(Content, weaponItem),
            weaponLevel,
            armorItem,
            WornEquipment.TableOf(Content, armorItem),
            armorLevel);
    }

    private static ConnectionId EnterWearing(TestServer server, string item, int level)
    {
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        server.Store.GiveItems(1, item, 1, 1, 0);
        long row = server.Store.Stored(1).Items.Single().Id;
        server.Store.Refine(1, row, level);
        server.Store.Wear(1, row, WeaponSlot);
        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        return player;
    }

    [Test]
    public void Load_OfASwordAtSeven_CountsItsBonus_AndLogsNothing()
    {
        var server = new TestServer();

        ConnectionId player = EnterWearing(server, Sword, 7);

        Assert.That(Parts(server.PlayerOf(player).Equipment), Is.EqualTo((20, 14, 0, 0)));
        Assert.That(server.LifetimeLog.Entries.Where(entry => entry.EventId.Id == 1035), Is.Empty);
    }

    [Test]
    public void Load_OfASwordPastItsTable_CountsTheLastBonus_AndLogsIt()
    {
        var server = new TestServer();

        ConnectionId player = EnterWearing(server, Sword, 12);

        Assert.That(Parts(server.PlayerOf(player).Equipment), Is.EqualTo((20, 20, 0, 0)));
        IReadOnlyDictionary<string, object?> fields =
            server.LifetimeLog.Entries.Single(entry => entry.EventId.Id == 1035).Fields;
        Assert.That(
            (fields["Item"]?.ToString(), fields["Level"], fields["Last"]),
            Is.EqualTo(((string?)Sword, (object?)12, (object?)10)));
    }

    [Test]
    public void Of_AWeaponAndAnArmor_AddTheirAttackAndDefense_WithTheirRefineBonuses()
    {
        Assert.That(Parts(Of(null, 0, null, 0)), Is.EqualTo((0, 0, 0, 0)), "nothing worn");
        Assert.That(Parts(Of(Sword, 0, "item.armor.cloth", 0)), Is.EqualTo((20, 0, 0, 8)), "at +0");
        Assert.That(Parts(Of(Sword, 7, "item.armor.cloth", 4)), Is.EqualTo((20, 14, 0, 28)), "+7 and +4");
        Assert.That(Parts(Of("item.weapon.ash_staff", 6, null, 0)), Is.EqualTo((15, 18, 18, 0)), "a staff's to magic");
        Assert.That(Parts(Of(null, 0, "item.armor.monarch_mantle", 10)), Is.EqualTo((0, 0, 0, 94)),
            "the Mantle at +10");
        Assert.That(Parts(Of(Sword, 12, null, 0)), Is.EqualTo((20, 20, 0, 0)), "past its table, the last bonus");
    }

    [Test]
    public void Of_AnItemWithoutATable_CountsNoBonus()
    {
        var worn = WornEquipment.Of(Item(Sword), null, 7, Item("item.armor.cloth"), null, 4);

        Assert.That(Parts(worn), Is.EqualTo((20, 0, 0, 8)));
    }

    // The equipment research note's vector with every draw at its lowest: the sword's 32 against the slime becomes 46
    // at +7, its flat 14 added after the draw; the equip's commit carries the row's level.
    [Test]
    public void Sword_RefinedToSevenAndEquipped_HitsTheSlimeForFourteenMore()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        rig.Slime.CurrentHealth = 10_000;
        rig.Attack(rig.Slime.Id);
        rig.Server.Tick(40);
        ItemDropEntity drop = rig.Server.World.SpawnItemDrop(
            rig.Map,
            new ItemDefinitionId(Sword),
            1,
            rig.Entity.Position,
            rig.Server.CurrentTick,
            long.MaxValue,
            default);
        rig.Server.Tick();
        rig.Server.SendPickup(rig.Player, drop.Id, 100);
        CharacterSession character = rig.Server.SessionOf(rig.Player).Character!;
        rig.Server.TickUntil(() => character.Inventory.Rows.Count == 1);
        long sword = character.Inventory.Rows.Single().InventoryItem;
        rig.Server.Store.Refine(1, sword, 7);

        rig.Server.SendEquip(rig.Player, sword, 101);
        rig.Server.TickUntil(() => rig.Entity.Weapon != null);
        rig.Server.Tick(60);

        Assert.That(Parts(rig.Entity.Equipment), Is.EqualTo((20, 14, 0, 0)));
        List<Damage> damages = rig.Damages();
        Assert.That((damages.First().Amount, damages.Last().Amount), Is.EqualTo((13u, 46u)), "unarmed, then armed");
    }
}
}
