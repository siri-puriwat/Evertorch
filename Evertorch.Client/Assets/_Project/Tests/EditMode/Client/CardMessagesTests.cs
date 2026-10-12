using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The words of the cards (Prototype Content §2): what a card fits, a row's free slots, the Smith's rows, an
///     insertion's and a removal's lines, their refusals chosen from the client's own rows, and the changes that report
///     a row whose cards grew or shrank and the rows a change emptied.
/// </summary>
[TestFixture]
public sealed class CardMessagesTests
{
    private static readonly ItemDefinitionId Sword = new("item.weapon.iron_sword");
    private static readonly ItemDefinitionId Cloth = new("item.armor.cloth");
    private static readonly ItemDefinitionId Keen = new("item.card.forest_crawler");
    private static readonly ItemDefinitionId Bane = new("item.card.gloom_wisp");
    private static readonly ItemDefinitionId Gel = new("item.material.slime_gel");

    private static ClientContent Content()
    {
        var items = new Dictionary<ItemDefinitionId, ClientItem>
        {
            [Sword] = new(Sword, "Iron Sword", ItemType.Weapon, "pickup_iron_sword", "sword", slots: 2),
            [Cloth] = new(Cloth, "Cloth Armor", ItemType.Armor, "pickup_cloth_armor", "armor", slots: 1),
            [Keen] = new(
                Keen,
                "Forest Crawler Card",
                ItemType.Card,
                "pickup_slime_gel",
                "item_slime_gel",
                stackLimit: 999,
                fits: EquipmentSlot.Weapon,
                prefix: "Keen",
                description: "Attack +8."),
            [Bane] = new(
                Bane,
                "Gloom Wisp Card",
                ItemType.Card,
                "pickup_slime_gel",
                "item_slime_gel",
                stackLimit: 999,
                fits: EquipmentSlot.Weapon,
                prefix: "Bane",
                description: "Damage to bosses +20 %."),
            [Gel] = new(Gel, "Slime Gel", ItemType.Material, "pickup_slime_gel", "gel")
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

    [Test]
    public void AChange_ThatEmptiesACardedRow_KeepsItAsHeld_AndReportsNoCardMoved()
    {
        var inventory = new ClientInventory();
        inventory.OnSnapshot(
            new InventorySnapshot(
                4,
                1000,
                0,
                1,
                new[] { new InventoryEntry(2, Sword, 1, EquipmentSlot.None, 7, new[] { Keen }) }));
        var deltas = new List<InventoryDelta>();
        inventory.ChangeApplied += deltas.Add;

        inventory.OnChanged(
            new InventoryChanged(4, 5, 980, new[] { new InventoryEntry(2, Sword, 0), new InventoryEntry(9, Keen, 1) }));

        InventoryEntry emptied = deltas.Single().Emptied.Single();
        Assert.That((emptied.InventoryItem, emptied.Cards.Count), Is.EqualTo((2L, 1)));
        Assert.That(
            (deltas[0].RemovedCard, deltas[0].InsertedCard),
            Is.EqualTo((default(ItemDefinitionId), default(ItemDefinitionId))));
    }

    [Test]
    public void AChange_ThatGrowsARowsCards_ReportsTheCardAndTheRow()
    {
        var inventory = new ClientInventory();
        inventory.OnSnapshot(
            new InventorySnapshot(4, 0, 0, 1,
                new[] { new InventoryEntry(1, Keen, 2), new InventoryEntry(2, Sword, 1) }));
        var deltas = new List<InventoryDelta>();
        inventory.ChangeApplied += deltas.Add;

        inventory.OnChanged(
            new InventoryChanged(
                4,
                5,
                0,
                new[]
                {
                    new InventoryEntry(1, Keen, 1),
                    new InventoryEntry(2, Sword, 1, EquipmentSlot.None, 0, new[] { Keen })
                }));

        Assert.That(deltas, Has.Count.EqualTo(1));
        Assert.That(
            (deltas[0].InsertedCard, deltas[0].InsertedInto.InventoryItem),
            Is.EqualTo((Keen, 2L)));
    }

    [Test]
    public void AChange_ThatShrinksARowsCards_ReportsTheCardAndTheRowAsItWas()
    {
        var inventory = new ClientInventory();
        inventory.OnSnapshot(
            new InventorySnapshot(
                4,
                1000,
                0,
                1,
                new[] { new InventoryEntry(2, Sword, 1, EquipmentSlot.None, 0, new[] { Keen, Bane }) }));
        var deltas = new List<InventoryDelta>();
        inventory.ChangeApplied += deltas.Add;

        inventory.OnChanged(
            new InventoryChanged(
                4,
                5,
                800,
                new[]
                {
                    new InventoryEntry(2, Sword, 1, EquipmentSlot.None, 0, new[] { Bane }),
                    new InventoryEntry(9, Keen, 1)
                }));

        Assert.That(deltas, Has.Count.EqualTo(1));
        Assert.That(
            (deltas[0].RemovedCard, deltas[0].RemovedFrom.Cards.Count, deltas[0].InsertedCard, deltas[0].Emptied.Count),
            Is.EqualTo((Keen, 2, default(ItemDefinitionId), 0)));
    }

    [Test]
    public void ARefusedInsertion_SaysWhyFromTheClientsOwnRows()
    {
        ClientContent content = Content();
        InventoryEntry[] rows =
        {
            new(1, Keen, 2), new(2, Sword, 1, EquipmentSlot.None, 0, new[] { Keen, Keen }),
            new(3, Sword, 1, EquipmentSlot.Weapon), new(4, Cloth, 1)
        };

        string Refusal(long row, CommandRejectionReason reason)
        {
            return CardMessages.DescribeInsertRefusal(new CardRequest(1, Keen, row), rows, reason, content);
        }

        Assert.That(
            new[]
            {
                Refusal(2, CommandRejectionReason.NotAllowedNow), Refusal(3, CommandRejectionReason.NotAllowedNow),
                Refusal(4, CommandRejectionReason.NotAllowedNow)
            },
            Is.EqualTo(
                new[]
                {
                    "Double Keen Iron Sword [2] has no free slot.", "Take off Iron Sword [2] first.",
                    "Forest Crawler Card does not fit Cloth Armor [1]."
                }));
        Assert.That(
            Refusal(9, CommandRejectionReason.NotAllowedNow),
            Is.EqualTo(RejectionMessages.Describe(CommandRejectionReason.NotAllowedNow, false)),
            "a row the client no longer holds");
    }

    [Test]
    public void ARefusedRemoval_AsksForAWornRowToBeTakenOff_AndOtherwiseSaysWhy()
    {
        ClientContent content = Content();
        InventoryEntry[] rows =
        {
            new(2, Sword, 1, EquipmentSlot.Weapon, 0, new[] { Keen }),
            new(3, Sword, 1, EquipmentSlot.None, 0, new[] { Keen })
        };

        string Refusal(long row, CommandRejectionReason reason)
        {
            return CardMessages.DescribeRemoveRefusal(new CardRequest(0, Keen, row, 1), rows, reason, content);
        }

        Assert.That(
            new[]
            {
                Refusal(2, CommandRejectionReason.NotAllowedNow), Refusal(3, CommandRejectionReason.NotEnoughCoins),
                Refusal(3, CommandRejectionReason.InventoryFull)
            },
            Is.EqualTo(
                new[]
                {
                    "Take off Keen Iron Sword [2] first.",
                    RejectionMessages.Describe(CommandRejectionReason.NotEnoughCoins, false),
                    RejectionMessages.Describe(CommandRejectionReason.InventoryFull, false)
                }));
        Assert.That(new CardRequest(0, Keen, 2, 1).IsRemoval, Is.True);
    }

    [Test]
    public void FreeSlots_CountOnlyAnUnwornRowOfTheKindTheCardFits()
    {
        ClientContent content = Content();

        Assert.That(
            new[]
            {
                CardMessages.FreeSlots(content, new InventoryEntry(1, Sword, 1), EquipmentSlot.Weapon),
                CardMessages.FreeSlots(
                    content,
                    new InventoryEntry(2, Sword, 1, EquipmentSlot.None, 0, new[] { Keen }),
                    EquipmentSlot.Weapon),
                CardMessages.FreeSlots(content, new InventoryEntry(3, Sword, 1, EquipmentSlot.Weapon),
                    EquipmentSlot.Weapon),
                CardMessages.FreeSlots(content, new InventoryEntry(4, Cloth, 1), EquipmentSlot.Weapon),
                CardMessages.FreeSlots(content, new InventoryEntry(5, Gel, 3), EquipmentSlot.Weapon)
            },
            Is.EqualTo(new[] { 2, 1, 0, 0, 0 }),
            "free, one taken, worn, armor, a gel");
        Assert.That(
            (CardMessages.Fits(EquipmentSlot.Weapon), CardMessages.Fits(EquipmentSlot.Armor)),
            Is.EqualTo(("Fits: weapon", "Fits: armor")));
    }

    [Test]
    public void Inserted_NamesTheCardAndTheRowAsItIsNow()
    {
        var row = new InventoryEntry(2, Sword, 1, EquipmentSlot.None, 0, new[] { Keen });

        Assert.That(
            CardMessages.Inserted(Content(), Keen, row),
            Is.EqualTo("Forest Crawler Card inserted: Keen Iron Sword [2]."));
    }

    [Test]
    public void TheSmithsRow_AndARemoval_NameTheCard_TheRowAsItWas_AndTheFee()
    {
        var row = new InventoryEntry(2, Sword, 1, EquipmentSlot.None, 0, new[] { Keen, Bane });

        Assert.That(
            (CardMessages.TakeOut(Content(), Keen, 200), CardMessages.Removed(Content(), Keen, row, 200)),
            Is.EqualTo(
                ("Take out Forest Crawler Card · 200 coins",
                    "Took Forest Crawler Card out of Keen Bane Iron Sword [2] for 200 coins.")));
    }
}
}
