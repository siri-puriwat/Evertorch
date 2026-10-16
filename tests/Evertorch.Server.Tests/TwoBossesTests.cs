using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The world's two bosses side by side (Network Protocol §9; System Architecture §10, §12; Milestone 21 line 6): both
///     spawn at start-up in their own places; the console's <c>boss</c> and <c>status</c> list both by their maps, the
///     living first and then the dead in the order they fell; <c>boss respawn</c> brings back each that waits, heard only
///     on its own map; each fall names its own most valuable player; the walk's <c>World:BossHealthPercent</c> scales
///     both; and the Furnace Heart notices nobody on the road below its arena.
/// </summary>
[TestFixture]
public sealed class TwoBossesTests
{
    private static readonly MapDefinitionId Quarry = new("map.ashen_quarry");
    private static readonly MapDefinitionId Grotto = new("map.umbral_grotto");
    private static readonly MonsterDefinitionId FurnaceHeart = new("monster.furnace_heart");
    private static readonly MonsterDefinitionId Monarch = new("monster.slime_monarch");

    private static MapInstance MapOf(TestServer server, MapDefinitionId id)
    {
        server.World.TryGetMap(id, out MapInstance? map);
        return map!;
    }

    private static MonsterEntity BossOf(TestServer server, MapDefinitionId map)
    {
        return MapOf(server, map).Monsters.Single(monster => monster.Definition.IsBoss && !monster.IsDead);
    }

    // One player at the quarry's arrival, one by the grotto's west gate, both far from their bosses, and one in town.
    private static (TestServer Server, ConnectionId InTheQuarry, ConnectionId InTheGrotto, ConnectionId InTown)
        Players(bool withMonsterAi = false, int bossHealthPercent = 100)
    {
        var server = new TestServer(
            withEveryMap: true,
            withGrotto: true,
            withQuarry: true,
            withMonsters: true,
            withMonsterAi: withMonsterAi,
            dropRandom: new ScriptedRandom(0),
            bossHealthPercent: bossHealthPercent);
        ConnectionId quarry = server.EnterWorld(1);
        server.CrossIntoTheQuarry(quarry);
        ConnectionId grotto = server.EnterWorld(2);
        server.CrossIntoTheGrotto(grotto);
        ConnectionId town = server.EnterWorld(3);
        server.Tick();
        return (server, quarry, grotto, town);
    }

    private static List<string> AnnouncementsHeardBy(TestServer server, ConnectionId player)
    {
        var heard = new List<string>();
        foreach (InMemoryServerTransport.SentMessage message in server.Transport.ControlSentTo(player))
        {
            if (message.Opcode == MessageOpcode.BossAnnouncement
                && BossAnnouncement.TryRead(message.Payload, out BossAnnouncement? announcement))
            {
                heard.Add($"{announcement!.Kind} {announcement.Monster.Value} '{announcement.Name}'");
            }
        }

        return heard;
    }

    private static string[] Console(TestServer server, string line)
    {
        var output = new StringWriter();
        Task answered = new AdminConsole(server.Admin).Execute(line, TextWriter.Synchronized(output));
        server.Tick();
        Assert.That(answered.Wait(TimeSpan.FromSeconds(10)), Is.True, $"'{line}' was answered");
        return output.ToString().Trim().Split('\n').Select(text => text.Trim()).ToArray();
    }

    // The status the console reads is published once a second.
    private static void Publish(TestServer server)
    {
        server.Tick(TestServer.TickRate);
    }

    private static int MinutesUntilTheReturn(string line, MonsterDefinitionId boss, MapDefinitionId map)
    {
        string prefix = $"boss {boss.Value} on {map.Value}: returns in about ";
        Assert.That(line, Does.StartWith(prefix).And.EndWith(" min"));
        return int.Parse(line.Substring(prefix.Length, line.Length - prefix.Length - " min".Length));
    }

    // The walk's helper (owner decision 26): every boss spawns with its share of its HP, the other monsters with all
    // of theirs.
    [Test]
    public void BossHealthPercent_ScalesBothBosses_AndNoOtherMonster()
    {
        (TestServer server, ConnectionId _, ConnectionId _, ConnectionId _) = Players(bossHealthPercent: 10);

        MonsterEntity[] bosses = { BossOf(server, Quarry), BossOf(server, Grotto) };

        Assert.That(
            bosses.Select(boss => (boss.Definition.Id.Value, boss.MaxHealth, boss.CurrentHealth)),
            Is.EqualTo(
                bosses.Select(boss => (boss.Definition.Id.Value, boss.Definition.Hp / 10, boss.Definition.Hp / 10))));
        Assert.That(bosses[0].MaxHealth, Is.EqualTo(3850), "the Furnace Heart's 38,500 HP");
        Assert.That(
            server.World.Maps.SelectMany(map => map.Monsters)
                .Where(monster => !monster.Definition.IsBoss)
                .Count(monster => monster.MaxHealth != monster.Definition.Hp),
            Is.Zero);
    }

    [Test]
    public void Bosses_AtStartUp_SpawnInTheirOwnPlaces_AndAreLoggedOnTheFirstTick()
    {
        var server = new TestServer(withEveryMap: true, withGrotto: true, withQuarry: true, withMonsters: true);

        server.Tick(3);

        Assert.That(
            server.World.Maps.SelectMany(map => map.Monsters
                .Where(monster => monster.Definition.IsBoss)
                .Select(boss => (map.Definition.Id.Value, boss.Definition.Id.Value, boss.Position.X,
                    boss.Position.Z))),
            Is.EquivalentTo(
                new[]
                {
                    ("map.ashen_quarry", "monster.furnace_heart", 0f, 28f),
                    ("map.umbral_grotto", "monster.slime_monarch", 18f, 18f)
                }));
        Assert.That(
            server.BossLog.Entries.Where(entry => entry.EventId.Id == 1023)
                .Select(entry => (entry.Fields["Monster"], entry.Fields["Map"])),
            Is.EquivalentTo(
                new[]
                {
                    ("monster.furnace_heart", "map.ashen_quarry"),
                    ((object?)"monster.slime_monarch", (object?)"map.umbral_grotto")
                }));
    }

    [Test]
    public void Console_ListsBothBossesByTheirMaps_AndBossRespawnBringsEachBackToItsOwnMap()
    {
        (TestServer server, ConnectionId quarry, ConnectionId grotto, ConnectionId town) = Players(true);
        Publish(server);
        string[] alive = Console(server, "boss");
        string[] status = Console(server, "status");
        server.Combat.Kill(MapOf(server, Grotto), BossOf(server, Grotto), server.PlayerOf(grotto), server.CurrentTick);
        Publish(server);
        string[] oneDead = Console(server, "boss");
        server.Combat.Kill(MapOf(server, Quarry), BossOf(server, Quarry), server.PlayerOf(quarry), server.CurrentTick);
        Publish(server);
        string[] bothDead = Console(server, "boss");
        server.Transport.ClearSent();

        string[] respawned = Console(server, "boss respawn");
        Publish(server);

        Assert.That(
            alive,
            Is.EqualTo(
                new[]
                {
                    "boss monster.furnace_heart on map.ashen_quarry: alive",
                    "boss monster.slime_monarch on map.umbral_grotto: alive"
                }),
            "both alive, in their maps' order");
        Assert.That(status.TakeLast(2), Is.EqualTo(alive), "the status ends with the boss lines");
        Assert.That(oneDead[0], Is.EqualTo("boss monster.furnace_heart on map.ashen_quarry: alive"),
            "the living first");
        Assert.That(MinutesUntilTheReturn(oneDead[1], Monarch, Grotto), Is.InRange(50, 70));
        Assert.That(MinutesUntilTheReturn(bothDead[0], Monarch, Grotto), Is.InRange(50, 70), "then as they fell");
        Assert.That(
            MinutesUntilTheReturn(bothDead[1], FurnaceHeart, Quarry),
            Is.InRange(50, 70),
            "an hour after its death, give or take ten minutes");
        Assert.That(respawned, Is.EqualTo(new[] { "Brought back: monster.slime_monarch, monster.furnace_heart." }));
        Assert.That(AnnouncementsHeardBy(server, quarry), Is.EqualTo(new[] { "Appeared monster.furnace_heart ''" }));
        Assert.That(AnnouncementsHeardBy(server, grotto), Is.EqualTo(new[] { "Appeared monster.slime_monarch ''" }));
        Assert.That(AnnouncementsHeardBy(server, town), Is.Empty);
        Assert.That(Console(server, "boss"), Is.EqualTo(alive), "both back");
    }

    // Each boss's fall names the player that did the most to it, on its own map alone, and awards it alone.
    [Test]
    public void EachBossesFall_NamesItsOwnMostValuablePlayer_OnItsOwnMap()
    {
        (TestServer server, ConnectionId quarry, ConnectionId grotto, ConnectionId town) = Players();
        MonsterEntity heart = BossOf(server, Quarry);
        MonsterEntity monarch = BossOf(server, Grotto);
        heart.LogMvpDealt(server.PlayerOf(quarry).Character, 500);
        monarch.LogMvpDealt(server.PlayerOf(grotto).Character, 500);
        server.Transport.ClearSent();

        server.Combat.Kill(MapOf(server, Quarry), heart, server.PlayerOf(quarry), server.CurrentTick);
        server.Combat.Kill(MapOf(server, Grotto), monarch, server.PlayerOf(grotto), server.CurrentTick);
        server.Tick(TestServer.TickRate);

        Assert.That(
            AnnouncementsHeardBy(server, quarry),
            Is.EqualTo(new[] { "Fell monster.furnace_heart 'Tester1'" }),
            "the quarry's player, its most valuable");
        Assert.That(
            AnnouncementsHeardBy(server, grotto),
            Is.EqualTo(new[] { "Fell monster.slime_monarch 'Tester2'" }),
            "the grotto's player, its own");
        Assert.That(AnnouncementsHeardBy(server, town), Is.Empty);
        foreach ((ConnectionId player, MonsterDefinitionId boss) in new[] { (quarry, FurnaceHeart), (grotto, Monarch) })
        {
            Assert.That(
                server.Transport.ControlSentTo(player)
                    .Where(message => message.Opcode == MessageOpcode.MvpAwarded)
                    .Select(message =>
                        MvpAwarded.TryRead(message.Payload, out MvpAwarded? read) ? read!.Monster : default),
                Is.EqualTo(new[] { boss }),
                $"{boss.Value}: its own award");
        }

        Assert.That(server.Transport.ControlOpcodesSentTo(town), Has.No.Member(MessageOpcode.MvpAwarded));
    }

    // Acquisition has no sight test (Gameplay Systems §10): a player walking up the road below the arena, beyond the
    // boss's 6 m, never draws the Furnace Heart out of it.
    [Test]
    public void TheFurnaceHeart_NoticesNobodyOnTheRoadBelowItsArena()
    {
        (TestServer server, ConnectionId quarry, ConnectionId _, ConnectionId _) = Players(true);
        PlayerEntity player = server.PlayerOf(quarry);
        MonsterEntity heart = BossOf(server, Quarry);
        NavigationGrid grid = MapOf(server, Quarry).Definition.Navigation;

        bool hasNoticed = false;
        foreach (float z in new[] { 4.5f, 8f, 12f, 15.5f })
        {
            Assert.That(grid.CanOccupy(0f, z), Is.True, $"(0, {z}) on the road");
            player.Position = new WorldPosition(0f, 0f, z);
            player.CurrentHealth = player.MaxHealth;
            for (int tick = 0; tick < 5 * TestServer.TickRate; tick++)
            {
                server.Tick();
                hasNoticed |= heart.Target == player.Id || heart.Brain.State == MonsterAiState.Chase;
            }
        }

        Assert.That(hasNoticed, Is.False);
        Assert.That(heart.CurrentHealth, Is.EqualTo(heart.MaxHealth));
    }
}
}
