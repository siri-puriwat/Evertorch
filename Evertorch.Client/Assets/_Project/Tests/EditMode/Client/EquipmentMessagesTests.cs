using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The words of the eight positions (Prototype Content §2): their names, what the Equipment window shows in each,
///     the rows a position may take, and an equip's refusals, told apart by the rows.
/// </summary>
[TestFixture]
public sealed class EquipmentMessagesTests
{
    private static readonly ItemDefinitionId Sword = new("item.weapon.iron_sword");
    private static readonly ItemDefinitionId Cap = new("item.headgear.trail_cap");
    private static readonly ItemDefinitionId Charm = new("item.accessory.traveler_charm");
    private static readonly ItemDefinitionId GrottoCharm = new("item.accessory.grotto_charm");
    private static readonly ItemDefinitionId Keen = new("item.card.forest_crawler");

    private static ClientContent Content()
    {
        var items = new Dictionary<ItemDefinitionId, ClientItem>
        {
            [Sword] = new(Sword, "Iron Sword", ItemType.Weapon, "pickup_iron_sword", "sword", slots: 2),
            [Cap] = new(Cap, "Trail Cap", ItemType.Headgear, "pickup_cloth_armor", "armor", slots: 1),
            [Charm] = new(Charm, "Traveler Charm", ItemType.Accessory, "pickup_cloth_armor", "armor", slots: 1),
            [GrottoCharm] = new(
                GrottoCharm,
                "Grotto Charm",
                ItemType.Accessory,
                "pickup_cloth_armor",
                "armor",
                slots: 1),
            [Keen] = new(
                Keen,
                "Forest Crawler Card",
                ItemType.Card,
                "pickup_slime_gel",
                "item_slime_gel",
                stackLimit: 999,
                fits: ItemType.Weapon,
                prefix: "Keen",
                description: "Attack +8.")
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

    // An accessory goes to either accessory position, so it is listed under both; a worn row and another category's
    // are not (Gameplay Systems §11.1).
    [Test]
    public void ChoicesFor_ListsTheUnwornRowsThatGoThere_AnAccessoryUnderBothPositions()
    {
        InventoryEntry[] rows =
        {
            new(10, Charm, 1), new(11, Cap, 1), new(12, GrottoCharm, 1, EquipmentSlot.Accessory1), new(13, Charm, 1),
            new(14, Sword, 1)
        };

        long[] Choices(EquipmentSlot slot)
        {
            return EquipmentMessages.ChoicesFor(Content(), rows, slot).Select(row => row.InventoryItem).ToArray();
        }

        Assert.That(Choices(EquipmentSlot.Accessory1), Is.EqualTo(new[] { 10L, 13L }));
        Assert.That(Choices(EquipmentSlot.Accessory2), Is.EqualTo(new[] { 10L, 13L }));
        Assert.That(Choices(EquipmentSlot.Headgear), Is.EqualTo(new[] { 11L }));
        Assert.That(Choices(EquipmentSlot.Weapon), Is.EqualTo(new[] { 14L }));
        Assert.That(Choices(EquipmentSlot.Shield), Is.Empty);
        Assert.That(EquipmentMessages.ChoicesFor(null, rows, EquipmentSlot.Weapon), Is.Empty, "no content yet");
    }

    [Test]
    public void ChoosePosition_NamesTheChosenRow_AndBothAccessoryPositions()
    {
        InventoryEntry[] rows = { new(10, Charm, 1, refineLevel: 3) };

        Assert.That(
            EquipmentMessages.ChoosePosition(Content(), rows, 10),
            Is.EqualTo("Wear +3 Traveler Charm [1] in Accessory 1 or Accessory 2"));
    }

    // The server refuses a row worn anywhere and a position its category cannot go to alike, with 3; the rows tell
    // them apart (Prototype Content §2), and any other refusal keeps an equip's words.
    [Test]
    public void DescribeRefusal_TellsAWornRowFromTheWrongPosition_AndKeepsOtherWords()
    {
        InventoryEntry[] rows = { new(10, Charm, 1, EquipmentSlot.Accessory1), new(11, Cap, 1) };
        ClientContent content = Content();

        string worn = EquipmentMessages.DescribeRefusal(
            new EquipRequest(10, EquipmentSlot.Accessory2),
            rows,
            CommandRejectionReason.NotAllowedNow,
            content);
        string wrong = EquipmentMessages.DescribeRefusal(
            new EquipRequest(11, EquipmentSlot.Shield),
            rows,
            CommandRejectionReason.NotAllowedNow,
            content);
        string other = EquipmentMessages.DescribeRefusal(
            new EquipRequest(11, EquipmentSlot.Headgear),
            rows,
            CommandRejectionReason.NotAllowedNow,
            content);
        string requirement = EquipmentMessages.DescribeRefusal(
            new EquipRequest(11, EquipmentSlot.Shield),
            rows,
            CommandRejectionReason.RequirementNotMet,
            content);
        string gone = EquipmentMessages.DescribeRefusal(
            new EquipRequest(99, EquipmentSlot.Weapon),
            rows,
            CommandRejectionReason.NotAllowedNow,
            content);

        Assert.That(worn, Is.EqualTo("Take it off first."));
        Assert.That(wrong, Is.EqualTo("That doesn't go there."));
        Assert.That(other, Is.EqualTo(RejectionMessages.Describe(CommandRejectionReason.NotAllowedNow, true)));
        Assert.That(requirement, Is.EqualTo("Your job cannot wield that."));
        Assert.That(gone, Is.EqualTo(RejectionMessages.Describe(CommandRejectionReason.NotAllowedNow, true)));
    }

    [Test]
    public void PositionName_NamesTheEightInTheirOrder()
    {
        Assert.That(
            EquipmentPositions.All.Select(EquipmentMessages.PositionName),
            Is.EqualTo(
                new[]
                {
                    "Weapon", "Body armor", "Headgear", "Shield", "Garment", "Footwear", "Accessory 1", "Accessory 2"
                }));
        Assert.That(EquipmentMessages.PositionName(EquipmentSlot.None), Is.Empty);
    }

    [Test]
    public void Position_NamesWhatItHolds_WithItsLevelCardsAndSlots_OrEmpty()
    {
        var sword = new InventoryEntry(10, Sword, 1, EquipmentSlot.Weapon, 7, new[] { Keen });

        Assert.That(
            EquipmentMessages.Position(Content(), EquipmentSlot.Weapon, sword),
            Is.EqualTo("Weapon: +7 Keen Iron Sword [2]"));
        Assert.That(EquipmentMessages.Position(Content(), EquipmentSlot.Accessory2, null),
            Is.EqualTo("Accessory 2: empty"));
    }

    [Test]
    public void WornIn_FindsTheRowWornThere_OrNone()
    {
        InventoryEntry[] rows = { new(10, Charm, 1, EquipmentSlot.Accessory2), new(11, Cap, 1) };

        Assert.That(EquipmentMessages.WornIn(rows, EquipmentSlot.Accessory2)?.InventoryItem, Is.EqualTo(10L));
        Assert.That(EquipmentMessages.WornIn(rows, EquipmentSlot.Accessory1).HasValue, Is.False);
    }
}
}
