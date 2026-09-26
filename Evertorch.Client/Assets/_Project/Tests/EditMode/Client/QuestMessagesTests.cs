using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The client's words for a quest (Prototype Content §2): the NPC window's lines, the status bar's part, and the
///     feedback lines, composed from display names and the values on the wire.
/// </summary>
[TestFixture]
public sealed class QuestMessagesTests
{
    private static readonly QuestDefinitionId Hunt = new("quest.crawler_hunt");
    private static readonly MonsterDefinitionId Crawler = new("monster.forest_crawler");
    private static readonly NpcQuestOffer Offer = new(Hunt, Crawler, 5, 150, 100);

    private static ClientContent Content()
    {
        return new ClientContent(
            "0000000000000000",
            new Dictionary<MapDefinitionId, ClientMap>(),
            new Dictionary<JobDefinitionId, ClientJob>(),
            new Dictionary<MonsterDefinitionId, ClientMonster>
            {
                [Crawler] = new(Crawler, "Forest Crawler", "monster_forest_crawler", "crawler")
            },
            new Dictionary<ItemDefinitionId, ClientItem>(),
            new Dictionary<SkillDefinitionId, ClientSkill>(),
            new Dictionary<StatusDefinitionId, ClientStatusEffect>(),
            null,
            new Dictionary<QuestDefinitionId, ClientQuest> { [Hunt] = new(Hunt, "Crawler Hunt") });
    }

    private static QuestLogEntry Active(ushort progress)
    {
        return new QuestLogEntry(Hunt, QuestState.Active, progress, 5);
    }

    [Test]
    public void Describe_SaysWhatAQuestLogChanged_AndNothingForTheRest()
    {
        ClientContent content = Content();
        var completed = new QuestLogEntry(Hunt, QuestState.Completed, 5, 5);

        Assert.That(QuestMessages.Describe(null, Active(0), Offer, content), Is.EqualTo("Accepted Crawler Hunt."));
        Assert.That(
            QuestMessages.Describe(Active(2), Active(3), Offer, content),
            Is.EqualTo("Crawler Hunt: Forest Crawler 3/5."));
        Assert.That(QuestMessages.Describe(Active(2), Active(3), null, content), Is.EqualTo("Crawler Hunt: 3/5."));
        Assert.That(
            QuestMessages.Describe(Active(4), Active(5), Offer, content),
            Is.EqualTo("Crawler Hunt is ready to turn in."));
        Assert.That(
            QuestMessages.Describe(Active(5), completed, Offer, content),
            Is.EqualTo("Completed Crawler Hunt: 150 base experience, 100 coins."));
        Assert.That(QuestMessages.Describe(Active(5), completed, null, content), Is.EqualTo("Completed Crawler Hunt."));
        Assert.That(QuestMessages.Describe(Active(3), Active(3), Offer, content), Is.Null, "nothing changed");
        Assert.That(QuestMessages.Describe(completed, completed, Offer, content), Is.Null, "still completed");
    }

    [Test]
    public void Progress_NamesTheObjectivesMonster_ReadyAtTheCount_OrTheQuestWithoutAnOffer()
    {
        ClientContent content = Content();

        Assert.That(QuestMessages.Progress(Active(3), Offer, content), Is.EqualTo("Forest Crawler 3/5"));
        Assert.That(QuestMessages.Progress(Active(5), Offer, content), Is.EqualTo("Forest Crawler 5/5 (ready)"));
        Assert.That(QuestMessages.Progress(Active(3), null, content), Is.EqualTo("Crawler Hunt 3/5"));
    }

    [Test]
    public void TheOffer_NamesTheObjectiveAndTheReward()
    {
        ClientContent content = Content();

        Assert.That(QuestMessages.QuestName(content, Hunt), Is.EqualTo("Crawler Hunt"));
        Assert.That(QuestMessages.Objective(Offer, content), Is.EqualTo("Defeat: Forest Crawler × 5"));
        Assert.That(QuestMessages.Reward(Offer), Is.EqualTo("Reward: 150 base experience, 100 coins"));
        Assert.That(QuestMessages.Reward(new NpcQuestOffer(Hunt, Crawler, 5, 0, 1)), Is.EqualTo("Reward: 1 coin"));
        Assert.That(QuestMessages.Objective(Offer, null), Is.EqualTo("Defeat: monster.forest_crawler × 5"));
    }
}
}
