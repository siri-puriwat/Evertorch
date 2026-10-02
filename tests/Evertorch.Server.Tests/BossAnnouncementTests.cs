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
///     The Slime Monarch's appearance and fall (Network Protocol §9; System Architecture §10): every player in the world
///     on its map hears both, and no one elsewhere; both are logged; and the console's <c>boss</c> lists it alive or
///     returning, while <c>boss respawn</c> brings it back at once, audited.
/// </summary>
[TestFixture]
public sealed class BossAnnouncementTests
{
    private static readonly MapDefinitionId Grotto = new("map.umbral_grotto");
    private static readonly MonsterDefinitionId Monarch = new("monster.slime_monarch");

    // One player by the grotto's west gate, far from the boss, and one still in town.
    private static (TestServer Server, ConnectionId InTheGrotto, ConnectionId InTown) Players(bool withMonsterAi)
    {
        var server = new TestServer(
            withEveryMap: true,
            withGrotto: true,
            withMonsters: true,
            withMonsterAi: withMonsterAi);
        ConnectionId grotto = server.EnterWorld(1);
        server.CrossIntoTheGrotto(grotto);
        ConnectionId town = server.EnterWorld(2);
        server.Tick();
        return (server, grotto, town);
    }

    private static MapInstance GrottoOf(TestServer server)
    {
        server.World.TryGetMap(Grotto, out MapInstance? map);
        return map!;
    }

    private static MonsterEntity MonarchOf(TestServer server)
    {
        return GrottoOf(server).Monsters.Single(monster => monster.Definition.Id == Monarch && !monster.IsDead);
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

    private static string Console(TestServer server, string line)
    {
        var output = new StringWriter();
        Task answered = new AdminConsole(server.Admin).Execute(line, TextWriter.Synchronized(output));
        server.Tick();
        Assert.That(answered.Wait(TimeSpan.FromSeconds(10)), Is.True, $"'{line}' was answered");
        return output.ToString().Trim();
    }

    // The status the console reads is published once a second.
    private static void Publish(TestServer server)
    {
        server.Tick(TestServer.TickRate);
    }

    [Test]
    public void Console_Boss_ListsItAlive_ThenReturningWithinItsSpread_AndBossRespawnBringsItBackAtOnce()
    {
        (TestServer server, ConnectionId grotto, ConnectionId town) = Players(true);
        Publish(server);
        string alive = Console(server, "boss");
        string status = Console(server, "status");
        server.Combat.Kill(GrottoOf(server), MonarchOf(server), server.PlayerOf(grotto), server.CurrentTick);
        Publish(server);
        string returning = Console(server, "boss");
        server.Transport.ClearSent();

        string respawned = Console(server, "boss respawn");
        string again = Console(server, "boss respawn");
        Publish(server);

        Assert.That(alive, Is.EqualTo("boss monster.slime_monarch on map.umbral_grotto: alive"));
        Assert.That(status, Does.EndWith("boss monster.slime_monarch on map.umbral_grotto: alive"));
        int minutes = int.Parse(
            returning.Replace("boss monster.slime_monarch on map.umbral_grotto: returns in about ", string.Empty)
                .Replace(" min", string.Empty));
        Assert.That(minutes, Is.InRange(50, 70), "an hour after its death, give or take ten minutes");
        Assert.That(respawned, Is.EqualTo("Brought back: monster.slime_monarch."));
        Assert.That(again, Is.EqualTo("No boss is waiting to return."));
        Assert.That(AnnouncementsHeardBy(server, grotto), Is.EqualTo(new[] { "Appeared monster.slime_monarch ''" }));
        Assert.That(AnnouncementsHeardBy(server, town), Is.Empty);
        Assert.That(
            server.AuditLogger.Entries
                .Where(entry => entry.EventId.Name == "OperatorBossRespawned")
                .Select(entry => entry.Fields["Bosses"]),
            Is.EqualTo(new[] { "monster.slime_monarch", "none" }));
        Assert.That(MonarchOf(server).CurrentHealth, Is.EqualTo(MonarchOf(server).MaxHealth));
        Assert.That(Console(server, "boss"), Is.EqualTo("boss monster.slime_monarch on map.umbral_grotto: alive"));
    }

    [Test]
    public void Console_Boss_WithNoBossInTheWorld_SaysSo()
    {
        var server = new TestServer(withMonsters: true);
        Publish(server);

        Assert.That(Console(server, "boss"), Is.EqualTo("No bosses in the world."));
    }

    [Test]
    public void Monarch_AtStartUp_IsLoggedOnTheFirstTick_AndOnlyThen()
    {
        var server = new TestServer(withEveryMap: true, withGrotto: true, withMonsters: true);

        server.Tick(3);

        IReadOnlyDictionary<string, object?> spawned = server.BossLog.Entries
            .Single(entry => entry.EventId.Id == 1023)
            .Fields;
        Assert.That(
            (spawned["Monster"], spawned["Map"]),
            Is.EqualTo(("monster.slime_monarch", "map.umbral_grotto")));
    }

    [Test]
    public void Monarch_Falling_TellsEveryPlayerOnItsMap_AndLogsIt()
    {
        (TestServer server, ConnectionId grotto, ConnectionId town) = Players(false);
        MonsterEntity monarch = MonarchOf(server);
        server.Transport.ClearSent();

        server.Combat.Kill(GrottoOf(server), monarch, server.PlayerOf(grotto), server.CurrentTick);

        Assert.That(
            AnnouncementsHeardBy(server, grotto),
            Is.EqualTo(new[] { "Fell monster.slime_monarch ''" }),
            "heard by the west gate, far from the boss");
        Assert.That(AnnouncementsHeardBy(server, town), Is.Empty, "no one in town hears it");
        IReadOnlyDictionary<string, object?> defeated = server.BossLog.Entries
            .Single(entry => entry.EventId.Id == 1024)
            .Fields;
        Assert.That(
            (defeated["Monster"], defeated["Entity"], defeated["Character"]),
            Is.EqualTo(("monster.slime_monarch", monarch.Id.Value, "none")));
    }
}
}
