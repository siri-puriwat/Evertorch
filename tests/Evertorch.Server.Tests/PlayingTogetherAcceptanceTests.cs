using System.IO;
using System.Linq;
using Evertorch.Game;
using Evertorch.Persistence.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Milestone 12 "Playing together" exit criterion's server side end to end (ROADMAP §8): the composed server
///     host on a real PostgreSQL 18 and real UDP sockets on loopback, with three clients built from the client's
///     production networking and gameplay code. Today's step is only that three players entering the training ground
///     together see each other. Each later line of Milestone 12 adds its steps here: the names, chat nearby and by
///     whisper and in a party, the party's share of experience and quest credit, its members' health, and its survival
///     of a restart.
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

    // The viewer has a spawn for the seen character.
    private static void AssertSees(string step, SocketClient viewer, SocketClient seen)
    {
        EntityId entity = seen.World.LocalEntity;
        Assert.That(
            SocketClients.PumpUntil(() => viewer.World.Remotes.ContainsKey(entity), viewer, seen),
            Is.True,
            $"{step}: in view");
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

        AssertSees("enter", anna, bobby);
        AssertSees("enter", anna, cora);
        AssertSees("enter", bobby, cora);
        AssertCleanTraffic("enter", anna, bobby, cora);

        host.StopAsync().GetAwaiter().GetResult();
    }
}
}
