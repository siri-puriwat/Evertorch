using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Gate Warden's two grotto quests (Gameplay Systems §2.2; Prototype Content §6), by the quests' one rule: the
///     Grotto Hunt counts Grotto Crawlers, the Slime Monarch's fall counts for The Slime Monarch of every member of a
///     party on its map that holds it, whoever struck, and each pays its reward at the turn-in.
/// </summary>
[TestFixture]
public sealed class DungeonQuestTests
{
    private const string Seven = "Tester7";
    private const string Eight = "Tester8";
    private const string GateWarden = "npc.gate_warden";

    private static readonly QuestDefinitionId GrottoHunt = new("quest.grotto_hunt");
    private static readonly QuestDefinitionId BossQuest = new("quest.slime_monarch");
    private static readonly QuestDefinitionId CrawlerHunt = new("quest.crawler_hunt");

    private static TestServer InTheGrotto()
    {
        return new TestServer(withEveryMap: true, withGrotto: true, withMonsters: true, withMonsterAi: false);
    }

    private static MapInstance GrottoOf(TestServer server)
    {
        server.World.TryGetMap(new MapDefinitionId("map.umbral_grotto"), out MapInstance? map);
        return map!;
    }

    private static MonsterEntity FirstOf(TestServer server, string monster)
    {
        return GrottoOf(server).Monsters.First(entity => entity.Definition.Id == new MonsterDefinitionId(monster));
    }

    private static CharacterQuests QuestsOf(TestServer server, ConnectionId connection)
    {
        return server.SessionOf(connection).Character!.Quests;
    }

    [Test]
    public void AGrottoCrawlerFalling_CountsForTheGrottoHunt_NotTheCrawlerHunt()
    {
        TestServer server = InTheGrotto();
        ConnectionId player = server.EnterWorld(1);
        server.CrossIntoTheGrotto(player);
        CharacterQuest hunt = QuestsOf(server, player).Accept(GrottoHunt);
        CharacterQuest crawlerHunt = QuestsOf(server, player).Accept(CrawlerHunt);
        MonsterEntity crawler = FirstOf(server, "monster.grotto_crawler");
        crawler.LogDamage(server.PlayerOf(player).Character, 10);

        server.Combat.Kill(GrottoOf(server), crawler, server.PlayerOf(player), server.CurrentTick);

        Assert.That((hunt.Progress, crawlerHunt.Progress), Is.EqualTo((1, 0)));
    }

    // Seven struck the boss; Eight, Seven's party member, never did; Nine, alone, never did either.
    [Test]
    public void TheSlimeMonarchFalling_CountsForItsQuestAcrossThePartyOnItsMap_AndForNoOneElse()
    {
        var rig = new PartyRig(InTheGrotto());
        TestServer server = rig.Server;
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Join(seven, Seven, eight, Eight);
        ConnectionId[] players = { seven, eight, nine };
        foreach (ConnectionId player in players)
        {
            server.CrossIntoTheGrotto(player);
        }

        CharacterQuest[] bossQuests = players.Select(player => QuestsOf(server, player).Accept(BossQuest)).ToArray();
        CharacterQuest[] hunts = players.Select(player => QuestsOf(server, player).Accept(GrottoHunt)).ToArray();
        MonsterEntity monarch = FirstOf(server, "monster.slime_monarch");
        monarch.LogDamage(server.PlayerOf(seven).Character, 100);

        server.Combat.Kill(GrottoOf(server), monarch, server.PlayerOf(seven), server.CurrentTick);

        Assert.That(bossQuests.Select(quest => quest.Progress), Is.EqualTo(new[] { 1, 1, 0 }));
        Assert.That(hunts.Select(quest => quest.Progress), Is.EqualTo(new[] { 0, 0, 0 }), "no crawler fell");
    }

    // From level 1, 2,400 base experience is exactly the table's first ten steps.
    [Test]
    public void TheSlimeMonarch_TurnedIn_PaysItsExperienceAndCoins_AndIsCompleted()
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        server.Store.GiveQuest(1, BossQuest.Value, 1);
        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        NpcEntity warden = server.NpcOf(GateWarden);
        server.Place(player, warden.Position.X + 2f, warden.Position.Z);
        server.Tick(2);

        server.SendCompleteQuest(player, warden.Id, BossQuest.Value, 1);
        server.Tick();
        server.TickUntil(() => server.SessionOf(player).Character!.Operation == null);

        PlayerEntity entity = server.PlayerOf(player);
        CharacterSession character = server.SessionOf(player).Character!;
        Assert.That((entity.Level, entity.Experience), Is.EqualTo((11, 0L)));
        Assert.That(character.Inventory.Coins, Is.EqualTo(1600));
        Assert.That(character.Quests.TryGet(BossQuest, out CharacterQuest? quest) && quest!.IsCompleted, Is.True);
    }
}
}
