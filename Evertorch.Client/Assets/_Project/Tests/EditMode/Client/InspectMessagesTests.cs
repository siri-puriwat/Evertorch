using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The words of an inspection (Prototype Content §2): each worn item named with its level, cards, and slots, each
///     card on a line of its own, "none" for an empty slot; and <c>/inspect</c>.
/// </summary>
[TestFixture]
public sealed class InspectMessagesTests
{
    private static readonly ItemDefinitionId Sword = new("item.weapon.iron_sword");
    private static readonly ItemDefinitionId Keen = new("item.card.forest_crawler");

    private static ClientContent Content()
    {
        var items = new Dictionary<ItemDefinitionId, ClientItem>
        {
            [Sword] = new(Sword, "Iron Sword", ItemType.Weapon, "pickup_iron_sword", "sword", slots: 2),
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

    [Test]
    public void Lines_NameEachWornItem_ItsCards_AndAnEmptySlot()
    {
        var items = new InspectedItem[8];
        items[0] = new InspectedItem(Sword, 7, new[] { Keen, Keen });
        var shown = new PlayerInspection(new EntityId(9), 3, items);

        Assert.That(
            InspectMessages.Lines(Content(), shown),
            Is.EqualTo(
                new[]
                {
                    "Weapon: +7 Double Keen Iron Sword [2]", "· Forest Crawler Card", "· Forest Crawler Card",
                    "Armor: none"
                }));
    }

    [Test]
    public void SlashInspect_AsksToSeeTheNamedPlayer_OrSaysHow()
    {
        ChatRequest inspect = ChatCommands.Parse("/inspect Bobby", null, "Anna");

        Assert.That((inspect.Inspected, inspect.Channel, inspect.Refusal),
            Is.EqualTo(("Bobby", ChatChannel.None, (string?)null)));
        Assert.That(ChatCommands.Parse("/INSPECT  Cora now", null, "Anna").Inspected, Is.EqualTo("Cora"));
        Assert.That(ChatCommands.Parse("/inspect", null, "Anna").Refusal, Is.EqualTo("Inspect with /inspect Name."));
        Assert.That(ChatCommands.Usage, Does.Contain("/inspect Name"));
    }
}
}
