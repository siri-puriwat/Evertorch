using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The words of a boss's appearance and fall (Prototype Content §2), by its display name, and by its ID when the
///     content does not know it.
/// </summary>
[TestFixture]
public sealed class BossMessagesTests
{
    private static readonly MonsterDefinitionId Monarch = new("monster.slime_monarch");

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
            new Dictionary<ItemDefinitionId, ClientItem>(),
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

    [Test]
    public void Describe_ForABossTheContentDoesNotKnow_NamesItsId()
    {
        var announcement = new BossAnnouncement(BossAnnouncementKind.Appeared, Monarch, string.Empty);

        Assert.That(BossMessages.Describe(announcement, null), Is.EqualTo("The monster.slime_monarch has appeared."));
    }
}
}
