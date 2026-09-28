using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Evertorch.Client;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Persistence.Tests;
using Evertorch.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Milestone 5 acceptance path (ROADMAP §7) end to end: the composed server host on a real PostgreSQL 18, real
///     UDP sockets on loopback, and the client's production networking and gameplay code. The nine exit steps, then a
///     server restart and sign-in, then a reconnect while a pickup's answer is still on its way. Every sign-in is a
///     login and a password at the HTTPS gateway (Network Protocol §4), whose certificate the test pins, and the
///     server takes no development token.
/// </summary>
/// <remarks>
///     Combat and drops draw from scripted sources (<see cref="TestHosts.ScriptOutcomes" />): every attack hits
///     without a critical, so the slime dies in four hits and grazes the player for 1 HP, and every slime drops one
///     gel.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class AcceptanceScenarioTests
{
    private const string Login = "acceptance";
    private const string Password = "Accept-Password-1";
    private const string CharacterName = "Accept1";
    private const string TrainingGround = "map.training_ground";
    private const string TrainingSlime = "monster.training_slime";
    private const string SlimeGel = "item.material.slime_gel";
    private const float ConvergedDistance = 1e-3f;

    private static readonly TimeSpan FightLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WalkLimit = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StoreCheckInterval = TimeSpan.FromMilliseconds(100);

    private PostgresFixture m_database = null!;
    private TestCertificate m_certificate = null!;
    private PostgresGameStore m_store = null!;
    private AccountId m_account;
    private long m_character;
    private EntityId m_entity;
    private WorldPosition m_stoppedAt;

    [OneTimeSetUp]
    public void StartDatabase()
    {
        m_database = PostgresFixture.Start();
        m_store = new PostgresGameStore(m_database.ConnectionString);
        m_certificate = new TestCertificate();
    }

    [OneTimeTearDown]
    public void StopDatabase()
    {
        m_database.Dispose();
        m_certificate.Dispose();
    }

    private IHost StartHost(string contentRootPath)
    {
        HostApplicationBuilder builder = TestHosts.CreateBuilderWithGateway(
            new[] { "--Network:Port=0", "--World:RandomSeed=5" },
            contentRootPath,
            m_database.ConnectionString,
            m_certificate);
        TestHosts.ScriptOutcomes(builder, new SureHitRandom(), new ScriptedRandom(0));
        IHost host = builder.Build();
        host.Start();
        HealthProbe health = host.Services.GetRequiredService<HealthProbe>();
        var elapsed = Stopwatch.StartNew();
        while (!health.Evaluate().IsReady && elapsed.Elapsed < WalkLimit)
        {
            Thread.Sleep(20);
        }

        return host;
    }

    private GatewaySignInResult SignIn(IHost host)
    {
        int gatewayPort = host.Services.GetRequiredService<GatewayEndpoint>().Port;
        return SocketClient.SignIn(m_certificate, gatewayPort, Login, Password);
    }

    private static int PortOf(IHost host)
    {
        return host.Services.GetRequiredService<LiteNetLibServerTransport>().LocalPort;
    }

    /// <summary>
    ///     Exit steps 1–9 against the first server, which is then stopped.
    /// </summary>
    private void PlayTheExitSteps(IHost host)
    {
        int port = PortOf(host);
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        IAdminCommandService admin = host.Services.GetRequiredService<IAdminCommandService>();
        AccountCommandResult made = admin.CreateAccountAsync(AdminActor.LocalConsole, Login, Password)
            .GetAwaiter()
            .GetResult();
        Assert.That(made.Outcome, Is.EqualTo(AccountCommandOutcome.Created), "step 1: the operator made the account");
        GatewaySignInResult signIn = SignIn(host);
        Assert.That(signIn.Port, Is.EqualTo(port), "step 1: the gateway named the UDP port");

        using (var client = SocketClient.WithToken(content, signIn.Token, CharacterName))
        {
            var listSizes = new List<int>();
            client.Connection.CharactersChanged += () => listSizes.Add(client.Connection.Characters.Count);
            client.EnterWorld(signIn.Port);

            Assert.That(listSizes, Is.Not.Empty,
                "step 1: the token signed in and the account's characters were listed");
            Assert.That(listSizes[0], Is.Zero, "step 1: a new account starts without characters");
            ClientWorld world = client.World;
            CharacterListEntry created = client.Connection.Characters.Single();
            Assert.That(created.Name, Is.EqualTo(CharacterName), "step 2: the character was created and entered");
            Assert.That(world.Map, Is.EqualTo(new MapDefinitionId(TrainingGround)), "step 2: in the training ground");
            Assert.That(world.Inventory.Rows, Is.Empty, "step 2: a new character's inventory baseline is empty");
            m_character = created.Character.Value;
            m_entity = world.LocalEntity;
            m_account = LookUpAccount();

            SeeTheSlime(client);
            MoveAndReconcile(client, admin);
            ItemDropped drop = KillASlimeForItsGel(client, "steps 5 and 6");
            PickUp(client, drop, "step 7");
            Assert.That(StoredGel(), Is.EqualTo(1), "step 7: the pickup is committed in PostgreSQL");
            AssertCleanTraffic(client, "steps 1-7");
            client.Disconnect();
        }

        using (var again = SocketClient.WithToken(content, signIn.Token, CharacterName))
        {
            again.EnterWorld(port);
            Assert.That(again.World.LocalEntity, Is.EqualTo(m_entity), "step 8: reattached to the retained character");
            Assert.That(
                again.Connection.Characters.Single().Character.Value,
                Is.EqualTo(m_character),
                "step 9: the same character");
            Assert.That(GelIn(again.World), Is.EqualTo(1), "step 9: the same character still owns the gel");
            AwaitConvergence(again, admin, "step 9");
            m_stoppedAt = again.World.Predictor.Position;

            host.StopAsync().GetAwaiter().GetResult();
            Assert.That(
                again.PumpUntil(() => again.Connection.State == ClientConnectionState.Disconnected),
                Is.True,
                "restart: the client heard the server stop");
            Assert.That(again.Connection.Notice?.Reason, Is.EqualTo(DisconnectReason.Maintenance), "restart");
            AssertCleanTraffic(again, "steps 8-9");
        }
    }

    /// <summary>
    ///     After the restart: sign in again, then pick up a second gel whose answer is held back on the link, reconnect,
    ///     and find exactly one more gel.
    /// </summary>
    private void PlayAfterTheRestart(IHost host)
    {
        int port = PortOf(host);
        ServerContent content = host.Services.GetRequiredService<ServerContent>();
        IAdminCommandService admin = host.Services.GetRequiredService<IAdminCommandService>();

        EntityId entity;
        EntityId inFlight;
        GatewaySignInResult signIn = SignIn(host);
        using (var client = SocketClient.WithToken(content, signIn.Token, CharacterName))
        {
            client.EnterWorld(port);
            CharacterListEntry listed = client.Connection.Characters.Single();
            Assert.That(listed.Character.Value, Is.EqualTo(m_character), "restart: signed in to the same character");
            Assert.That(listed.Name, Is.EqualTo(CharacterName), "restart");
            Assert.That(GelIn(client.World), Is.EqualTo(1), "restart: the gel outlived the server");
            Assert.That(
                client.DistanceTo(m_stoppedAt),
                Is.LessThanOrEqualTo(ConvergedDistance),
                "restart: the shutdown checkpoint kept the position");
            entity = client.World.LocalEntity;

            ItemDropped drop = KillASlimeForItsGel(client, "in flight");
            inFlight = StartPickup(client, drop, "in flight");
            Assert.That(client.PumpUntil(() => client.Pickup.IsSent), Is.True, "in flight: the pickup was sent");

            // Reliable messages are delayed, never dropped, so every answer from here on stays on the link until the
            // disconnect discards it. The pickup itself already went to the socket.
            client.Link.LatencyMilliseconds = 10000;
            bool isCommitted = client.PumpUntil(Every(StoreCheckInterval, () => StoredGel() == 2));
            string refused = string.Join(
                ", ",
                admin.GetPlayers(AdminActor.LocalConsole).Select(player => player.RefusedCommands));
            Assert.That(isCommitted, Is.True, $"in flight: the pickup was committed (refused commands: {refused})");
            Assert.That(GelIn(client.World), Is.EqualTo(1), "in flight: the client has not heard the answer");
            AssertCleanTraffic(client, "in flight");
            client.Disconnect();
        }

        using (var again = SocketClient.WithToken(content, signIn.Token, CharacterName))
        {
            again.EnterWorld(port);
            ClientWorld world = again.World;
            Assert.That(world.LocalEntity, Is.EqualTo(entity), "in flight: reattached to the retained character");
            Assert.That(GelIn(world), Is.EqualTo(2), "in flight: the baseline holds the committed pickup once");
            Assert.That(world.Remotes.ContainsKey(inFlight), Is.False, "in flight: the drop is gone");

            var rejections = new List<CommandRejected>();
            world.CommandRejectedReceived += rejections.Add;
            again.Connection.SendPickup(inFlight);
            Assert.That(again.PumpUntil(() => rejections.Count > 0), Is.True,
                "in flight: the repeated pickup was answered");
            Assert.That(rejections.Single().Reason, Is.EqualTo(CommandRejectionReason.InvalidTarget), "in flight");
            again.PumpFor(TimeSpan.FromMilliseconds(200));
            Assert.That(GelIn(world), Is.EqualTo(2), "in flight: the repeat added nothing");
            Assert.That(StoredGel(), Is.EqualTo(2), "in flight: nothing more was stored");
            AssertCleanTraffic(again, "in flight");
        }
    }

    private static void SeeTheSlime(SocketClient client)
    {
        ClientWorld world = client.World;
        RemoteEntity? slime = null;
        bool isSeen = client.PumpUntil(() => (slime = DrawnSlime(world)) != null);
        Assert.That(isSeen, Is.True, "step 3: a training slime was spawned to the client and drawn");
        Assert.That(slime!.HealthPermille, Is.EqualTo(1000), "step 3: the slime arrived with its HP");
        int applied = world.SnapshotsApplied;
        Assert.That(
            client.PumpUntil(() => world.SnapshotsApplied >= applied + 5),
            Is.True,
            "step 3: snapshots keep the client synchronized");
    }

    private static RemoteEntity? DrawnSlime(ClientWorld world)
    {
        return world.Remotes.Values.FirstOrDefault(remote =>
            remote.Kind == EntityKind.Monster
            && remote.DefinitionId == TrainingSlime
            && remote.Buffer.TrySample(world.RemoteRenderTime, out WorldPosition _, out WorldDirection _));
    }

    /// <summary>
    ///     Step 4: a manual walk whose stop is lost, a manual walk over a lossy link, and a click walk toward the slimes.
    /// </summary>
    private static void MoveAndReconcile(SocketClient client, IAdminCommandService admin)
    {
        ClientWorld world = client.World;
        MovementController controller = client.Controller;
        LossyTransport link = client.Link;

        // With latency alone the prediction is exact. Losing everything from the stop on leaves the server walking
        // on the last input for its hold time (World:InputHoldTimeoutMs), so the client must take the server's word.
        link.LatencyMilliseconds = 50;
        controller.SetManualDirection(1f, 0f);
        client.PumpFor(TimeSpan.FromSeconds(1));
        float before = world.Smoother.LargestCorrection;
        link.LossPercent = 100;
        controller.SetManualDirection(0f, 0f);
        client.PumpFor(TimeSpan.FromMilliseconds(500));
        link.LossPercent = 0;
        AwaitConvergence(client, admin, "step 4, lost stop");
        TestContext.Out.WriteLine(
            $"Correction after the lost stop: {world.Smoother.LargestCorrection:F3} m (before {before:F3} m)");
        Assert.That(
            world.Smoother.LargestCorrection,
            Is.GreaterThan(Math.Max(before, 0.5f)),
            "step 4: the server corrected the prediction");
        Assert.That(world.Smoother.Snaps, Is.Zero, "step 4: the correction was smoothed, not snapped");

        int dropped = link.Dropped;
        link.JitterMilliseconds = 10;
        link.LossPercent = 10;
        link.ReorderPercent = 5;
        controller.SetManualDirection(1f, 0f);
        client.PumpFor(TimeSpan.FromSeconds(1.5));
        controller.SetManualDirection(0f, 0f);
        AwaitConvergence(client, admin, "step 4, lossy manual walk");

        // Where the walk ends is the server's word: stops lost on the link leave it walking on for up to its input
        // hold time (World:InputHoldTimeoutMs), 1.25 m at the default speed, so the bound allows that and no more.
        var clicked = new WorldPosition(12f, 0f, 8f);
        Assert.That(client.DistanceTo(clicked), Is.GreaterThan(3f), "step 4: the click is away from the player");
        bool isWalking = controller.TryMoveTo(world.Predictor.Position, clicked);
        Assert.That(isWalking, Is.True, "step 4: the clicked point is reachable");
        Assert.That(client.PumpUntil(() => !controller.HasPath, WalkLimit), Is.True, "step 4: the click walk ended");
        AwaitConvergence(client, admin, "step 4, lossy click walk");
        Assert.That(client.DistanceTo(clicked), Is.LessThan(1.5f), "step 4: the click walk took the player there");
        Assert.That(link.Dropped, Is.GreaterThan(dropped), "step 4: the link really lost messages");
        Assert.That(world.Smoother.Snaps, Is.Zero, "step 4: no correction was large enough to snap");

        link.LatencyMilliseconds = 0;
        link.JitterMilliseconds = 0;
        link.LossPercent = 0;
        link.ReorderPercent = 0;
    }

    private static void AwaitConvergence(SocketClient client, IAdminCommandService admin, string step)
    {
        ClientWorld world = client.World;

        // The admin view is republished once a second, so agreement shows up within two of those.
        bool isConverged = client.PumpUntil(() =>
            world.Predictor.PendingCount == 0
            && admin.GetPlayers(AdminActor.LocalConsole).Any(player =>
                player.Entity == world.LocalEntity && client.DistanceTo(player.Position) <= ConvergedDistance));
        string serverView = string.Join(
            ", ",
            admin.GetPlayers(AdminActor.LocalConsole).Select(player => player.Position.ToString()));
        Assert.That(
            isConverged,
            Is.True,
            $"{step}: the client at {world.Predictor.Position} converges on the server at {serverView}");
    }

    /// <summary>
    ///     Steps 5 and 6: Tab, then West once the server confirms the target; the client walks into range and the
    ///     server's hits kill the slime, which drops a gel.
    /// </summary>
    private static ItemDropped KillASlimeForItsGel(SocketClient client, string step)
    {
        ClientWorld world = client.World;
        var hits = new List<Damage>();
        var deaths = new List<EntityDied>();
        var drops = new List<ItemDropped>();
        world.DamageReceived += hits.Add;
        world.EntityDiedReceived += deaths.Add;
        world.ItemDroppedReceived += drops.Add;

        EntityId slime = client.CycleTarget(true);
        Assert.That(slime, Is.Not.EqualTo(default(EntityId)), $"{step}: Tab found a slime");
        Assert.That(client.PumpUntil(() => world.Target == slime), Is.True, $"{step}: the server confirmed the target");
        client.AttackTarget();
        Assert.That(
            client.PumpUntil(() => deaths.Any(death => death.Entity == slime) && drops.Count > 0, FightLimit),
            Is.True,
            $"{step}: the slime died and dropped something");

        Assert.That(hits.Where(hit => hit.Target == slime), Is.Not.Empty, $"{step}: the server reported the hits");
        Assert.That(world.IsLocalDead, Is.False, $"{step}: the player survived");
        ItemDropped drop = drops.Single();
        Assert.That(drop.ItemId, Is.EqualTo(SlimeGel), $"{step}: the server rolled a slime gel");
        Assert.That(drop.Amount, Is.EqualTo(1u), step);
        Assert.That(world.Remotes[drop.Entity].Kind, Is.EqualTo(EntityKind.ItemDrop), $"{step}: the gel lies there");

        world.DamageReceived -= hits.Add;
        world.EntityDiedReceived -= deaths.Add;
        world.ItemDroppedReceived -= drops.Add;
        return drop;
    }

    /// <summary>
    ///     F, once the attack has ended: the nearest drawn drop is the gel, and the client walks up to it.
    /// </summary>
    private static EntityId StartPickup(SocketClient client, ItemDropped drop, string step)
    {
        Assert.That(client.PumpUntil(() => !client.AutoAttack.IsActive), Is.True, $"{step}: the attack ended");
        EntityId picked = default;
        Assert.That(
            client.PumpUntil(() => (picked = client.PickUpNearest()) != default),
            Is.True,
            $"{step}: F found a drop within reach");
        Assert.That(picked, Is.EqualTo(drop.Entity), $"{step}: F chose the gel");
        return picked;
    }

    private static void PickUp(SocketClient client, ItemDropped drop, string step)
    {
        ClientWorld world = client.World;
        var rejections = new List<CommandRejected>();
        world.CommandRejectedReceived += rejections.Add;

        EntityId gel = StartPickup(client, drop, step);
        Assert.That(client.PumpUntil(() => GelIn(world) > 0), Is.True, $"{step}: the gel reached the inventory");

        Assert.That(GelIn(world), Is.EqualTo(1), step);
        Assert.That(world.Remotes.ContainsKey(gel), Is.False, $"{step}: the drop left the world");
        Assert.That(rejections, Is.Empty, $"{step}: nothing was refused");
        world.CommandRejectedReceived -= rejections.Add;
    }

    private static long GelIn(ClientWorld world)
    {
        return world.Inventory.Rows
            .Where(row => row.Item == new ItemDefinitionId(SlimeGel))
            .Sum(row => row.Quantity);
    }

    private static void AssertCleanTraffic(SocketClient client, string step)
    {
        Assert.That(client.Connection.MalformedMessages, Is.Zero, $"{step}: no malformed message");
        Assert.That(client.Connection.UnexpectedMessages, Is.Zero, $"{step}: no unexpected message");
    }

    /// <summary>
    ///     Checks <paramref name="condition" /> at most once per <paramref name="interval" />, so polling the database
    ///     does not slow the client's frame loop.
    /// </summary>
    private static Func<bool> Every(TimeSpan interval, Func<bool> condition)
    {
        Stopwatch? since = null;
        return () =>
        {
            if (since != null && since.Elapsed < interval)
            {
                return false;
            }

            since = Stopwatch.StartNew();
            return condition();
        };
    }

    private AccountId LookUpAccount()
    {
        AccountCredentials? account = m_store
            .FindAccountCredentialsAsync(Login, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That(account, Is.Not.Null, "the account exists");
        Assert.That(account!.IsDisabled, Is.False, "the account is active");
        return account.Account;
    }

    private long StoredGel()
    {
        StoredCharacter? stored = m_store
            .LoadCharacterAsync(m_account, m_character, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That(stored, Is.Not.Null, "the character is stored");
        return stored!.Items.Where(item => item.ItemDefinitionId == SlimeGel).Sum(item => (long)item.Quantity);
    }

    [Test]
    public void AcceptancePath_OverRealSocketsAndPostgres_KeepsEachGelThroughReconnectsAndARestart()
    {
        using var root = new TemporaryDirectory();
        PackageFixture.WriteTo(
            Path.Combine(root.Path, "content", "server"),
            PackageFixture.BuildRepositoryPackage());

        using (IHost first = StartHost(root.Path))
        {
            PlayTheExitSteps(first);
        }

        using (IHost restarted = StartHost(root.Path))
        {
            PlayAfterTheRestart(restarted);
            restarted.StopAsync().GetAwaiter().GetResult();
        }
    }
}
}
