using System.Linq;
using Evertorch.Client;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The client's real prediction code against the server's real tick pipeline, joined by a seeded link that delays,
///     drops, and reorders. After the player stops, the client must end up exactly where the server has the entity.
/// </summary>
[TestFixture]
public sealed class ConvergenceTests
{
    private const int EnterWorldLimitMs = 5000;
    private const int ConvergenceLimitMs = 1000;
    private const int WalkLimitMs = 20000;
    private const float ConvergedDistance = 1e-3f;

    private static string RunSeeded(int seed)
    {
        var rig = new ClientServerRig();
        SimulatedClient client = rig.AddClient(7, seed, 17);
        client.Link.LatencyMilliseconds = 50;
        client.Link.JitterMilliseconds = 20;
        client.Link.LossPercent = 20;
        client.Link.ReorderPercent = 10;
        EnterWorld(rig, client);
        rig.Advance(1200, simulated => simulated.Controller!.SetManualDirection(1f, 0.4f));

        // The predicted position alone would not do: prediction is local, so it is the same whatever the link loses.
        return client.World.Predictor.Position + " server " + rig.Server.PlayerOf(client.Loopback.Connection).Position
            + " dropped " + client.Link.Dropped + " reordered " + client.Link.Reordered + " stale "
            + client.World.StaleSnapshots + " ack " + client.World.Predictor.LastAcknowledgedSequence;
    }

    private static void EnterWorld(ClientServerRig rig, SimulatedClient client)
    {
        rig.ConnectAll();
        int entered = rig.AdvanceUntil(
            () => client.Connection.State == ClientConnectionState.InWorld && client.Controller != null,
            EnterWorldLimitMs);
        Assert.That(entered, Is.GreaterThanOrEqualTo(0), "entered the world: " + client.Connection.LocalError);
    }

    /// <summary>
    ///     Three held directions that run into obstacles, a stop, then a click walk back to the spawn point and its stop.
    ///     Both stops must converge within the limit.
    /// </summary>
    private static RunResult Run(ClientServerRig rig, SimulatedClient client)
    {
        rig.Advance(1500, simulated => simulated.Controller!.SetManualDirection(1f, 0.3f));
        rig.Advance(1000, simulated => simulated.Controller!.SetManualDirection(-0.2f, 1f));
        rig.Advance(1000, simulated => simulated.Controller!.SetManualDirection(-1f, -1f));
        int manualStop = AwaitConvergence(rig, client);

        bool isWalking = client.Controller!.TryMoveTo(client.World.Predictor.Position, new WorldPosition(0f, 0f, 0f));
        Assert.That(isWalking, Is.True, "the spawn point is reachable from wherever the run ended");
        int walked = rig.AdvanceUntil(
            () => !client.Controller.HasPath,
            WalkLimitMs,
            simulated => simulated.Controller!.SetManualDirection(0f, 0f));
        Assert.That(walked, Is.GreaterThanOrEqualTo(0), "the walk ended");
        int walkStop = AwaitConvergence(rig, client);

        WorldPosition end = rig.Server.PlayerOf(client.Loopback.Connection).Position;
        Assert.That(end.X, Is.EqualTo(0f).Within(0.5f), "the server has the player back at the spawn point");
        Assert.That(end.Z, Is.EqualTo(0f).Within(0.5f));
        return new RunResult(manualStop, walkStop);
    }

    private static int AwaitConvergence(ClientServerRig rig, SimulatedClient client)
    {
        int converged = rig.AdvanceUntil(
            () => client.World.Predictor.PendingCount == 0
                && client.DistanceTo(rig.Server.PlayerOf(client.Loopback.Connection).Position) <= ConvergedDistance,
            ConvergenceLimitMs,
            simulated => simulated.Controller!.SetManualDirection(0f, 0f));
        float distance = client.DistanceTo(rig.Server.PlayerOf(client.Loopback.Connection).Position);
        Assert.That(
            converged,
            Is.GreaterThanOrEqualTo(0),
            "not converged after " + ConvergenceLimitMs + " ms: " + client.World.Predictor.PendingCount
            + " pending inputs, " + distance + " m from the server");

        // Converged means it stays converged: nothing further arrives that moves the client again.
        rig.Advance(500, simulated => simulated.Controller!.SetManualDirection(0f, 0f));
        float settled = client.DistanceTo(rig.Server.PlayerOf(client.Loopback.Connection).Position);
        Assert.That(settled, Is.LessThanOrEqualTo(ConvergedDistance), "still converged half a second later");
        return converged;
    }

    private readonly struct RunResult
    {
        public RunResult(int manualStopConvergenceMs, int walkConvergenceMs)
        {
            ManualStopConvergenceMs = manualStopConvergenceMs;
            WalkConvergenceMs = walkConvergenceMs;
        }

        public int ManualStopConvergenceMs { get; }

        public int WalkConvergenceMs { get; }
    }

    [Test]
    [Combinatorial]
    public void BadLink_ClientConvergesOnTheServerWithinASecondOfStopping(
        [Values(25, 50, 100)] int oneWayLatencyMs,
        [Values(5, 10, 20)] int lossPercent,
        [Values(1, 2, 3, 4, 5)] int seed)
    {
        var rig = new ClientServerRig();
        SimulatedClient client = rig.AddClient(7, seed, 17);
        client.Link.LatencyMilliseconds = oneWayLatencyMs;
        client.Link.JitterMilliseconds = oneWayLatencyMs / 5;
        client.Link.LossPercent = lossPercent;
        client.Link.ReorderPercent = 5;
        EnterWorld(rig, client);

        RunResult result = Run(rig, client);

        TestContext.Out.WriteLine(
            "latency " + oneWayLatencyMs + " ms each way, loss " + lossPercent + " %, seed " + seed
            + ": largest correction " + client.World.Smoother.LargestCorrection.ToString("F3")
            + " m, converged " + result.ManualStopConvergenceMs + " ms after the manual stop and "
            + result.WalkConvergenceMs + " ms after the walk, dropped " + client.Link.Dropped
            + ", reordered " + client.Link.Reordered + ", stale snapshots " + client.World.StaleSnapshots);
        Assert.That(client.Link.Dropped, Is.GreaterThan(0), "the link really lost messages");
        Assert.That(client.World.Smoother.Snaps, Is.EqualTo(0), "no correction was large enough to teleport");
        Assert.That(client.World.Smoother.LargestCorrection, Is.LessThan(RenderSmoother.TeleportThreshold));
    }

    [Test]
    public void PerfectLink_PredictionIsNeverCorrected()
    {
        var rig = new ClientServerRig();
        SimulatedClient client = rig.AddClient(7, 1, 17);
        EnterWorld(rig, client);

        Run(rig, client);

        Assert.That(client.World.Smoother.LargestCorrection, Is.LessThan(1e-4f));
        Assert.That(client.World.Smoother.Snaps, Is.EqualTo(0));
    }

    [Test]
    public void ReorderedInput_IsCountedStaleByTheServerAndStillConverges()
    {
        var rig = new ClientServerRig();
        SimulatedClient client = rig.AddClient(7, 3, 17);
        client.Link.LatencyMilliseconds = 40;
        client.Link.ReorderPercent = 30;
        EnterWorld(rig, client);

        Run(rig, client);

        rig.Advance(1000);
        Assert.That(client.Link.Reordered, Is.GreaterThan(0));
        Assert.That(rig.Server.Status.Current.Players.Single().StaleInputs, Is.GreaterThan(0));
    }

    [Test]
    public void SameSeed_GivesTheSameRunBitForBit()
    {
        string first = RunSeeded(11);
        string second = RunSeeded(11);
        string other = RunSeeded(12);

        Assert.That(second, Is.EqualTo(first));
        Assert.That(other, Is.Not.EqualTo(first), "a different seed loses different messages");
    }

    [Test]
    public void SecondClient_SeesTheFirstWhereTheServerHasIt()
    {
        var rig = new ClientServerRig();
        SimulatedClient mover = rig.AddClient(7, 1, 17);
        SimulatedClient watcher = rig.AddClient(8, 2, 31);
        foreach (SimulatedClient client in new[] { mover, watcher })
        {
            client.Link.LatencyMilliseconds = 50;
            client.Link.LossPercent = 10;
        }

        rig.ConnectAll();
        int entered = rig.AdvanceUntil(
            () => mover.Connection.State == ClientConnectionState.InWorld
                && watcher.Connection.State == ClientConnectionState.InWorld,
            EnterWorldLimitMs);
        Assert.That(entered, Is.GreaterThanOrEqualTo(0));

        rig.Advance(2000, client => client.Controller!.SetManualDirection(client == mover ? 1f : 0f, 0f));
        rig.Advance(1500, client => client.Controller!.SetManualDirection(0f, 0f));

        PlayerEntity moverOnServer = rig.Server.PlayerOf(mover.Loopback.Connection);
        RemoteEntity seen = watcher.World.Remotes[moverOnServer.Id];
        bool hasPose =
            seen.Buffer.TrySample(watcher.World.RemoteRenderTime, out WorldPosition drawn, out WorldDirection _);
        Assert.That(hasPose, Is.True);
        Assert.That(moverOnServer.Position.X, Is.GreaterThan(3f), "the mover really went somewhere");
        Assert.That(drawn.X, Is.EqualTo(moverOnServer.Position.X).Within(ConvergedDistance));
        Assert.That(drawn.Z, Is.EqualTo(moverOnServer.Position.Z).Within(ConvergedDistance));
        Assert.That(watcher.World.Remotes.Count, Is.EqualTo(1));
    }
}
}
