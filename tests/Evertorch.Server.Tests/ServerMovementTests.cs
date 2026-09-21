using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
/// Players enter at the training ground spawn point (0, 0, 0) with speed 5, so one 20 Hz tick moves 0.25 m. East of
/// the spawn point the ground is open for more than 20 m.
/// </summary>
[TestFixture]
public sealed class ServerMovementTests
{
    private const float StepLength = 0.25f;
    private const float Tolerance = 1e-4f;

    [Test]
    public void MoveInput_MovesThePlayerOneStepPerTickAtItsAuthoritativeSpeed()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        server.SendMove(connection, 1, 1f, 0f);

        server.Tick();

        PlayerEntity player = server.PlayerOf(connection);
        Assert.That(player.Position.X, Is.EqualTo(StepLength).Within(Tolerance));
        Assert.That(player.Position.Z, Is.EqualTo(0f));
        Assert.That(player.VelocityX, Is.EqualTo(5f).Within(1e-3f));
        Assert.That(player.Facing, Is.EqualTo(new WorldDirection(1f, 0f)));
        Assert.That(player.StateFlags, Is.EqualTo(EntityStateFlags.Moving));
    }

    [TestCase(1000f, 0f)]
    [TestCase(0.01f, 0f)]
    [TestCase(3.4e38f, 0f)]
    public void MoveInput_WhateverTheDirectionMagnitude_MovesTheSameAsAUnitDirection(float directionX, float directionZ)
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        server.SendMove(connection, 1, directionX, directionZ);

        server.Tick();

        Assert.That(server.PlayerOf(connection).Position.X, Is.EqualTo(StepLength).Within(Tolerance));
    }

    [Test]
    public void MoveInput_Diagonal_CoversTheSameDistanceAsAStraightOne()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        server.SendMove(connection, 1, 1f, 1f);

        server.Tick();

        WorldPosition position = server.PlayerOf(connection).Position;
        double distance = System.Math.Sqrt((position.X * position.X) + (position.Z * position.Z));
        Assert.That(distance, Is.EqualTo(StepLength).Within(Tolerance));
    }

    [Test]
    public void MoveInput_WithOlderSequence_IsIgnored()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        server.SendMove(connection, 5, 1f, 0f);
        server.Tick();

        server.SendMove(connection, 3, -1f, 0f);
        server.Tick();

        Assert.That(server.PlayerOf(connection).Position.X, Is.EqualTo(2 * StepLength).Within(Tolerance));
        Assert.That(InputOf(server, connection).Queue.Stale, Is.EqualTo(1));
        Assert.That(InputOf(server, connection).LastProcessedSequence, Is.EqualTo(5u));
    }

    [Test]
    public void MoveInput_Duplicate_IsIgnored()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        server.SendMove(connection, 1, 1f, 0f);
        server.SendMove(connection, 1, 1f, 0f);
        server.SendMove(connection, 1, 1f, 0f);

        server.Tick(2);

        Assert.That(InputOf(server, connection).Queue.Stale, Is.EqualTo(2));
        Assert.That(InputOf(server, connection).Queue.Count, Is.EqualTo(0));
    }

    [Test]
    public void MoveInput_WhenTheSequenceWrapsAround_IsStillNewer()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        server.SendMove(connection, uint.MaxValue, 1f, 0f);
        server.Tick();

        server.SendMove(connection, 0, -1f, 0f);
        server.Tick();

        Assert.That(InputOf(server, connection).LastProcessedSequence, Is.EqualTo(0u));
        Assert.That(server.PlayerOf(connection).Position.X, Is.EqualTo(0f).Within(Tolerance));
    }

    [Test]
    public void Inputs_ArrivingTogether_AreAppliedOnePerTick()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        server.SendMove(connection, 1, 1f, 0f);
        server.SendMove(connection, 2, 1f, 0f);
        server.SendMove(connection, 3, 1f, 0f);

        server.Tick();
        float afterOneTick = server.PlayerOf(connection).Position.X;
        uint acknowledgedAfterOneTick = InputOf(server, connection).LastProcessedSequence;
        server.Tick(2);

        Assert.That(afterOneTick, Is.EqualTo(StepLength).Within(Tolerance));
        Assert.That(acknowledgedAfterOneTick, Is.EqualTo(1u));
        Assert.That(server.PlayerOf(connection).Position.X, Is.EqualTo(3 * StepLength).Within(Tolerance));
        Assert.That(InputOf(server, connection).LastProcessedSequence, Is.EqualTo(3u));
    }

    [Test]
    public void Inputs_BeyondQueueCapacity_DropTheOldest()
    {
        TestServer server = new TestServer(maxQueuedInputs: 8);
        ConnectionId connection = server.EnterWorld(1);
        for (uint sequence = 1; sequence <= 12; sequence++)
        {
            server.SendMove(connection, sequence, 1f, 0f);
        }

        server.Tick();

        Assert.That(InputOf(server, connection).Queue.Dropped, Is.EqualTo(4));
        Assert.That(InputOf(server, connection).LastProcessedSequence, Is.EqualTo(5u));
        Assert.That(server.PlayerOf(connection).Position.X, Is.EqualTo(StepLength).Within(Tolerance));
    }

    [Test]
    public void Inputs_WhenMissing_HoldLastDirectionUntilTimeoutThenStop()
    {
        TestServer server = new TestServer(inputHoldTimeoutMs: 250);
        ConnectionId connection = server.EnterWorld(1);
        server.SendMove(connection, 1, 1f, 0f);

        server.Tick(6);
        float whileHeld = server.PlayerOf(connection).Position.X;
        server.Tick(10);

        PlayerEntity player = server.PlayerOf(connection);
        Assert.That(whileHeld, Is.EqualTo(6 * StepLength).Within(Tolerance), "one applied tick plus five held ticks");
        Assert.That(player.Position.X, Is.EqualTo(6 * StepLength).Within(Tolerance));
        Assert.That(player.StateFlags, Is.EqualTo(EntityStateFlags.None));
        Assert.That(player.VelocityX, Is.EqualTo(0f));
    }

    [Test]
    public void StopMovement_StopsEntityAndAdvancesAcknowledgement()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        server.SendMove(connection, 1, 1f, 0f);
        server.Tick();
        server.SendStop(connection, 2);

        server.Tick(3);

        PlayerEntity player = server.PlayerOf(connection);
        Assert.That(player.Position.X, Is.EqualTo(StepLength).Within(Tolerance));
        Assert.That(player.StateFlags, Is.EqualTo(EntityStateFlags.None));
        Assert.That(player.Facing, Is.EqualTo(new WorldDirection(1f, 0f)), "stopping keeps the last facing");
        Assert.That(InputOf(server, connection).LastProcessedSequence, Is.EqualTo(2u));
    }

    [Test]
    public void MoveInput_BeforeWorldEntered_IsIgnored()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.Connect();
        server.SendHello(connection);
        server.SendMove(connection, 1, 1f, 0f);

        server.Tick();

        Assert.That(server.SessionManager.IgnoredEvents, Is.EqualTo(1));
        Assert.That(server.World.Maps.Single().Players, Is.Empty);
    }

    [Test]
    public void MoveInput_OnTheControlChannel_IsMalformed()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        byte[] payload = new byte[MoveInput.EncodedLength];
        new MoveInput(new MoveIntent(1, 1, 1f, 0f)).Write(payload);

        server.Inbound.OnPayload(connection, ProtocolChannel.Control, payload);
        server.Tick();

        Assert.That(server.Inbound.Malformed, Is.EqualTo(1));
        Assert.That(server.PlayerOf(connection).Position.X, Is.EqualTo(0f));
    }

    [Test]
    public void MoveInput_WithTickDriftBeyondBound_RebasesAndCounts()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        server.SendMove(connection, 1, 100, 1f, 0f);
        server.Tick();
        server.SendMove(connection, 2, 101, 1f, 0f);
        server.Tick();
        long rebasesWhileInStep = InputOf(server, connection).TickDriftRebases;

        server.SendMove(connection, 3, 5000, 1f, 0f);
        server.Tick();

        Assert.That(rebasesWhileInStep, Is.EqualTo(0));
        Assert.That(InputOf(server, connection).TickDriftRebases, Is.EqualTo(1));
        Assert.That(
            server.PlayerOf(connection).Position.X,
            Is.EqualTo(3 * StepLength).Within(Tolerance),
            "the client clock is advisory");
    }

    [Test]
    public void Movement_TowardsAnObstacle_NeverEntersIt()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(1);
        NavigationGrid grid = server.Content.Maps.Values.Single().Navigation;

        for (uint sequence = 1; sequence <= 80; sequence++)
        {
            server.SendMove(connection, sequence, 1f, 1f);
            server.Tick();
            WorldPosition position = server.PlayerOf(connection).Position;
            Assert.That(grid.CanOccupy(position.X, position.Z), Is.True, "tick " + sequence + " at " + position);
        }

        WorldPosition end = server.PlayerOf(connection).Position;
        bool isInsideObstacle = end.X > 4f && end.X < 6f && end.Z > 4f && end.Z < 6f;
        Assert.That(isInsideObstacle, Is.False);
        Assert.That(
            end.X,
            Is.GreaterThan(3f),
            "it should have reached the obstacle on the diagonal from the spawn point");
    }

    private static PlayerInputState InputOf(TestServer server, ConnectionId connection)
    {
        server.Sessions.TryGet(connection, out ClientSession? session);
        return session!.Input!;
    }
}
}
