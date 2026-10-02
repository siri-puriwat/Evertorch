using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Slime Monarch's most valuable player (Gameplay Systems §2.1, §10; owner decisions 7 and 19): its log keeps,
///     in the order the characters first met it, the damage each dealt it by any means and the damage its basic attacks
///     dealt each; the MVP has the most of both together among those who may share, the earlier on a tie; it alone
///     gains the boss's MVP experience; and its prize is rolled from the drops' source.
/// </summary>
[TestFixture]
public sealed class MvpTests
{
    private const int Spare = 1_000_000;

    private static readonly MapDefinitionId Grotto = new("map.umbral_grotto");
    private static readonly MonsterDefinitionId Monarch = new("monster.slime_monarch");
    private static readonly SkillDefinitionId QuakeSlam = new("skill.quake_slam");

    private static MapInstance GrottoOf(TestServer server)
    {
        server.World.TryGetMap(Grotto, out MapInstance? map);
        return map!;
    }

    // Two players beside the boss, at 1.2 m and 1.6 m west of it, with HP to spare; the AI is off, so only the test
    // makes the boss act.
    private static (TestServer Server, ConnectionId[] Players, MonsterEntity Monarch) BesideTheBoss(
        IRandomSource? drops = null)
    {
        var server = new TestServer(
            withEveryMap: true,
            withGrotto: true,
            withMonsters: true,
            withMonsterAi: false,
            combatRandom: new SureHitRandom(),
            dropRandom: drops);
        MonsterEntity monarch = GrottoOf(server).Monsters.Single(monster => monster.Definition.Id == Monarch);
        var players = new ConnectionId[2];
        float[] distances = { 1.2f, 1.6f };
        for (int index = 0; index < players.Length; index++)
        {
            players[index] = server.EnterWorld(index + 1);
            server.CrossIntoTheGrotto(players[index]);
            PlayerEntity player = server.PlayerOf(players[index]);
            player.Position = new WorldPosition(monarch.Position.X - distances[index], 0f, monarch.Position.Z);
            player.CurrentHealth = Spare;
        }

        server.Tick();
        return (server, players, monarch);
    }

    private static CharacterSession SessionOf(TestServer server, ConnectionId connection)
    {
        server.Sessions.TryGetCharacter(server.PlayerOf(connection).Character, out CharacterSession? session);
        return session!;
    }

    private static string Log(MonsterEntity monarch)
    {
        return string.Join(
            ", ",
            monarch.MvpLog.Select(entry =>
                $"{entry.Character.Value}: {(entry.Dealt > 0 ? "dealt" : "-")} {(entry.Taken > 0 ? "taken" : "-")}"));
    }

    // A swing of the boss's takes its 2 s; three seconds of ticks are enough for any swing to land.
    private static void TickUntil(TestServer server, Func<bool> condition)
    {
        for (int tick = 0; tick < 3 * TestServer.TickRate && !condition(); tick++)
        {
            server.Tick();
        }

        Assert.That(condition(), Is.True, "within three seconds");
    }

    // The base experience a character holds from level 1, by the Adventurer's table.
    private static long HeldExperience(TestServer server, PlayerEntity player)
    {
        IReadOnlyList<int> levels = server.Content.ExperienceTables[new ExperienceDefinitionId("experience.adventurer")]
            .Levels;
        return player.Experience + levels.Take(player.Level - 1).Sum(points => (long)points);
    }

    [Test]
    public void AwardMostValuable_ReturnsWhatItGained_ToTheCap_AndNothingAtIt()
    {
        (TestServer server, ConnectionId[] players, MonsterEntity _) = BesideTheBoss();
        PlayerEntity ann = server.PlayerOf(players[0]);
        PlayerEntity bob = server.PlayerOf(players[1]);
        bob.Level = 24;
        bob.Experience = 3000;

        long fresh = server.Progression.AwardMostValuable(SessionOf(server, players[0]), 3000);
        long toTheCap = server.Progression.AwardMostValuable(SessionOf(server, players[1]), 3000);
        long atTheCap = server.Progression.AwardMostValuable(SessionOf(server, players[1]), 3000);

        Assert.That((fresh, HeldExperience(server, ann)), Is.EqualTo((3000L, 3000L)));
        Assert.That((toTheCap, atTheCap), Is.EqualTo((20L, 0L)), "3,020 to level 25, of which 3,000 were held");
        Assert.That(bob.Level, Is.EqualTo(25));
    }

    // Ann's hits make Ann the MVP; the scripted drops' first draw keeps the Mantle, and the drops roll after it.
    [Test]
    public void Kill_OfTheBoss_GivesTheMvpItsExperienceAlone_NamesItInTheFall_AndRollsItsPrizeBeforeTheDrops()
    {
        var drops = new ScriptedRandom(0);
        (TestServer server, ConnectionId[] players, MonsterEntity monarch) = BesideTheBoss(drops);
        PlayerEntity ann = server.PlayerOf(players[0]);
        PlayerEntity bob = server.PlayerOf(players[1]);
        monarch.LogDamage(ann.Character, 500);
        monarch.LogMvpDealt(ann.Character, 500);
        monarch.LogMvpTaken(bob.Character, 100);
        server.Transport.ClearSent();

        server.Combat.Kill(GrottoOf(server), monarch, ann, server.CurrentTick);

        IReadOnlyDictionary<string, object?> defeated = server.BossLog.Entries
            .Single(entry => entry.EventId.Id == 1024)
            .Fields;
        BossAnnouncement? fell = server.Transport.ControlSentTo(players[1])
            .Where(message => message.Opcode == MessageOpcode.BossAnnouncement)
            .Select(message => BossAnnouncement.TryRead(message.Payload, out BossAnnouncement? read) ? read : null)
            .Single();
        Assert.That(defeated["Character"], Is.EqualTo(ann.Character.Value.ToString()));
        Assert.That((fell!.Kind, fell.Name), Is.EqualTo((BossAnnouncementKind.Fell, ann.Name)));
        Assert.That(HeldExperience(server, ann), Is.EqualTo(6000 + 3000), "its whole share and the MVP's 3,000");
        Assert.That(HeldExperience(server, bob), Is.Zero, "no MVP experience without being the MVP");
        Assert.That(drops.Bounds.First(), Is.EqualTo(ItemDropSystem.RollScale), "the prize's roll comes first");
        Assert.That(
            drops.Bounds.Count,
            Is.EqualTo(1 + monarch.Definition.Drops.Count + monarch.Definition.Drops.Count),
            "one roll kept the Mantle, then each drop's chance and amount");
    }

    [Test]
    public void MostValuable_HasTheMostDealtAndTakenTogether_TheEarlierOnATie_AmongThoseWhoMayShare()
    {
        (TestServer server, ConnectionId[] players, MonsterEntity monarch) = BesideTheBoss();
        PlayerEntity ann = server.PlayerOf(players[0]);
        PlayerEntity bob = server.PlayerOf(players[1]);
        MapInstance map = GrottoOf(server);

        monarch.LogMvpDealt(ann.Character, 100);
        monarch.LogMvpTaken(bob.Character, 60);
        monarch.LogMvpDealt(bob.Character, 40);
        CharacterSession? onATie = server.Progression.ChooseMostValuable(map, monarch);
        monarch.LogMvpTaken(bob.Character, 1);
        CharacterSession? withMore = server.Progression.ChooseMostValuable(map, monarch);
        server.Combat.Kill(map, bob, monarch, server.CurrentTick);
        CharacterSession? withBobDead = server.Progression.ChooseMostValuable(map, monarch);
        server.Combat.Kill(map, ann, monarch, server.CurrentTick);
        CharacterSession? withNobody = server.Progression.ChooseMostValuable(map, monarch);

        Assert.That(onATie!.Character, Is.EqualTo(ann.Character), "100 against 60 and 40: the earlier");
        Assert.That(withMore!.Character, Is.EqualTo(bob.Character), "101 against 100");
        Assert.That(withBobDead!.Character, Is.EqualTo(ann.Character), "the dead may not share");
        Assert.That(withNobody, Is.Null);
    }

    [Test]
    public void MvpLog_CountsEveryHitOnTheBoss_AndItsBasicAttacksAlone_InTheOrderTheyFirstMet()
    {
        (TestServer server, ConnectionId[] players, MonsterEntity monarch) = BesideTheBoss();
        PlayerEntity ann = server.PlayerOf(players[0]);
        PlayerEntity bob = server.PlayerOf(players[1]);

        monarch.Target = bob.Id;
        monarch.Combat.IsAutoAttacking = true;
        TickUntil(server, () => monarch.MvpLog.Count > 0);
        monarch.Combat.IsAutoAttacking = false;
        string afterTheSwing = Log(monarch);
        server.SendAttack(players[0], monarch.Id, 1);
        TickUntil(server, () => monarch.MvpLog.Count > 1);
        server.SendCancel(players[0], 2);
        server.Tick(TestServer.TickRate);
        string afterTheHit = Log(monarch);
        long annTaken = monarch.MvpLog.Single(entry => entry.Character == ann.Character).Taken;
        server.Combat.BeginMonsterCast(GrottoOf(server), monarch, QuakeSlam, ann, server.CurrentTick);
        server.Tick(TestServer.TickRate * 2);

        Assert.That(afterTheSwing, Is.EqualTo($"{bob.Character.Value}: - taken"), "its swing at Bob");
        Assert.That(
            afterTheHit,
            Is.EqualTo($"{bob.Character.Value}: - taken, {ann.Character.Value}: dealt -"),
            "then Ann's hit, after Bob in the log");
        Assert.That(ann.CurrentHealth, Is.LessThan(Spare), "the slam struck Ann");
        Assert.That(
            monarch.MvpLog.Single(entry => entry.Character == ann.Character).Taken,
            Is.EqualTo(annTaken),
            "but its skills' damage is not taken");
        Assert.That(monarch.DamageLog.Select(entry => entry.Character), Is.EqualTo(new[] { ann.Character }));
    }
}
}
