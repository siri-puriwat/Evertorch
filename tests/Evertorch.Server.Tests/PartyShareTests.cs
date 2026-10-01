using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A party's share of a kill (Milestone 12 line 7; Gameplay Systems §2.1, §2.2): its members' shares pooled and
///     split evenly among those who may share, while their base levels span at most 5, and a kill's quest credit for
///     every member on the map, whatever the levels. The training slime gives 10 base experience.
/// </summary>
[TestFixture]
public sealed class PartyShareTests
{
    private const string Seven = "Tester7";
    private const string Eight = "Tester8";
    private const string Nine = "Tester9";
    private const string Hunt = "quest.crawler_hunt";

    private static PartyRig Rig()
    {
        return new PartyRig(new TestServer(withMonsters: true, withMonsterAi: false));
    }

    private static MapInstance Ground(TestServer server)
    {
        return server.World.Maps.Single(map => map.Definition.Id == new MapDefinitionId("map.training_ground"));
    }

    private static MonsterEntity SlimeOf(TestServer server)
    {
        MonsterEntity slime = Ground(server).Monsters.First();
        Assert.That(slime.Definition.BaseExperience, Is.EqualTo(10));
        return slime;
    }

    private static void Kill(TestServer server, MonsterEntity monster)
    {
        server.Combat.Kill(Ground(server), monster, null, server.CurrentTick);
    }

    [TestCase(6, 5L, 5L)]
    [TestCase(7, 10L, 0L)]
    public void TheSplit_HoldsUpToASpanOfFive(int level, long sharerExperience, long otherExperience)
    {
        PartyRig rig = Rig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Join(seven, Seven, eight, Eight);
        rig.Server.PlayerOf(eight).Level = level;
        MonsterEntity slime = SlimeOf(rig.Server);
        slime.LogDamage(rig.Server.PlayerOf(seven).Character, 10);

        Kill(rig.Server, slime);

        Assert.That(rig.Server.PlayerOf(seven).Experience, Is.EqualTo(sharerExperience));
        Assert.That(rig.Server.PlayerOf(eight).Experience, Is.EqualTo(otherExperience));
    }

    // The crawler lives on the field; the credit asks only which monster died on the members' map.
    [Test]
    public void AKill_CreditsTheQuestOfEveryMemberOnTheMap_WhateverTheLevels_OnceEach()
    {
        var rig = new PartyRig(new TestServer(withMonsters: true, withMonsterAi: false, withEveryMap: true));
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Join(seven, Seven, eight, Eight);
        rig.Server.PlayerOf(eight).Level = 30;
        var hunt = new QuestDefinitionId(Hunt);
        CharacterQuest[] quests = new[] { seven, eight, nine }
            .Select(member => rig.Server.SessionOf(member).Character!.Quests.Accept(hunt))
            .ToArray();
        MonsterEntity crawler = rig.Server.World.Maps
            .SelectMany(map => map.Monsters)
            .First(monster => monster.Definition.Id == new MonsterDefinitionId("monster.forest_crawler"));
        crawler.LogDamage(rig.Server.PlayerOf(seven).Character, 5);

        rig.Server.Progression.CreditQuests(Ground(rig.Server), crawler);

        Assert.That(quests.Select(quest => quest.Progress), Is.EqualTo(new[] { 1, 1, 0 }));
    }

    [Test]
    public void ALevelGainedFromTheKill_DoesNotChangeTheSplit()
    {
        PartyRig rig = Rig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        rig.Join(seven, Seven, eight, Eight);
        PlayerEntity first = rig.Server.PlayerOf(seven);
        first.Level = 6;
        first.Experience = rig.Server.Progression.ExperienceToNextLevel(first) - 1;
        MonsterEntity slime = SlimeOf(rig.Server);
        slime.LogDamage(first.Character, 10);

        Kill(rig.Server, slime);

        Assert.That(first.Level, Is.EqualTo(7), "the first member's half levels it up");
        Assert.That(rig.Server.PlayerOf(eight).Experience, Is.EqualTo(5), "the span was 5 before the kill");
    }

    [Test]
    public void AMemberWhoMayNotShare_IsLeftOutOfTheSplit()
    {
        PartyRig rig = Rig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Join(seven, Seven, eight, Eight);
        rig.Join(seven, Seven, nine, Nine);
        rig.Server.Combat.Kill(Ground(rig.Server), rig.Server.PlayerOf(nine), null, rig.Server.CurrentTick);
        MonsterEntity slime = SlimeOf(rig.Server);
        slime.LogDamage(rig.Server.PlayerOf(seven).Character, 10);

        Kill(rig.Server, slime);

        Assert.That(rig.Server.PlayerOf(seven).Experience, Is.EqualTo(5));
        Assert.That(rig.Server.PlayerOf(eight).Experience, Is.EqualTo(5));
        Assert.That(rig.Server.PlayerOf(nine).Experience, Is.Zero);
    }

    [Test]
    public void APartyWithinTheGap_SplitsThePoolEvenly_WithAMemberWhoNeverHit()
    {
        PartyRig rig = Rig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Join(seven, Seven, eight, Eight);
        MonsterEntity slime = SlimeOf(rig.Server);
        long job = slime.Definition.JobExperience;
        slime.LogDamage(rig.Server.PlayerOf(seven).Character, 30);
        slime.LogDamage(rig.Server.PlayerOf(nine).Character, 20);

        Kill(rig.Server, slime);

        Assert.That(rig.Server.PlayerOf(seven).Experience, Is.EqualTo(3), "half of the member's 6");
        Assert.That(rig.Server.PlayerOf(eight).Experience, Is.EqualTo(3));
        Assert.That(rig.Server.PlayerOf(nine).Experience, Is.EqualTo(4), "outside the party, its own share");
        Assert.That(rig.Server.PlayerOf(eight).JobExperience, Is.EqualTo(rig.Server.PlayerOf(seven).JobExperience));
        Assert.That(job, Is.Positive);
    }

    [Test]
    public void APoolNotDividingEvenly_LosesItsRemainder()
    {
        PartyRig rig = Rig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Join(seven, Seven, eight, Eight);
        rig.Join(seven, Seven, nine, Nine);
        MonsterEntity slime = SlimeOf(rig.Server);
        slime.LogDamage(rig.Server.PlayerOf(eight).Character, 10);

        Kill(rig.Server, slime);

        Assert.That(
            new[] { seven, eight, nine }.Select(member => rig.Server.PlayerOf(member).Experience),
            Has.All.EqualTo(3));
    }
}
}
