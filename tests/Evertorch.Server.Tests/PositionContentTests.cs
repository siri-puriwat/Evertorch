using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Milestone 20's content as the server loads it (Content Pipeline §4; Gameplay Systems §10, §11.3, §11.5, §11.6,
///     §11.7; the equipment positions research note's vectors): the ten pieces and what they add, the light table, the
///     five cards, the drops appended last, the Quartermaster's twenty-four items, the board's thirty-two goods, and the
///     Smith's four tables.
/// </summary>
[TestFixture]
public sealed class PositionContentTests
{
    private const string TrailCap = "item.headgear.trail_cap";
    private const string GrottoCirclet = "item.headgear.grotto_circlet";
    private const string WoodenBuckler = "item.shield.wooden_buckler";
    private const string CarapaceShield = "item.shield.carapace";
    private const string TravelCape = "item.garment.travel_cape";
    private const string GloomCape = "item.garment.gloom_cape";
    private const string TrailBoots = "item.footwear.trail_boots";
    private const string GrottoBoots = "item.footwear.grotto_boots";
    private const string TravelerCharm = "item.accessory.traveler_charm";
    private const string GrottoCharm = "item.accessory.grotto_charm";
    private const string Poise = "item.card.hollow_wisp_poise";
    private const string LightTable = "refine.light_armor";
    private const uint Revision = 5;

    private static ServerContent Content => new TestServer().Content;

    private static ItemDefinition Item(string id)
    {
        return Content.Items[new ItemDefinitionId(id)];
    }

    // Character 1 wears each piece in its position at its level, as earlier commits would have left it, and enters.
    private static PlayerEntity EnterWearing(params (string Item, EquipmentSlot Slot, int Level, string[] Cards)[] worn)
    {
        var server = new TestServer();
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        foreach ((string item, EquipmentSlot slot, int level, string[] cards) in worn)
        {
            server.Store.GiveItems(1, item, 1, 1, Revision);
            long row = server.Store.Stored(1).Items.Max(stored => stored.Id);
            server.Store.Refine(1, row, level);
            server.Store.Card(1, row, cards);
            server.Store.Wear(1, row, slot.ToString());
        }

        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        return server.PlayerOf(player);
    }

    private static (string, EquipmentSlot, int, string[]) Worn(string item, EquipmentSlot slot, int level = 0)
    {
        return (item, slot, level, new string[0]);
    }

    [Test]
    public void Drops_OfTheSixMonsters_EndWithTheirNewEntries_ThePieceBeforeTheCard()
    {
        (string Monster, string[] Last)[] tables =
        {
            ("monster.forest_crawler", new[] { "item.card.forest_crawler_stride" }),
            ("monster.spark_wisp", new[] { "item.card.spark_wisp_insight" }),
            ("monster.grotto_crawler", new[] { CarapaceShield, GrottoBoots, "item.card.grotto_crawler_bulwark" }),
            ("monster.gloom_wisp", new[] { GloomCape, "item.card.gloom_wisp_veil" }),
            ("monster.hollow_wisp", new[] { GrottoCirclet, Poise }),
            ("monster.barrow_crawler", new[] { GrottoCharm })
        };

        foreach ((string monster, string[] last) in tables)
        {
            IReadOnlyList<MonsterDrop> drops = Content.Monsters[new MonsterDefinitionId(monster)].Drops;
            Assert.That(
                drops.Skip(drops.Count - last.Length).Select(drop => drop.Item.Value),
                Is.EqualTo(last),
                monster);
        }
    }

    [Test]
    public void LightTable_IsSafeToFour_ThenLowersAPieceOneLevel_TwoDefenseALevel()
    {
        RefineTableDefinition table = Content.RefineTables[new RefineDefinitionId(LightTable)];

        Assert.That(
            table.Steps.Select(step => step.Chance),
            Is.EqualTo(new[] { 100, 100, 100, 100, 60, 50, 40, 30, 20, 10 }));
        Assert.That(table.Steps.Select(step => step.Bonus), Is.EqualTo(Enumerable.Range(1, 10).Select(n => 2 * n)));
        Assert.That(table.Steps.Select(step => step.Coins), Is.All.EqualTo(30));
        Assert.That(
            (table.Material.Value, table.MaterialQuantity, table.OnFailure, table.DowngradeBy),
            Is.EqualTo(("item.material.wardstone", 1, RefineFailure.Downgrade, 1)));
    }

    [Test]
    public void Pieces_AddWhatTheirTableSays()
    {
        (string Id, ItemType Type, int Defense, PrimaryStats Bonus, int Penalty, int Weight, int Sell)[] pieces =
        {
            (TrailCap, ItemType.Headgear, 2, default, 0, 20, 15),
            (GrottoCirclet, ItemType.Headgear, 4, new PrimaryStats(0, 0, 0, 1, 0, 0), 0, 25, 30),
            (WoodenBuckler, ItemType.Shield, 6, default, 5, 40, 25),
            (CarapaceShield, ItemType.Shield, 10, default, 5, 60, 50),
            (TravelCape, ItemType.Garment, 2, default, 0, 20, 15),
            (GloomCape, ItemType.Garment, 4, new PrimaryStats(0, 0, 1, 0, 0, 0), 0, 25, 30),
            (TrailBoots, ItemType.Footwear, 2, default, 0, 30, 15),
            (GrottoBoots, ItemType.Footwear, 4, new PrimaryStats(0, 1, 0, 0, 0, 0), 0, 35, 30),
            (TravelerCharm, ItemType.Accessory, 0, new PrimaryStats(0, 0, 0, 0, 1, 0), 0, 5, 20),
            (GrottoCharm, ItemType.Accessory, 0, new PrimaryStats(1, 0, 0, 1, 0, 0), 0, 5, 40)
        };

        foreach ((string id, ItemType type, int defense, PrimaryStats bonus, int penalty, int weight, int sell) in
                 pieces)
        {
            ItemDefinition item = Item(id);
            Assert.That(
                (item.Type, item.StackLimit, item.Slots, item.Weight, item.SellPrice),
                Is.EqualTo((type, 1, 1, weight, sell)),
                id);
            Assert.That(
                (item.Equipment!.Defense, item.Equipment.Bonus, item.Equipment.AttackSpeedPenalty),
                Is.EqualTo((defense, bonus, penalty)),
                id);
            Assert.That(
                item.Refine?.Value,
                Is.EqualTo(type == ItemType.Accessory ? null : LightTable),
                $"{id}: only the charms name no table");
        }
    }

    // The equipment positions research note's sets: the town's four light pieces add 12, the dungeon's 22, and four
    // light pieces at +10 add 80 more, against 72 for one body armor at +10.
    [Test]
    public void Sets_OfTheLightPieces_AddTheirResearchVectors()
    {
        PlayerEntity basic = EnterWearing(
            Worn(TrailCap, EquipmentSlot.Headgear),
            Worn(WoodenBuckler, EquipmentSlot.Shield),
            Worn(TravelCape, EquipmentSlot.Garment),
            Worn(TrailBoots, EquipmentSlot.Footwear));
        PlayerEntity dungeon = EnterWearing(
            Worn(GrottoCirclet, EquipmentSlot.Headgear),
            Worn(CarapaceShield, EquipmentSlot.Shield),
            Worn(GloomCape, EquipmentSlot.Garment),
            Worn(GrottoBoots, EquipmentSlot.Footwear));
        PlayerEntity refined = EnterWearing(
            Worn(TrailCap, EquipmentSlot.Headgear, 10),
            Worn(WoodenBuckler, EquipmentSlot.Shield, 10),
            Worn(TravelCape, EquipmentSlot.Garment, 10),
            Worn(TrailBoots, EquipmentSlot.Footwear, 10));
        PlayerEntity armor = EnterWearing(Worn("item.armor.cloth", EquipmentSlot.Armor, 10));

        Assert.That(
            (basic.Equipment.HardDefense, dungeon.Equipment.HardDefense, refined.Equipment.HardDefense),
            Is.EqualTo((12, 22, 92)));
        Assert.That(armor.Equipment.HardDefense - 8, Is.EqualTo(72), "the armor table's +10");
        Assert.That(dungeon.Equipment.Bonus, Is.EqualTo(new PrimaryStats(0, 1, 1, 1, 0, 0)), "AGI, VIT, and INT");
        Assert.That(
            (basic.Equipment.AttackSpeedPenalty, dungeon.Equipment.AttackSpeedPenalty),
            Is.EqualTo((5, 5)),
            "each shield's 5");
    }

    // Two charms with a Poise card each add DEX +2 and LUK +2 beside the charms' own (Gameplay Systems §11.7).
    // Network Protocol §6 and §9: the Smith holds four tables in the order of their IDs, the light one second with its
    // eight pieces, and the fees of thirteen cards in one part of 414 bytes; the board's thirty-two goods take three
    // messages.
    [Test]
    public void Smith_HoldsFourTables_TheFeesOfThirteenCards_AndTheBoardThirtyTwoGoods()
    {
        ServerContent content = Content;
        NpcDefinition smith = content.Npcs[new NpcDefinitionId("npc.smith")];

        IReadOnlyList<RefineTable> tables = NpcServicesBuilder.BuildRefineTables(new EntityId(2), smith, content);
        CardRemovalFees fees = NpcServicesBuilder.BuildCardRemovalFees(new EntityId(2), smith, content).Single();
        MarketBoardEntry[] goods = content.Items.Values
            .Where(item => item.Market != null)
            .Select(item => new MarketBoardEntry(item.Id, (uint)item.Market!.BasePrice, 0))
            .ToArray();

        Assert.That(tables.Select(table => table.OnFailure), Is.EqualTo(new[]
        {
            RefineFailure.Destroy, RefineFailure.Downgrade, RefineFailure.Destroy, RefineFailure.Destroy
        }));
        Assert.That(
            tables[1].Items.Select(item => item.Value),
            Is.EqualTo(new[]
            {
                GrottoBoots, TrailBoots, GloomCape, TravelCape, GrottoCirclet, TrailCap, CarapaceShield, WoodenBuckler
            }));
        Assert.That((fees.Entries.Count, fees.GetEncodedLength()), Is.EqualTo((13, 414)));
        Assert.That(
            MarketBoard.CreateParts(goods).Select(part => part.Entries.Count),
            Is.EqualTo(new[] { 13, 13, 6 }));
    }

    [Test]
    public void TwoPoiseCards_OnTwoCharms_AddTwice()
    {
        PlayerEntity player = EnterWearing(
            (TravelerCharm, EquipmentSlot.Accessory1, 0, new[] { Poise }),
            (GrottoCharm, EquipmentSlot.Accessory2, 0, new[] { Poise }));

        Assert.That(player.CardBonus.Stats, Is.EqualTo(new PrimaryStats(0, 0, 0, 0, 2, 2)));
        Assert.That(player.Equipment.Bonus, Is.EqualTo(new PrimaryStats(1, 0, 0, 1, 1, 0)));
    }
}
}
