using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Evertorch.Game;
using Evertorch.Protocol;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class TelemetryTests
{
    [TestCase("shutdownnow")]
    [TestCase("save now")]
    [TestCase("teleport 1 2 3")]
    [TestCase("status now")]
    public void Execute_UnknownCommand_PrintsHelpAndDoesNothingElse(string line)
    {
        var output = new StringWriter();

        CreateConsole(new TestServer()).Execute(line, output);

        Assert.That(
            output.ToString().Trim(),
            Is.EqualTo("Unknown command. Commands: status, players, save, shutdown [reason], help"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Execute_BlankLine_PrintsNothing(string line)
    {
        var output = new StringWriter();

        CreateConsole(new TestServer()).Execute(line, output);

        Assert.That(output.ToString(), Is.Empty);
    }

    private static AdminConsole CreateConsole(TestServer server)
    {
        return new AdminConsole(server.Admin);
    }

    private static ServerMetrics CreateMetrics(out CapturingLogger<TickLogObserver> log)
    {
        log = new CapturingLogger<TickLogObserver>();
        return new ServerMetrics(new TickLogObserver(log, new FakeClock()), TestInstruments.Create());
    }

    // The console thread reads only the published status; what acts on the world waits in the queue for the tick
    // thread, and a shutdown goes to the host.
    [Test]
    public void AdminCommandService_DependsOnNothingThatTouchesTheWorldFromItsCaller()
    {
        Type[] dependencies = typeof(AdminCommandService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.That(
            dependencies,
            Is.EquivalentTo(
                new[]
                {
                    typeof(StatusPublisher),
                    typeof(AdminQueue),
                    typeof(ShutdownRequest),
                    typeof(IHostApplicationLifetime),
                    typeof(AuditLog)
                }));
    }

    [Test]
    public void Execute_PlayersWithNobodyInTheWorld_SaysSo()
    {
        var output = new StringWriter();

        CreateConsole(new TestServer()).Execute("players", output);

        Assert.That(output.ToString(), Does.Contain("No players in the world."));
    }

    [Test]
    public void Execute_Players_ListsEveryPlayerWithoutCredentials()
    {
        var server = new TestServer();
        ConnectionId seven = server.Connect();
        server.SignInWithCharacter(seven, 7);
        server.Store.Edit(7, coins: 250);
        server.SendEnterWorld(seven, 7);
        server.TickUntil(() => server.SessionOf(seven).State == SessionState.InWorld);
        server.EnterWorld(8);
        server.Tick(19);
        var output = new StringWriter();

        CreateConsole(server).Execute("  PLAYERS ", output);

        string[] lines = output.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines, Has.Length.EqualTo(2));
        Assert.That(lines[0], Does.Contain("character 7").And.Contain("map.training_ground").And.Contain("rtt 42 ms"));
        Assert.That(lines[0], Does.EndWith(" other epoch 0 coins 250"), "input for another map, then the coins");
        Assert.That(lines[1], Does.Contain("character 8"));
        Assert.That(output.ToString(), Does.Not.Contain("dev:").And.Not.Contain("tester"));
    }

    [Test]
    public void Execute_Status_PrintsThePublishedStatus()
    {
        var server = new TestServer();
        server.EnterWorld(7);
        server.TickUntilPublished();
        var output = new StringWriter();

        CreateConsole(server).Execute("status", output);

        string text = output.ToString();
        Assert.That(text, Does.Contain($"tick {server.Status.Current.Tick} at 20 Hz"));
        Assert.That(text, Does.Contain("sessions connected 1, signed in 1, in world 1; authentication failures 0"));
        Assert.That(text, Does.Contain("admission open"));
        Assert.That(text, Does.Contain("database available, persistence jobs waiting 0, checkpoints waiting 0"));
        Assert.That(
            text,
            Does.Contain(
                "rate limits: messages over budget 0, commands throttled 0; violations 0, disconnects for violations 0"));
        Assert.That(text, Does.Contain("network bytes in 1000, out 2000"));
        Assert.That(text, Does.Contain("map map.training_ground: 1 players"));
    }

    [Test]
    public void Execute_Status_ShowsUptimeAndAClosedAdmission()
    {
        var server = new TestServer();
        server.Clock.Advance(TimeSpan.FromHours(26) + TimeSpan.FromSeconds(7));
        server.Transport.CloseAdmission();
        server.TickUntilPublished();
        var output = new StringWriter();

        CreateConsole(server).Execute("status", output);

        Assert.That(output.ToString(), Does.StartWith("uptime 1.02:00:07, tick"));
        Assert.That(output.ToString(), Does.Contain("admission closed"));
    }

    [Test]
    public void Metrics_AfterTicks_ReportLastAndMaximumDuration()
    {
        ServerMetrics metrics = CreateMetrics(out CapturingLogger<TickLogObserver> _);

        metrics.OnTickCompleted(1, TimeSpan.FromMilliseconds(4));
        metrics.OnTickCompleted(2, TimeSpan.FromMilliseconds(9));
        metrics.OnTickCompleted(3, TimeSpan.FromMilliseconds(2));

        Assert.That(metrics.LastTick, Is.EqualTo(3u));
        Assert.That(metrics.LastTickDuration, Is.EqualTo(TimeSpan.FromMilliseconds(2)));
        Assert.That(metrics.MaxTickDuration, Is.EqualTo(TimeSpan.FromMilliseconds(9)));
    }

    [Test]
    public void Metrics_WithOverruns_CountsThemAndStillLogs()
    {
        ServerMetrics metrics = CreateMetrics(out CapturingLogger<TickLogObserver> log);

        metrics.OnOverrun(5, TimeSpan.FromMilliseconds(80), 0);
        metrics.OnOverrun(6, TimeSpan.FromMilliseconds(600), 9);

        Assert.That(metrics.Overruns, Is.EqualTo(2));
        Assert.That(metrics.SkippedSteps, Is.EqualTo(9));
        Assert.That(log.Entries.Select(entry => entry.EventId.Name), Is.EqualTo(new[] { "TickOverrun" }));
    }

    [Test]
    public void PlayerSummary_HasNoMemberThatCouldCarryAnIdentityOrToken()
    {
        string[] textMembers = typeof(PlayerSummary)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.Name)
            .ToArray();

        Assert.That(textMembers, Is.Empty);
    }

    [Test]
    public void Run_WhenAlreadyStopped_ReadsNothing()
    {
        var output = new StringWriter();
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        CreateConsole(new TestServer()).Run(new StringReader("help"), output, stop.Token);

        Assert.That(output.ToString(), Is.Empty);
    }

    [Test]
    public void Run_WhenInputEnds_ExitsQuietly()
    {
        var output = new StringWriter();
        var input = new StringReader($"help{Environment.NewLine}");

        CreateConsole(new TestServer()).Run(input, output, CancellationToken.None);

        Assert.That(output.ToString().Trim(), Is.EqualTo("Commands: status, players, save, shutdown [reason], help"));
    }

    [Test]
    public void Status_BeforeAnyTick_IsEmpty()
    {
        var server = new TestServer();

        Assert.That(server.Status.Current, Is.SameAs(ServerStatus.Empty));
    }

    [Test]
    public void Status_IsASnapshot_NotAViewOfLiveState()
    {
        var server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        server.TickUntilPublished();
        ServerStatus published = server.Status.Current;
        WorldPosition publishedPosition = published.Players.Single().Position;

        server.SendMove(connection, 1, 1f, 0f);
        server.Tick(5);

        Assert.That(server.Status.Current, Is.SameAs(published), "nothing is republished within the second");
        Assert.That(published.Players.Single().Position, Is.EqualTo(publishedPosition));
        Assert.That(server.PlayerOf(connection).Position, Is.Not.EqualTo(publishedPosition));
    }

    [Test]
    public void Status_IsPublishedOnTheFirstTickAndThenOncePerSecond()
    {
        var server = new TestServer();

        server.Tick();
        uint afterFirstTick = server.Status.Current.Tick;
        server.Tick(18);
        uint beforeTheSecondIsUp = server.Status.Current.Tick;
        server.Tick();

        Assert.That(afterFirstTick, Is.EqualTo(1u));
        Assert.That(beforeTheSecondIsUp, Is.EqualTo(1u));
        Assert.That(server.Status.Current.Tick, Is.EqualTo(20u));
        Assert.That(server.Status.Current.TickRate, Is.EqualTo(TestServer.TickRate));
    }

    [Test]
    public void Status_ReportsSessionsPlayersQueueCountersAndTransportTotals()
    {
        var server = new TestServer();
        ConnectionId inWorld = server.EnterWorld(7);
        ConnectionId handshakeOnly = server.Connect();
        server.SendHello(handshakeOnly);
        server.Inbound.OnPayload(inWorld, ProtocolChannel.Control, new byte[] { 0xFF, 0xFF });
        server.SendMove(inWorld, 2, 1f, 0f);
        server.SendMove(inWorld, 1, 1f, 0f);
        server.Metrics.OnTickCompleted(19, TimeSpan.FromMilliseconds(3));

        server.Tick(19);

        ServerStatus status = server.Status.Current;
        Assert.That(status.Tick, Is.EqualTo(20u));
        Assert.That(status.ConnectedSessions, Is.EqualTo(2));
        Assert.That(status.InWorldSessions, Is.EqualTo(1));
        Assert.That(status.MalformedMessages, Is.EqualTo(1));
        Assert.That(status.IgnoredEvents, Is.EqualTo(1));
        Assert.That(status.InboundQueueDepth, Is.EqualTo(0));
        Assert.That(status.PlayersPerMap["map.training_ground"], Is.EqualTo(1));
        Assert.That(status.Transport.BytesSent, Is.EqualTo(2000));
        Assert.That(status.Transport.PacketsLost, Is.EqualTo(3));
        Assert.That(status.LastTickDuration, Is.EqualTo(TimeSpan.FromMilliseconds(3)));

        PlayerSummary player = status.Players.Single();
        Assert.That(player.Connection, Is.EqualTo(inWorld));
        Assert.That(player.Character, Is.EqualTo(new CharacterId(7)));
        Assert.That(player.Entity, Is.EqualTo(server.PlayerOf(inWorld).Id));
        Assert.That(player.Map.Value, Is.EqualTo("map.training_ground"));
        Assert.That(player.RoundTripMilliseconds, Is.EqualTo(42));
        Assert.That(player.StaleInputs, Is.EqualTo(1));
    }
}
}
