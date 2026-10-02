using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Evertorch.Client;
using Evertorch.Game;
using Evertorch.Persistence.Tests;
using Evertorch.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Milestone 13 "A dungeon and its boss" exit criterion's server side end to end (ROADMAP §8): the composed
///     server host on a real PostgreSQL 18 and real UDP sockets on loopback, loading the server-only test package
///     (<see cref="DungeonPackage" />), with three clients built from the client's production networking and gameplay
///     code. Three players enter the training ground together, Aldo invites Bree and Cora into a party, and the party
///     walks through the ground's portal onto the training field, then through the field's east portal into the Umbral
///     Grotto, where a pack of Grotto Crawlers links when Aldo attacks one. Each later line of Milestone 13 adds its
///     steps here: the wisp's debuff, the boss with its slam and its announcement, the most valuable player's prize,
///     the quests, and a restart.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class DungeonAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string TrainingField = "map.training_field";
    private const string UmbralGrotto = "map.umbral_grotto";
    private const string GrottoCrawler = "monster.grotto_crawler";
    private const string AldoIdentity = "dungeon-aldo";
    private const string AldoName = "Aldo";
    private const string BreeIdentity = "dungeon-bree";
    private const string BreeName = "Bree";
    private const string CoraIdentity = "dungeon-cora";
    private const string CoraName = "Cora";

    private static readonly TimeSpan FightLimit = TimeSpan.FromSeconds(30);

    private PostgresFixture m_database = null!;

    [OneTimeSetUp]
    public void StartDatabase()
    {
        m_database = PostgresFixture.Start();
    }

    [OneTimeTearDown]
    public void StopDatabase()
    {
        m_database.Dispose();
    }

    private IHost StartHost(string contentRootPath)
    {
        HostApplicationBuilder builder = TestHosts.CreateBuilderWithDatabase(
            new[] { "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true", "--World:RandomSeed=13" },
            contentRootPath,
            m_database.ConnectionString);
        IHost host = builder.Build();
        host.Start();
        return host;
    }

    private static void EnterTogether(int port, params SocketClient[] clients)
    {
        foreach (SocketClient client in clients)
        {
            client.Connect(port);
        }

        bool isEntered = SocketClients.PumpUntil(
            () => clients.All(client => client.Connection.World?.Inventory.IsCurrent == true),
            clients);
        string states = string.Join(
            ", ",
            clients.Select(client => $"{client.Connection.State} {client.Connection.LocalError}"));
        Assert.That(isEntered, Is.True, $"enter: all three entered ({states})");
        foreach (SocketClient client in clients)
        {
            Assert.That(client.World.Map, Is.EqualTo(new MapDefinitionId(TrainingGround)), "enter: in town");
        }
    }

    // Aldo invites Bree, then Cora; once both accept, each of the three hears the roster of three that Aldo leads
    // (Gameplay Systems §14).
    private static void FormParty(SocketClient aldo, SocketClient bree, SocketClient cora)
    {
        const string step = "party";
        SocketClient[] all = { aldo, bree, cora };
        var heard = new Dictionary<SocketClient, List<string>>();
        var rosters = new Dictionary<SocketClient, PartyRoster>();
        foreach (SocketClient client in all)
        {
            var events = new List<string>();
            heard.Add(client, events);
            client.Connection.PartyEventReceived += partyEvent => events.Add($"{partyEvent.Kind} {partyEvent.Name}");
            client.Connection.PartyRosterReceived += roster => rosters[client] = roster;
        }

        foreach ((SocketClient invitee, string name) in new[] { (bree, BreeName), (cora, CoraName) })
        {
            aldo.Connection.SendPartyInvite(name);
            Assert.That(
                SocketClients.PumpUntil(() => heard[invitee].Contains($"Invited {AldoName}"), all),
                Is.True,
                $"{step}: {name} invited");
            invitee.Connection.SendPartyReply(AldoName, true);
            Assert.That(
                SocketClients.PumpUntil(() => heard[aldo].Contains($"Joined {name}"), all),
                Is.True,
                $"{step}: {name} joined");
        }

        Assert.That(
            SocketClients.PumpUntil(
                () => all.All(client => rosters.TryGetValue(client, out PartyRoster? roster)
                    && roster.Members.Count == 3),
                all),
            Is.True,
            $"{step}: every member heard the roster of three");
        foreach (SocketClient client in all)
        {
            Assert.That(rosters[client].LeaderIndex, Is.Zero, $"{step}: Aldo leads");
        }
    }

    // The party walks into the portal before the ground's east gate together, and each follows its character to the
    // field (Gameplay Systems §4.2).
    private static void CrossToTheField(ServerContent content, params SocketClient[] party)
    {
        const string step = "to the field";
        var field = new MapDefinitionId(TrainingField);
        MapPortal portal = content.Maps[new MapDefinitionId(TrainingGround)].Portals.Single();
        Assert.That(portal.DestinationMap, Is.EqualTo(field), step);
        foreach (SocketClient client in party)
        {
            Assert.That(
                client.Controller.TryMoveTo(
                    client.World.Predictor.Position,
                    new WorldPosition(portal.Center.X - 0.3f, 0f, portal.Center.Z)),
                Is.True,
                $"{step}: a way into the portal");
        }

        Assert.That(
            SocketClients.PumpUntil(
                () => party.All(client => client.Connection.World?.Map == field
                    && client.Connection.World.Inventory.IsCurrent),
                party),
            Is.True,
            $"{step}: every member followed its character to the field");
    }

    // The party walks on into the portal before the field's east gate, and each follows its character into the grotto,
    // where it stands by the grotto's west gate (Gameplay Systems §4.2).
    private static void CrossIntoTheGrotto(ServerContent content, params SocketClient[] party)
    {
        const string step = "into the grotto";
        var grotto = new MapDefinitionId(UmbralGrotto);
        MapPortal portal = content.Maps[new MapDefinitionId(TrainingField)].Portals
            .Single(candidate => candidate.DestinationMap == grotto);
        foreach (SocketClient client in party)
        {
            Assert.That(
                client.Controller.TryMoveTo(
                    client.World.Predictor.Position,
                    new WorldPosition(portal.Center.X - 0.3f, 0f, portal.Center.Z)),
                Is.True,
                $"{step}: a way into the portal");
        }

        Assert.That(
            SocketClients.PumpUntil(
                () => party.All(client => client.Connection.World?.Map == grotto
                    && client.Connection.World.Inventory.IsCurrent),
                party),
            Is.True,
            $"{step}: every member followed its character into the grotto");
        foreach (SocketClient client in party)
        {
            Assert.That(
                client.World.Predictor.Position,
                Is.EqualTo(content.Maps[grotto].SpawnPosition),
                $"{step}: by the west gate");
        }
    }

    private static bool IsGrottoCrawler(ClientWorld world, EntityId entity)
    {
        return world.Remotes.TryGetValue(entity, out RemoteEntity? remote) && remote.DefinitionId == GrottoCrawler;
    }

    // The party walks up the corridor to the crawler hall, and Aldo attacks the nearest Grotto Crawler: it strikes
    // back, and its kin within 11 m that see it answer its call, so more than one crawler swings at the party
    // (Gameplay Systems §10). The test package disarms the crawlers, so the party lives on.
    private static void MeetThePackThatLinks(SocketClient aldo, params SocketClient[] party)
    {
        const string step = "the pack";
        var hall = new WorldPosition(-22f, 0f, 4f);
        var swingers = new HashSet<EntityId>();
        EntityId[] members = party.Select(client => client.World.LocalEntity).ToArray();
        foreach (SocketClient client in party)
        {
            ClientWorld world = client.World;
            world.AttackStartedReceived += started =>
            {
                if (members.Contains(started.Target) && IsGrottoCrawler(world, started.Attacker))
                {
                    swingers.Add(started.Attacker);
                }
            };
            Assert.That(
                client.Controller.TryMoveTo(world.Predictor.Position, hall),
                Is.True,
                $"{step}: a way to the crawler hall");
        }

        Assert.That(
            SocketClients.PumpUntil(() => party.All(client => !client.Controller.HasPath), party),
            Is.True,
            $"{step}: in the crawler hall");
        EntityId crawler = aldo.CycleTarget(true);
        Assert.That(IsGrottoCrawler(aldo.World, crawler), Is.True, $"{step}: Tab found a Grotto Crawler");
        Assert.That(SocketClients.PumpUntil(() => aldo.World.Target == crawler, party), Is.True, $"{step}: targeted");
        aldo.AttackTarget();
        Assert.That(
            SocketClients.PumpUntil(() => swingers.Count >= 2, FightLimit, party),
            Is.True,
            $"{step}: more than one crawler swung at the party ({swingers.Count})");
        aldo.AutoAttack.OnWalkRequested();
    }

    private static void AssertCleanTraffic(string step, params SocketClient[] clients)
    {
        foreach (SocketClient client in clients)
        {
            Assert.That(client.Connection.MalformedMessages, Is.Zero, $"{step}: no malformed message");
            Assert.That(client.Connection.UnexpectedMessages, Is.Zero, $"{step}: no unexpected message");
        }
    }

    [Test]
    public void AParty_OverRealSocketsAndPostgres_CrossesTheFieldIntoTheGrotto()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            DungeonPackage.Build(DungeonPackage.Disarm(GrottoCrawler)));

        using IHost host = StartHost(root.Path);
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
        using var aldo = new SocketClient(content, AldoIdentity, AldoName);
        using var bree = new SocketClient(content, BreeIdentity, BreeName);
        using var cora = new SocketClient(content, CoraIdentity, CoraName);
        EnterTogether(port, aldo, bree, cora);
        AssertCleanTraffic("enter", aldo, bree, cora);

        FormParty(aldo, bree, cora);
        AssertCleanTraffic("party", aldo, bree, cora);

        CrossToTheField(content, aldo, bree, cora);
        AssertCleanTraffic("to the field", aldo, bree, cora);

        CrossIntoTheGrotto(content, aldo, bree, cora);
        AssertCleanTraffic("into the grotto", aldo, bree, cora);

        MeetThePackThatLinks(aldo, aldo, bree, cora);
        AssertCleanTraffic("the pack", aldo, bree, cora);

        host.StopAsync().GetAwaiter().GetResult();
    }
}
}
