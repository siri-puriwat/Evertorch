using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Evertorch.Game;
using Evertorch.Persistence.Tests;
using Evertorch.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Milestone 12 "Playing together" exit criterion's server side end to end (ROADMAP §8): the composed server
///     host on a real PostgreSQL 18 and real UDP sockets on loopback, with three clients built from the client's
///     production networking and gameplay code. Three players entering the training ground together see each other,
///     each by its name (line 3); a line said nearby reaches all three, a whisper reaches only its recipient and comes
///     back to its speaker as sent, a whisper to no one is refused with 1, and party chat without a party with 3
///     (line 4). Anna invites Bobby and Cora, who accept; all three hear the roster of the party Anna leads and each
///     other's health, and a line said to the party reaches all three (line 6). A slime only Anna fights gives each of
///     the three a third of its experience (line 7). Each later line of Milestone 12 adds its steps here: the party's
///     survival of a restart.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class PlayingTogetherAcceptanceTests
{
    private const string TrainingGround = "map.training_ground";
    private const string AnnaIdentity = "playing-together-anna";
    private const string AnnaName = "Anna";
    private const string BobbyIdentity = "playing-together-bobby";
    private const string BobbyName = "Bobby";
    private const string CoraIdentity = "playing-together-cora";
    private const string CoraName = "Cora";
    private const string TrainingSlime = "monster.training_slime";
    private static readonly TimeSpan FightLimit = TimeSpan.FromSeconds(40);

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
            new[] { "--Network:Port=0", "--DevelopmentAuthentication:Enabled=true", "--World:RandomSeed=12" },
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

    // The viewer has a spawn for the seen character, which names it (Network Protocol §6).
    private static void AssertSees(string step, SocketClient viewer, SocketClient seen, string name)
    {
        EntityId entity = seen.World.LocalEntity;
        Assert.That(
            SocketClients.PumpUntil(() => viewer.World.Remotes.ContainsKey(entity), viewer, seen),
            Is.True,
            $"{step}: in view");
        Assert.That(viewer.World.Remotes[entity].Name, Is.EqualTo(name), $"{step}: by its name");
    }

    // Anna says hello nearby; Bobby whispers to Cora; Anna whispers to no one and speaks to a party she does not have
    // (Gameplay Systems §15).
    private static void Chat(SocketClient anna, SocketClient bobby, SocketClient cora)
    {
        const string step = "chat";
        var heard = new Dictionary<SocketClient, List<string>>();
        foreach (SocketClient client in new[] { anna, bobby, cora })
        {
            var lines = new List<string>();
            heard.Add(client, lines);
            client.Connection.ChatLineReceived += line => lines.Add($"{line.Channel} {line.Name}: {line.Text}");
        }

        anna.Connection.SendChat(ChatChannel.Nearby, string.Empty, "hello all");
        Assert.That(
            SocketClients.PumpUntil(() => heard.Values.All(lines => lines.Count == 1), anna, bobby, cora),
            Is.True,
            $"{step}: said nearby, heard by all three");
        Assert.That(heard.Values.Select(lines => lines[0]), Has.All.EqualTo($"Nearby {AnnaName}: hello all"));

        bobby.Connection.SendChat(ChatChannel.Whisper, CoraName.ToLowerInvariant(), "psst");
        Assert.That(
            SocketClients.PumpUntil(() => heard[cora].Count == 2 && heard[bobby].Count == 2, anna, bobby, cora),
            Is.True,
            $"{step}: the whisper and its echo");
        Assert.That(heard[cora][1], Is.EqualTo($"Whisper {BobbyName}: psst"));
        Assert.That(heard[bobby][1], Is.EqualTo($"WhisperSent {CoraName}: psst"));

        uint toNoOne = anna.Connection.SendChat(ChatChannel.Whisper, "Nobody1", "hello?");
        Assert.That(
            SocketClients.PumpUntil(() => anna.World.LastRejection != CommandRejectionReason.None, anna, bobby, cora),
            Is.True,
            $"{step}: the whisper to no one refused");
        Assert.That(anna.World.LastRejection, Is.EqualTo(CommandRejectionReason.InvalidTarget), $"{step}: {toNoOne}");
        uint toParty = anna.Connection.SendChat(ChatChannel.Party, string.Empty, "anyone?");
        Assert.That(
            SocketClients.PumpUntil(
                () => anna.World.LastRejection == CommandRejectionReason.NotAllowedNow,
                anna,
                bobby,
                cora),
            Is.True,
            $"{step}: party chat without a party refused, {toParty}");
        Assert.That(heard[anna], Has.Count.EqualTo(1), $"{step}: Anna heard only her own line");
        Assert.That(heard[bobby], Has.Count.EqualTo(2), $"{step}: nothing more reached Bobby");
    }

    // Anna invites Bobby, then Cora; each accepts, and the three hear the roster of three that Anna leads, each other's
    // health and SP, and Bobby's line to the party (Gameplay Systems §14, §15).
    private static void FormParty(SocketClient anna, SocketClient bobby, SocketClient cora)
    {
        const string step = "party";
        SocketClient[] all = { anna, bobby, cora };
        var heard = new Dictionary<SocketClient, List<string>>();
        var rosters = new Dictionary<SocketClient, PartyRoster>();
        var statuses = new Dictionary<SocketClient, HashSet<string>>();
        var lines = new Dictionary<SocketClient, List<string>>();
        foreach (SocketClient client in all)
        {
            var events = new List<string>();
            var named = new HashSet<string>();
            var said = new List<string>();
            heard.Add(client, events);
            statuses.Add(client, named);
            lines.Add(client, said);
            client.Connection.PartyEventReceived += partyEvent => events.Add($"{partyEvent.Kind} {partyEvent.Name}");
            client.Connection.PartyRosterReceived += roster => rosters[client] = roster;
            client.Connection.PartyMemberStatusReceived += status => named.Add(status.Name);
            client.Connection.ChatLineReceived += line => said.Add($"{line.Channel} {line.Name}: {line.Text}");
        }

        foreach ((SocketClient invitee, string name) in new[] { (bobby, BobbyName), (cora, CoraName) })
        {
            anna.Connection.SendPartyInvite(name);
            Assert.That(
                SocketClients.PumpUntil(() => heard[invitee].Contains($"Invited {AnnaName}"), all),
                Is.True,
                $"{step}: {name} invited");
            invitee.Connection.SendPartyReply(AnnaName, true);
            Assert.That(
                SocketClients.PumpUntil(() => heard[anna].Contains($"Joined {name}"), all),
                Is.True,
                $"{step}: {name} joined");
        }

        Assert.That(
            SocketClients.PumpUntil(
                () => all.All(client => rosters.TryGetValue(client, out PartyRoster? roster)
                    && roster.Members.Count == 3
                    && statuses[client].Count == 2),
                all),
            Is.True,
            $"{step}: every member heard the roster of three and the other two's health");
        foreach (SocketClient client in all)
        {
            Assert.That(
                rosters[client].Members.Select(member => member.Name),
                Is.EqualTo(new[] { AnnaName, BobbyName, CoraName }),
                $"{step}: the members in the order they joined");
            Assert.That(rosters[client].LeaderIndex, Is.Zero, $"{step}: Anna leads");
        }

        bobby.Connection.SendChat(ChatChannel.Party, string.Empty, "ready");
        Assert.That(
            SocketClients.PumpUntil(() => all.All(client => lines[client].Count == 1), all),
            Is.True,
            $"{step}: said to the party, heard by all three");
        Assert.That(lines.Values.Select(said => said[0]), Has.All.EqualTo($"Party {BobbyName}: ready"));
    }

    // Anna alone fights a slime; its 10 base experience is pooled for the party and split evenly, so each of the three,
    // all at level 1, gets 3 (Gameplay Systems §2.1).
    private static void ShareAKill(SocketClient anna, SocketClient bobby, SocketClient cora)
    {
        const string step = "share";
        SocketClient[] all = { anna, bobby, cora };
        var deaths = new List<EntityId>();
        anna.World.EntityDiedReceived += death => deaths.Add(death.Entity);
        EntityId slime = anna.CycleTarget(true);
        Assert.That(slime, Is.Not.EqualTo(default(EntityId)), $"{step}: Tab found a slime");
        Assert.That(anna.World.Remotes[slime].DefinitionId, Is.EqualTo(TrainingSlime), step);
        Assert.That(SocketClients.PumpUntil(() => anna.World.Target == slime, all), Is.True, $"{step}: targeted");
        anna.AttackTarget();
        Assert.That(
            SocketClients.PumpUntil(() => deaths.Contains(slime), FightLimit, all),
            Is.True,
            $"{step}: the slime died");
        Assert.That(
            SocketClients.PumpUntil(() => all.All(client => client.World.Experience == 3), all),
            Is.True,
            $"{step}: each member got a third ({string.Join(", ", all.Select(client => client.World.Experience))})");
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
    public void ThreePlayers_OverRealSocketsAndPostgres_SeeEachOtherEntering()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());

        using IHost host = StartHost(root.Path);
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        int port = host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
        using var anna = new SocketClient(content, AnnaIdentity, AnnaName);
        using var bobby = new SocketClient(content, BobbyIdentity, BobbyName);
        using var cora = new SocketClient(content, CoraIdentity, CoraName);
        EnterTogether(port, anna, bobby, cora);

        AssertSees("enter", anna, bobby, BobbyName);
        AssertSees("enter", bobby, cora, CoraName);
        AssertSees("enter", cora, anna, AnnaName);
        AssertCleanTraffic("enter", anna, bobby, cora);

        Chat(anna, bobby, cora);
        AssertCleanTraffic("chat", anna, bobby, cora);

        FormParty(anna, bobby, cora);
        AssertCleanTraffic("party", anna, bobby, cora);

        ShareAKill(anna, bobby, cora);
        AssertCleanTraffic("share", anna, bobby, cora);

        host.StopAsync().GetAwaiter().GetResult();
    }
}
}
