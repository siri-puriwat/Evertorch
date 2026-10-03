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

    private static ClientContent Content()
    {
        var items = new Dictionary<ItemDefinitionId, ClientItem>
        {
            [Sword] = new(Sword, "Training Sword", ItemType.Weapon, "pickup_training_sword", "sword"),
            [Gel] = new(Gel, "Slime Gel", ItemType.Material, "pickup_slime_gel", "gel"),
            [Potion] = new(Potion, "Minor Health Potion", ItemType.Consumable, "pickup_minor_health", "potion")
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

    private static InventoryDelta Delta(uint before, uint after, params (ItemDefinitionId Item, long Units)[] moved)
    {
        var units = new Dictionary<ItemDefinitionId, long>();
        foreach ((ItemDefinitionId item, long count) in moved)
        {
            units[item] = count;
        }

        return new InventoryDelta(before, after, units);
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
