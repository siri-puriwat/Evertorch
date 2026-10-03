using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The words of a boss's appearance and fall, and of its most valuable player's award (Prototype Content §2), by
///     display names, and by IDs when the content does not know them.
/// </summary>
[TestFixture]
public sealed class BossMessagesTests
{
    private static readonly MonsterDefinitionId Monarch = new("monster.slime_monarch");
    private static readonly ItemDefinitionId Mantle = new("item.armor.monarch_mantle");
    private static readonly ItemDefinitionId Jelly = new("item.material.monarch_jelly");

    private static ClientContent Content()
    {
        var monarch = new ClientMonster(
            Monarch,
            "Slime Monarch",
            "monster_training_slime",
            "monster_training_slime_icon");
        return new ClientContent(
            "0000000000000000",
            new Dictionary<MapDefinitionId, ClientMap>(),
            new Dictionary<JobDefinitionId, ClientJob>(),
            new Dictionary<MonsterDefinitionId, ClientMonster> { { Monarch, monarch } },
            new Dictionary<ItemDefinitionId, ClientItem>
            {
                { Mantle, new ClientItem(Mantle, "Monarch Mantle", ItemType.Armor, "pickup_mantle", "icon_mantle") },
                { Jelly, new ClientItem(Jelly, "Monarch Jelly", ItemType.Material, "pickup_jelly", "icon_jelly") }
            },
            new Dictionary<SkillDefinitionId, ClientSkill>(),
            new Dictionary<StatusDefinitionId, ClientStatusEffect>());
    }

    [TestCase(BossAnnouncementKind.Appeared, "", "The Slime Monarch has appeared.")]
    [TestCase(BossAnnouncementKind.Fell, "", "The Slime Monarch has fallen.")]
    [TestCase(BossAnnouncementKind.Fell, "Anna", "The Slime Monarch has fallen. MVP: Anna.")]
    public void Describe_NamesTheBossAndItsMostValuablePlayer(BossAnnouncementKind kind, string name, string expected)
    {
        Assert.That(BossMessages.Describe(new BossAnnouncement(kind, Monarch, name), Content()), Is.EqualTo(expected));
    }

    [TestCase(3000UL, 5U, "You are the MVP: 3,000 experience and 5 Monarch Jelly.")]
    [TestCase(0UL, 5U, "You are the MVP: 5 Monarch Jelly.")]
    [TestCase(1234567UL, 0U, "You are the MVP: 1,234,567 experience.")]
    [TestCase(0UL, 0U, "You are the MVP.")]
    public void Describe_TheAward_NamesAnAmountAboveOne_AndLeavesOutWhatItDidNotGive(
        ulong experience,
        uint amount,
        string expected)
    {
        MvpAwarded award = amount > 0
            ? new MvpAwarded(Monarch, experience, Jelly, amount, PrizePlacement.Bag)
            : new MvpAwarded(Monarch, experience, null, 0, PrizePlacement.None);

        Assert.That(BossMessages.Describe(award, Content()), Is.EqualTo(expected));
    }

    [Test]
    public void Describe_ForABossTheContentDoesNotKnow_NamesItsId()
    {
        var announcement = new BossAnnouncement(BossAnnouncementKind.Appeared, Monarch, string.Empty);

        Assert.That(BossMessages.Describe(announcement, null), Is.EqualTo("The monster.slime_monarch has appeared."));
    }

    [Test]
    public void Describe_TheAwardOfAPrizeTheContentDoesNotKnow_NamesItsId()
    {
        var award = new MvpAwarded(Monarch, 3000, Mantle, 1, PrizePlacement.Bag);

        Assert.That(
            BossMessages.Describe(award, null),
            Is.EqualTo("You are the MVP: 3,000 experience and item.armor.monarch_mantle."));
    }

    [Test]
    public void Describe_TheAward_NamesTheExperienceAndThePrize_AndWhereItLies()
    {
        string inTheBag =
            BossMessages.Describe(new MvpAwarded(Monarch, 3000, Mantle, 1, PrizePlacement.Bag), Content());
        string atTheFeet =
            BossMessages.Describe(new MvpAwarded(Monarch, 3000, Mantle, 1, PrizePlacement.Feet), Content());

        Assert.That(inTheBag, Is.EqualTo("You are the MVP: 3,000 experience and Monarch Mantle."));
        Assert.That(
            atTheFeet,
            Is.EqualTo(
                "You are the MVP: 3,000 experience and Monarch Mantle. Your bag was full; it lies at your feet."));
    }
}
}
