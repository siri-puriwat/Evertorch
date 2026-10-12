using System;
using System.Collections.Generic;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The feedback line for a committed purchase or sale, told apart by which way the coins and the units moved
///     (Prototype Content
///     §2), in words composed from display names and the committed values.
/// </summary>
[TestFixture]
public sealed class ShopMessagesTests
{
    private static readonly ItemDefinitionId Sword = new("item.weapon.training_sword");
    private static readonly ItemDefinitionId Gel = new("item.material.slime_gel");
    private static readonly ItemDefinitionId Potion = new("item.consumable.minor_health");
    private static readonly ItemDefinitionId IronSword = new("item.weapon.iron_sword");
    private static readonly ItemDefinitionId Keen = new("item.card.forest_crawler");
    private static readonly ItemDefinitionId Bane = new("item.card.gloom_wisp");

    private static ClientContent Content()
    {
        var items = new Dictionary<ItemDefinitionId, ClientItem>
        {
            [Sword] = new(Sword, "Training Sword", ItemType.Weapon, "pickup_training_sword", "sword"),
            [Gel] = new(Gel, "Slime Gel", ItemType.Material, "pickup_slime_gel", "gel"),
            [Potion] = new(Potion, "Minor Health Potion", ItemType.Consumable, "pickup_minor_health", "potion"),
            [IronSword] = new(IronSword, "Iron Sword", ItemType.Weapon, "pickup_iron_sword", "sword", slots: 2),
            [Keen] = Card(Keen, "Forest Crawler Card", "Keen"),
            [Bane] = Card(Bane, "Gloom Wisp Card", "Bane")
        };
        return new ClientContent(
            "0000000000000000",
            new Dictionary<MapDefinitionId, ClientMap>(),
            new Dictionary<JobDefinitionId, ClientJob>(),
            new Dictionary<MonsterDefinitionId, ClientMonster>(),
            items,
            new Dictionary<SkillDefinitionId, ClientSkill>(),
            new Dictionary<StatusDefinitionId, ClientStatusEffect>());
    }

    private static ClientItem Card(ItemDefinitionId id, string name, string prefix)
    {
        return new ClientItem(
            id,
            name,
            ItemType.Card,
            "pickup_slime_gel",
            "item_slime_gel",
            stackLimit: 999,
            fits: EquipmentSlot.Weapon,
            prefix: prefix,
            description: "Attack +8.");
    }

    private static InventoryDelta Delta(uint before, uint after, params (ItemDefinitionId Item, long Units)[] moved)
    {
        var units = new Dictionary<ItemDefinitionId, long>();
        foreach ((ItemDefinitionId item, long count) in moved)
        {
            units[item] = count;
        }

        return new InventoryDelta(before, after, units);
    }

    // Prototype Content §2: "+7 Keen Bane Iron Sword [2]", a repeat as Double, Triple, or Quadruple in the order the
    // cards first appear, and the slots of an item that has them.
    [TestCase(0, new string[0], "Iron Sword [2]")]
    [TestCase(7, new[] { "item.card.forest_crawler", "item.card.gloom_wisp" }, "+7 Keen Bane Iron Sword [2]")]
    [TestCase(0, new[] { "item.card.forest_crawler", "item.card.forest_crawler" }, "Double Keen Iron Sword [2]")]
    [TestCase(
        10,
        new[]
        {
            "item.card.gloom_wisp", "item.card.forest_crawler", "item.card.gloom_wisp", "item.card.gloom_wisp"
        },
        "+10 Triple Bane Keen Iron Sword [2]")]
    [TestCase(
        0,
        new[]
        {
            "item.card.forest_crawler", "item.card.forest_crawler", "item.card.forest_crawler",
            "item.card.forest_crawler"
        },
        "Quadruple Keen Iron Sword [2]")]
    [TestCase(0, new[] { "item.card.retired" }, "item.card.retired Iron Sword [2]")]
    public void RowName_NamesTheLevelTheCardsAndTheSlots(int level, string[] cards, string expected)
    {
        ItemDefinitionId[] ids = Array.ConvertAll(cards, card => new ItemDefinitionId(card));

        Assert.That(ShopMessages.RowName(Content(), IronSword, (byte)level, ids), Is.EqualTo(expected));
    }

    [Test]
    public void ASaleOfACardedRow_AsksTwice_SayingItsCardsAreLost_AndItsWordsNameTheCards()
    {
        ClientContent content = Content();
        var units = new Dictionary<ItemDefinitionId, long> { [IronSword] = -1 };

        Assert.That(
            ShopMessages.SellPrompt(content, IronSword, 0, 60, new[] { Keen }),
            Is.EqualTo("Press again to sell. Its cards are lost."));
        Assert.That(
            ShopMessages.Describe(new InventoryDelta(0, 60, units, 4, new[] { Keen, Bane }), content),
            Is.EqualTo("Sold +4 Keen Bane Iron Sword [2] for 60 coins."));
        Assert.That(ShopMessages.ItemName(content, IronSword), Is.EqualTo("Iron Sword"), "an item without its row");
    }

    [Test]
    public void Describe_APurchase_NamesWhatWasBoughtAndWhatItCost()
    {
        ClientContent content = Content();

        Assert.That(
            ShopMessages.Describe(Delta(100, 50, (Sword, 1)), content),
            Is.EqualTo("Bought Training Sword for 50 coins."));
        Assert.That(
            ShopMessages.Describe(Delta(100, 40, (Potion, 3)), content),
            Is.EqualTo("Bought Minor Health Potion x 3 for 60 coins."));
    }

    // Prototype Content §2: a refined row is named with its level in the sale's words and its prompt; at +0 a name
    // stays as it is.
    [Test]
    public void Describe_ASaleOfARefinedRow_NamesItsLevel_AsItsPromptDoes()
    {
        ClientContent content = Content();
        var units = new Dictionary<ItemDefinitionId, long> { [Sword] = -1 };

        Assert.That(
            ShopMessages.Describe(new InventoryDelta(50, 75, units, 7), content),
            Is.EqualTo("Sold +7 Training Sword for 25 coins."));
        Assert.That(
            ShopMessages.SellPrompt(content, Sword, 7, 25, Array.Empty<ItemDefinitionId>()),
            Is.EqualTo("Press again to sell +7 Training Sword for 25 coins"));
        Assert.That(ShopMessages.ItemName(content, Sword, 0), Is.EqualTo("Training Sword"));
    }

    [Test]
    public void Describe_ASale_NamesWhatWasSoldAndWhatItFetched()
    {
        ClientContent content = Content();

        Assert.That(
            ShopMessages.Describe(Delta(50, 74, (Gel, -12)), content),
            Is.EqualTo("Sold Slime Gel x 12 for 24 coins."));
        Assert.That(ShopMessages.Describe(Delta(0, 1, (Gel, -1)), content), Is.EqualTo("Sold Slime Gel for 1 coin."));
    }

    [Test]
    public void Describe_AnyOtherChange_SaysNothing()
    {
        ClientContent content = Content();
        InventoryDelta[] others =
        {
            Delta(50, 50, (Gel, 3)),
            Delta(50, 50, (Potion, -1)),
            Delta(50, 50),
            Delta(50, 150),
            Delta(100, 50, (Gel, -1)),
            Delta(50, 100, (Gel, 1)),
            Delta(100, 50, (Sword, 1), (Gel, -1))
        };

        foreach (InventoryDelta other in others)
        {
            Assert.That(ShopMessages.Describe(other, content), Is.Null);
        }
    }

    [Test]
    public void Describe_WithoutContent_NamesTheDefinition()
    {
        Assert.That(
            ShopMessages.Describe(Delta(100, 50, (Sword, 1)), null),
            Is.EqualTo("Bought item.weapon.training_sword for 50 coins."));
    }
}
}
