using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     Unity's runtime on one side and the real .NET server process on the other, over a simulated bad link. This is
///     the one place that proves both runtimes compute the same movement while messages are delayed and lost.
/// </summary>
public sealed class LiveServerConvergenceTests
{
    private const float StartTimeoutSeconds = 30f;
    private const float StepTimeoutSeconds = 15f;
    private const float ConvergedDistance = 1e-3f;
    private const int TestTimeoutMs = 120_000;

    private LiveDatabase? m_database;
    private LiveServer? m_server;
    private LiteNetLibClientTransport? m_socket;

    [TearDown]
    public void StopEverything()
    {
        m_socket?.Dispose();
        m_socket = null;
        m_server?.Dispose();
        m_server = null;
        m_database?.Dispose();
        m_database = null;
    }

    [UnityTest]
    [Timeout(TestTimeoutMs)]
    public IEnumerator Client_OnABadLink_EndsWhereTheServerProcessSaysItIs()
    {
        if (!LiveServer.IsBuilt())
        {
            Assert.Inconclusive(LiveServer.MissingPrerequisites);
        }

        string? mismatch = LiveServer.ContentMismatch();
        if (mismatch != null)
        {
            Assert.Fail(mismatch);
        }

        // A package that is present but refused is a defect, not a missing prerequisite.
        ClientContent? content = LiveServer.LoadClientContent(out string contentError);
        Assert.That(content, Is.Not.Null, contentError);

        Task<LiveDatabase> starting = LiveDatabase.StartAsync();
        yield return new WaitUntil(() => starting.IsCompleted);
        Assert.That(starting.IsFaulted, Is.False, starting.Exception?.GetBaseException().Message);
        LiveDatabase database = m_database = starting.Result;

        LiveServer server = m_server = new LiveServer();
        server.Start(database);
        yield return WaitUntil(() => server.TryReadListeningPort(out int _), StartTimeoutSeconds);
        Assert.That(server.TryReadListeningPort(out int port), Is.True, $"server output: {server.JoinOutput()}");

        m_socket = new LiteNetLibClientTransport("evertorch", 5000);
        var link = new LossyTransport(m_socket, 9, () => Time.realtimeSinceStartupAsDouble)
        {
            LatencyMilliseconds = 60,
            JitterMilliseconds = 15,
            LossPercent = 10,
            ReorderPercent = 5
        };
        var connection = new ClientConnection(
            link,
            new ClientConnectionSettings(ProtocolConstants.BuildVersion, content!.Version, "dev:playmode"),
            content);
        var picker = new CharacterPicker("Live31");
        connection.Connect("127.0.0.1", port);
        yield return WaitUntil(
            () =>
            {
                connection.Poll();
                picker.Poll(connection);
                return connection.State == ClientConnectionState.InWorld;
            },
            StepTimeoutSeconds);
        Assert.That(
            connection.State,
            Is.EqualTo(ClientConnectionState.InWorld),
            $"{connection.LocalError} {connection.DisconnectCause} server output: {server.JoinOutput()}");

        ClientWorld world = connection.World!;
        var controller = new MovementController(world.Grid);
        var driver = new LocalPlayerDriver(controller, new MoveIntentProducer(), world, connection);
        var clock = new FixedTickClock(1f / connection.ServerTickRate);

        controller.SetManualDirection(1f, 0.5f);
        yield return Simulate(connection, driver, world, clock, 1.5f, () => false);
        controller.SetManualDirection(0f, 0f);
        Func<bool> isAcknowledged = () => world.Predictor.PendingCount == 0;
        yield return Simulate(connection, driver, world, clock, StepTimeoutSeconds, isAcknowledged);
        Assert.That(world.Predictor.PendingCount, Is.EqualTo(0), "every input was acknowledged or forgotten");

        // The server republishes what its console reads once a second, so wait out one full period of quiet.
        yield return Simulate(connection, driver, world, clock, 1.5f, () => false);
        server.ClearOutput();

        long character = connection.Characters.Single(entry => entry.Name == picker.Name).Character.Value;
        server.SendCommand("players");
        yield return WaitUntil(() => server.HasOutput(LiveServer.CharacterMarker(character)), StepTimeoutSeconds);

        WorldPosition predicted = world.Predictor.Position;
        Assert.That(
            server.TryReadPlayerPosition(character, out float serverX, out float serverZ),
            Is.True,
            server.JoinOutput());
        Assert.That(predicted.X, Is.GreaterThan(3f), "the player really walked");
        Assert.That(link.Dropped, Is.GreaterThan(0), "the link really lost messages");
        Assert.That(world.Smoother.Snaps, Is.EqualTo(0));
        Assert.That(
            predicted.X,
            Is.EqualTo(serverX).Within(ConvergedDistance),
            $"server output: {server.JoinOutput()}");
        Assert.That(predicted.Z, Is.EqualTo(serverZ).Within(ConvergedDistance));
        Debug.Log(
            $"Live convergence: client {predicted}, server ({serverX}, {serverZ}), largest correction "
            + $"{world.Smoother.LargestCorrection} m, dropped {link.Dropped}, reordered {link.Reordered}");
    }

    private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
    }

    private static IEnumerator Simulate(
        ClientConnection connection,
        LocalPlayerDriver driver,
        ClientWorld world,
        FixedTickClock clock,
        float seconds,
        Func<bool> isDone)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline && !isDone())
        {
            connection.Poll();
            int due = clock.Advance(Time.unscaledDeltaTime);
            for (int index = 0; index < due; index++)
            {
                driver.Tick(clock.NextTick());
            }

            world.Advance(Time.unscaledDeltaTime);
            yield return null;
        }
    }
}
}
