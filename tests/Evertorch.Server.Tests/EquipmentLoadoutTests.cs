using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     What the eight positions add (Gameplay Systems §2, §7, §9.1, §11.1, §11.7; the equipment positions research note's
///     vectors): every piece's defense with its refine bonus and its statistic bonuses, a shield's attack-speed penalty
///     on top of the weapon's or the unarmed one, the cards of every worn row once each, their resistances capped at
///     100, a converter kept through any change but the weapon's, and maxima that never heal.
/// </summary>
[TestFixture]
public sealed class EquipmentLoadoutTests
{
    private const string Sword = "item.weapon.training_sword";
    private const string Staff = "item.weapon.training_staff";
    private const string Cloth = "item.armor.cloth";
    private const string Royal = "item.card.slime_monarch";
    private const string FireConverter = "item.consumable.fire_converter";
    private const uint Revision = 5;

    private static readonly Lazy<ServerContent> Gear = new(() => ServerContentLoader.Load(GearPackage.Build()));

    // The eight positions worn at once, a card in every piece but the weapon, two Poise cards among them.
    private static readonly Piece[] FullLoadout =
    {
        new(Sword, EquipmentSlot.Weapon), new(Cloth, EquipmentSlot.Armor, Royal),
        new(GearPackage.Cap, EquipmentSlot.Headgear, GearPackage.Insight),
        new(GearPackage.Buckler, EquipmentSlot.Shield, GearPackage.Bulwark),
        new(GearPackage.Cape, EquipmentSlot.Garment, GearPackage.Veil),
        new(GearPackage.Boots, EquipmentSlot.Footwear, GearPackage.Stride),
        new(GearPackage.Charm, EquipmentSlot.Accessory1, GearPackage.Poise),
        new(GearPackage.Bead, EquipmentSlot.Accessory2, GearPackage.Poise)
    };

    private static CharacterSession CharacterOf(TestServer server, ConnectionId player)
    {
        return server.SessionOf(player).Character!;
    }

    private static long RowOf(TestServer server, ConnectionId player, string item)
    {
        return CharacterOf(server, player).Inventory.Rows.First(row => row.Item.Value == item).InventoryItem;
    }

    // Character 1 holds each piece, worn where it says, at its level and with its cards, as earlier commits would have
    // left it, and enters the world with HP and SP held to its maxima.
    private static ConnectionId EnterWearing(TestServer server, params Piece[] pieces)
    {
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        server.Store.Edit(1, health: 10_000, spirit: 10_000);
        foreach (Piece piece in pieces)
        {
            server.Store.GiveItems(1, piece.Item, 1, 1, Revision);
            long row = server.Store.Stored(1).Items.Max(stored => stored.Id);
            server.Store.Card(1, row, piece.Cards);
            server.Store.Refine(1, row, piece.Level);
            if (piece.Slot != EquipmentSlot.None)
            {
                server.Store.Wear(1, row, piece.Slot.ToString());
            }
        }

        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        return player;
    }

    private static CharacterSheet SheetOf(TestServer server, ConnectionId player)
    {
        return server.Builds.SheetOf(server.PlayerOf(player));
    }

    private static void Settle(TestServer server, ConnectionId player)
    {
        server.TickUntil(() => CharacterOf(server, player).Operation == null);
    }

    [TestCase(null, 148, TestName = "AttackSpeed_Unarmed_WithAShield_Is148")]
    [TestCase(Sword, 142, TestName = "AttackSpeed_OfTheSword_WithAShield_Is142")]
    [TestCase(Staff, 138, TestName = "AttackSpeed_OfTheStaff_WithAShield_Is138")]
    public void AttackSpeed_WithAShield_AddsItsPenaltyToTheWeaponsOrTheUnarmedOne(string? weapon, int attackSpeed)
    {
        var server = new TestServer(content: Gear.Value);
        var pieces = new List<Piece> { new(GearPackage.Buckler, EquipmentSlot.Shield) };
        if (weapon != null)
        {
            pieces.Add(new Piece(weapon, EquipmentSlot.Weapon));
        }

        ConnectionId player = EnterWearing(server, pieces.ToArray());

        Assert.That(server.PlayerOf(player).Stats.AttackSpeed, Is.EqualTo(attackSpeed));
        Assert.That(server.PlayerOf(player).Equipment.AttackSpeedPenalty, Is.EqualTo(5));
    }

    private readonly struct Piece
    {
        public Piece(string item, EquipmentSlot slot, params string[] cards)
            : this(item, slot, 0, cards)
        {
        }

        public Piece(string item, EquipmentSlot slot, int level, params string[] cards)
        {
            Item = item;
            Slot = slot;
            Level = level;
            Cards = cards;
        }

        public string Item { get; }

        public EquipmentSlot Slot { get; }

        public int Level { get; }

        public string[] Cards { get; }
    }

    // The armor's card alone gives an element; a garment's card beside it resists, never sets one (Gameplay Systems
    // §9.1, §11.7).
    [Test]
    public void ArmorElement_ComesFromTheBodyArmorsCard_WhateverElseIsWorn()
    {
        var server = new TestServer(content: Gear.Value);

        ConnectionId player = EnterWearing(
            server,
            new Piece(Cloth, EquipmentSlot.Armor, "item.card.grotto_crawler"),
            new Piece(GearPackage.Cape, EquipmentSlot.Garment, GearPackage.Veil),
            new Piece(GearPackage.Charm, EquipmentSlot.Accessory1, GearPackage.Ward));

        PlayerEntity entity = server.PlayerOf(player);
        Assert.That(entity.ArmorElement, Is.EqualTo(Element.Earth));
        Assert.That(
            (entity.CardBonus.ResistanceTo(Ailment.Stun), entity.CardBonus.ResistanceTo(Ailment.Blind)),
            Is.EqualTo((30, 70)));
    }

    // Unarmed, a swing every 940 ms; with a shield, every 1,040 ms. A swing that began before the equip settled keeps
    // its interval.
    [Test]
    public void AttackInterval_WithAShieldPutOn_GrowsBy100Ms_AndASwingUnderWayKeepsItsOwn()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom(), content: Gear.Value);
        rig.Slime.CurrentHealth = 10_000;
        rig.Attack(rig.Slime.Id);
        rig.Server.Tick(40);
        ItemDropEntity drop = rig.Server.World.SpawnItemDrop(
            rig.Map,
            new ItemDefinitionId(GearPackage.Buckler),
            1,
            rig.Entity.Position,
            rig.Server.CurrentTick,
            long.MaxValue,
            default);
        rig.Server.Tick();
        rig.Server.SendPickup(rig.Player, drop.Id, 100);
        rig.Server.TickUntil(() => CharacterOf(rig.Server, rig.Player).Inventory.Rows.Count == 1);
        long buckler = CharacterOf(rig.Server, rig.Player).Inventory.Rows.Single().InventoryItem;

        rig.Server.SendEquip(rig.Player, buckler, 101, EquipmentSlot.Shield);
        rig.Server.TickUntil(() => rig.Entity.Equipment.AttackSpeedPenalty > 0);
        uint settled = rig.Server.CurrentTick;
        rig.Server.Tick(60);

        List<AttackStarted> starts = rig.Starts();
        Assert.That(
            starts.Where(start => start.StartTick < settled).Select(start => start.Timing.Interval),
            Is.Not.Empty.And.All.EqualTo(TimeSpan.FromMilliseconds(940)));
        Assert.That(
            starts.Where(start => start.StartTick >= settled).Select(start => start.Timing.Interval).ToArray(),
            Has.Length.GreaterThanOrEqualTo(2).And.All.EqualTo(TimeSpan.FromMilliseconds(1040)));
    }

    [Test]
    public void Cards_OfEveryWornRow_CountOnceEach_AndTwoPoiseCardsAddTwice()
    {
        var server = new TestServer(content: Gear.Value);

        ConnectionId player = EnterWearing(server, FullLoadout);

        PlayerEntity entity = server.PlayerOf(player);
        CardBonus cards = entity.CardBonus;
        Assert.That(cards.Stats, Is.EqualTo(new PrimaryStats(0, 1, 0, 1, 2, 2)), "Insight, Stride, and two Poise");
        Assert.That((cards.MaxHp, cards.MaxSp, cards.Defense), Is.EqualTo((50, 15, 5)), "Royal and Stride; Insight");
        Assert.That(cards.BossDamageReductionPercent, Is.EqualTo(30), "Royal's 25 and Bulwark's 5");
        Assert.That(cards.ReductionFrom(Element.Shadow), Is.EqualTo(10), "Veil");
        Assert.That(
            (cards.ResistanceTo(Ailment.Blind), cards.ResistanceTo(Ailment.Freeze)),
            Is.EqualTo((10, 50)),
            "Veil's on a garment and Royal's on the armor");
        CharacterSheet sheet = SheetOf(server, player);
        Assert.That(
            new[]
            {
                sheet.EquipmentStr, sheet.EquipmentAgi, sheet.EquipmentVit, sheet.EquipmentInt, sheet.EquipmentDex,
                sheet.EquipmentLuk
            },
            Is.EqualTo(new ushort[] { 0, 1, 0, 1, 3, 3 }),
            "the charm's DEX and the bead's LUK beside the cards'");
        Assert.That(sheet.EquipmentDefense, Is.EqualTo(25), "8 + 2 + 6 + 2 + 2, and Bulwark's 5");
        Assert.That(entity.ArmorElement, Is.EqualTo(Element.Neutral), "no card here gives an element");
    }

    [Test]
    public void Converter_IsKeptByAChangeOfAnyPositionButTheWeapons()
    {
        var server = new TestServer(content: Gear.Value);
        ConnectionId player = EnterWearing(
            server,
            new Piece(Sword, EquipmentSlot.Weapon),
            new Piece(FireConverter, EquipmentSlot.None),
            new Piece(GearPackage.Cap, EquipmentSlot.None),
            new Piece(GearPackage.Buckler, EquipmentSlot.None));
        PlayerEntity entity = server.PlayerOf(player);
        server.SendUseItem(player, RowOf(server, player, FireConverter), 1);
        Settle(server, player);
        Assert.That(entity.AttackElement, Is.EqualTo(Element.Fire), "the converter's");

        server.SendEquip(player, RowOf(server, player, GearPackage.Cap), 2, EquipmentSlot.Headgear);
        Settle(server, player);
        server.SendEquip(player, RowOf(server, player, GearPackage.Buckler), 3, EquipmentSlot.Shield);
        Settle(server, player);
        server.SendUnequip(player, EquipmentSlot.Headgear, 4);
        Settle(server, player);
        Element kept = entity.AttackElement;
        server.SendUnequip(player, EquipmentSlot.Weapon, 5);
        Settle(server, player);

        Assert.That((kept, entity.AttackElement), Is.EqualTo((Element.Fire, Element.Neutral)));
    }

    // The armor research vectors' plain basic set adds 12; each refine level adds its table's bonus, and the weapon adds
    // no defense.
    [Test]
    public void HardDefense_OfEveryPieceButTheWeapon_AddsUp_WithEachRefineBonus()
    {
        var plain = new TestServer(content: Gear.Value);
        var refined = new TestServer(content: Gear.Value);
        Piece[] basic =
        {
            new(GearPackage.Cap, EquipmentSlot.Headgear), new(GearPackage.Buckler, EquipmentSlot.Shield),
            new(GearPackage.Cape, EquipmentSlot.Garment), new(GearPackage.Boots, EquipmentSlot.Footwear)
        };

        ConnectionId first = EnterWearing(plain, basic);
        ConnectionId second = EnterWearing(
            refined,
            basic.Skip(1)
                .Append(new Piece(GearPackage.Cap, EquipmentSlot.Headgear, 4))
                .Append(new Piece(Cloth, EquipmentSlot.Armor))
                .Append(new Piece(Sword, EquipmentSlot.Weapon))
                .ToArray());

        Assert.That(plain.PlayerOf(first).Equipment.HardDefense, Is.EqualTo(12));
        Assert.That(SheetOf(plain, first).EquipmentDefense, Is.EqualTo(12));
        Assert.That(refined.PlayerOf(second).Equipment.HardDefense, Is.EqualTo(40),
            "12, the cap's +4 20, the cloth's 8");
    }

    // The pieces and cards raise AGI, INT, DEX, and LUK, and the statistics follow them, then the cards' maxima; a
    // character of those statistics, unarmed and bare, is the reference.
    [Test]
    public void Load_OfAFullLoadout_CountsEveryPositionFromTheFirstTick()
    {
        var server = new TestServer(content: Gear.Value);
        var raised = new PrimaryStats(5, 6, 5, 6, 8, 8);

        ConnectionId player = EnterWearing(server, FullLoadout);

        PlayerEntity entity = server.PlayerOf(player);
        DerivedStats reference = new CharacterStats(new RenewalCharacterRules()).Calculate(
            server.Content.Jobs[entity.Job],
            entity.Level,
            raised);
        Assert.That(entity.EffectivePrimary, Is.EqualTo(raised));
        Assert.That(
            entity.Stats.AttackSpeed,
            Is.EqualTo(reference.AttackSpeed - 11),
            "the sword's 50 and the shield's 5 against the unarmed 44");
        Assert.That(
            (entity.MaxHealth, entity.MaxSpirit),
            Is.EqualTo((reference.MaxHp + 50, reference.MaxSp + 15)),
            "Royal's and Stride's HP, Insight's SP");
        Assert.That((entity.CurrentHealth, entity.CurrentSpirit), Is.EqualTo((entity.MaxHealth, entity.MaxSpirit)));
    }

    // Equipping never heals (Gameplay Systems §2): a maximum that falls holds HP to it, and one that rises leaves HP.
    [Test]
    public void Maximum_RaisedByAPiecesCard_HoldsHpWhenItFalls_AndNeverHealsWhenItRises()
    {
        var server = new TestServer(content: Gear.Value);
        ConnectionId player =
            EnterWearing(server, new Piece(GearPackage.Boots, EquipmentSlot.Footwear, GearPackage.Stride));
        PlayerEntity entity = server.PlayerOf(player);
        int worn = entity.MaxHealth;
        long boots = RowOf(server, player, GearPackage.Boots);

        server.SendUnequip(player, EquipmentSlot.Footwear, 1);
        Settle(server, player);
        (int Maximum, int Current) off = (entity.MaxHealth, entity.CurrentHealth);
        server.SendEquip(player, boots, 2, EquipmentSlot.Footwear);
        Settle(server, player);

        Assert.That(off, Is.EqualTo((worn - 10, worn - 10)));
        Assert.That((entity.MaxHealth, entity.CurrentHealth), Is.EqualTo((worn, worn - 10)));
    }

    // Two Ward cards and a Veil sum 130 against blindness, held to 100: the sheet's row shows all of it kept out.
    [Test]
    public void Resistance_OfTheCardsOfEveryPieceButTheWeapon_IsCappedAt100()
    {
        var server = new TestServer(content: Gear.Value);

        ConnectionId player = EnterWearing(
            server,
            new Piece(GearPackage.Cape, EquipmentSlot.Garment, GearPackage.Veil),
            new Piece(GearPackage.Charm, EquipmentSlot.Accessory1, GearPackage.Ward),
            new Piece(GearPackage.Bead, EquipmentSlot.Accessory2, GearPackage.Ward));

        Assert.That(server.PlayerOf(player).CardBonus.ResistanceTo(Ailment.Blind), Is.EqualTo(130));
        Assert.That(SheetOf(server, player).BlindResistance, Is.EqualTo(100));
    }
}
}
