using System;
using System.Buffers.Binary;
using System.Linq;
using System.Reflection;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
/// A client that lies, floods, or sends garbage. Whatever arrives, the authoritative entity may only ever stand
/// where the grid lets a body stand, and may never cover more ground in a tick than its speed allows.
/// </summary>
[TestFixture]
public sealed class MovementAbuseTests
{
    private const float TickSeconds = 1f / TestServer.TickRate;
    private const float Tolerance = 1e-4f;

    private static readonly MapDefinitionId TrainingGround = new MapDefinitionId("map.training_ground");

    [Test]
    public void MoveIntent_HasNoFieldThatCouldStateAPositionOrASpeed()
    {
        string[] properties = typeof(MoveIntent)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(properties, Is.EqualTo(new[] { "ClientTick", "DirectionX", "DirectionZ", "Sequence" }));
        Assert.That(MoveInput.EncodedLength, Is.EqualTo(18), "opcode, sequence, client tick, and two floats");
    }

    [Test]
    public void InputFlood_BuysNoExtraDistance()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        PlayerEntity player = server.PlayerOf(connection);
        WorldPosition start = player.Position;
        uint sequence = 0;

        for (int tick = 0; tick < 20; tick++)
        {
            for (int burst = 0; burst < 200; burst++)
            {
                sequence++;
                server.SendMove(connection, sequence, 0f, -1f);
            }

            WorldPosition before = player.Position;
            server.Tick();
            AssertLegalStep(server, player, before);
        }

        float travelled = start.Z - player.Position.Z;
        Assert.That(travelled, Is.LessThanOrEqualTo((20 * player.MovementSpeed * TickSeconds) + Tolerance));
        Assert.That(travelled, Is.GreaterThan(0f), "the flood is not rewarded, but the player is not frozen either");
    }

    [TestCase(float.MaxValue, 0f)]
    [TestCase(1e30f, -1e30f)]
    [TestCase(-3.4e38f, 3.4e38f)]
    [TestCase(1000f, 0.001f)]
    public void EnormousDirection_MovesExactlyOneNormalStep(float directionX, float directionZ)
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        PlayerEntity player = server.PlayerOf(connection);
        WorldPosition before = player.Position;

        server.SendMove(connection, 1, directionX, directionZ);
        server.Tick();

        float moved = Distance(before, player.Position);
        Assert.That(moved, Is.EqualTo(player.MovementSpeed * TickSeconds).Within(1e-3f));
        AssertLegalStep(server, player, before);
    }

    [TestCase(1e-4f, 0f)]
    [TestCase(0f, -9e-4f)]
    [TestCase(1e-40f, 1e-40f)]
    [TestCase(-0f, 0f)]
    public void NegligibleDirection_MovesNothing(float directionX, float directionZ)
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        PlayerEntity player = server.PlayerOf(connection);
        WorldPosition before = player.Position;

        server.SendMove(connection, 1, directionX, directionZ);
        server.Tick(3);

        Assert.That(player.Position, Is.EqualTo(before));
    }

    [TestCase(float.NaN, 0f)]
    [TestCase(0f, float.NaN)]
    [TestCase(float.PositiveInfinity, 1f)]
    [TestCase(1f, float.NegativeInfinity)]
    public void NonFiniteDirectionBytes_AreMalformedAndMoveNothing(float directionX, float directionZ)
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        PlayerEntity player = server.PlayerOf(connection);
        WorldPosition before = player.Position;
        long malformedBefore = server.Inbound.Malformed;

        server.Inbound.OnPayload(connection, ProtocolChannel.Input, RawMove(1, 1, directionX, directionZ));
        server.Tick(10);

        Assert.That(server.Inbound.Malformed, Is.EqualTo(malformedBefore + 1));
        Assert.That(player.Position, Is.EqualTo(before));
        Assert.That(float.IsNaN(player.Position.X) || float.IsNaN(player.Position.Z), Is.False);
    }

    [Test]
    public void SteeringIntoAWall_ForSeconds_NeverEntersIt()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        PlayerEntity player = server.PlayerOf(connection);
        NavigationGrid grid = Grid(server);

        for (uint tick = 1; tick <= 400; tick++)
        {
            WorldPosition before = player.Position;
            server.SendMove(connection, tick, 1f, tick % 40 < 20 ? 0.6f : -0.6f);
            server.Tick();
            AssertLegalStep(server, player, before);
        }

        float eastWallInnerFace = grid.OriginX + ((grid.Columns - 1) * grid.CellSize);
        Assert.That(player.Position.X, Is.LessThanOrEqualTo(eastWallInnerFace - grid.AgentRadius + Tolerance));
        Assert.That(player.Position.X, Is.GreaterThan(eastWallInnerFace - 1.5f), "it really reached the wall");
    }

    [Test]
    public void SteeringOffThePlateauEdge_IsHeldAtTheEdge()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        PlayerEntity player = server.PlayerOf(connection);
        NavigationGrid grid = Grid(server);
        FindLedge(grid, out WorldPosition onTop, out float ledgeHeight);
        player.Position = onTop;

        for (uint tick = 1; tick <= 60; tick++)
        {
            WorldPosition before = player.Position;
            server.SendMove(connection, tick, 0f, -1f);
            server.Tick();
            AssertLegalStep(server, player, before);
        }

        Assert.That(ledgeHeight, Is.GreaterThan(grid.MaxStepHeight), "the fixture really is a ledge");
        Assert.That(player.Position.Y, Is.EqualTo(onTop.Y).Within(Tolerance), "still on top");
        Assert.That(player.Position.Z, Is.LessThan(onTop.Z), "it walked up to the edge");
    }

    [Test]
    public void InputsFromASessionThatNeverEnteredTheWorld_MoveNobody()
    {
        TestServer server = new TestServer();
        ConnectionId player = server.EnterWorld(7);
        ConnectionId lurker = server.Connect();
        server.SendHello(lurker);
        server.Tick();
        WorldPosition before = server.PlayerOf(player).Position;
        long ignoredBefore = server.SessionManager.IgnoredEvents;

        for (uint sequence = 1; sequence <= 50; sequence++)
        {
            server.SendMove(lurker, sequence, 1f, 0f);
        }

        server.Tick(5);

        Assert.That(server.PlayerOf(player).Position, Is.EqualTo(before));
        Assert.That(server.SessionManager.IgnoredEvents, Is.EqualTo(ignoredBefore + 50));
        Assert.That(server.World.Maps.Sum(map => map.Players.Count), Is.EqualTo(1));
    }

    [Test]
    public void MalformedBurst_DoesNotFaultTheTickOrMoveAnyone()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        PlayerEntity player = server.PlayerOf(connection);
        WorldPosition before = player.Position;
        Random random = new Random(20260921);
        long malformedBefore = server.Inbound.Malformed;

        for (int index = 0; index < 3000; index++)
        {
            byte[] garbage = new byte[random.Next(0, 40)];
            random.NextBytes(garbage);
            if (garbage.Length >= 2)
            {
                // Never a routable client opcode, so every payload here is one the server must refuse.
                garbage[1] = 0x7F;
            }

            server.Inbound.OnPayload(connection, (ProtocolChannel)random.Next(0, 3), garbage);
        }

        Action tick = () => server.Tick(3);

        Assert.That(tick, Throws.Nothing);
        Assert.That(server.Inbound.Malformed, Is.EqualTo(malformedBefore + 3000));
        Assert.That(player.Position, Is.EqualTo(before));

        server.SendMove(connection, 1, 1f, 0f);
        server.Tick();
        Assert.That(player.Position.X, Is.GreaterThan(before.X), "an honest input still works afterwards");
    }

    [Test]
    public void HostileRun_NeverLeavesWalkableGroundOrOutrunsItsSpeed()
    {
        TestServer server = new TestServer();
        ConnectionId connection = server.EnterWorld(7);
        PlayerEntity player = server.PlayerOf(connection);
        Random random = new Random(7);
        uint sequence = 0;
        float farthest = 0f;
        WorldPosition start = player.Position;

        for (int tick = 0; tick < 10000; tick++)
        {
            int messages = random.Next(0, 6);
            for (int index = 0; index < messages; index++)
            {
                sequence = NextHostileSequence(random, sequence);
                SendHostileMessage(server, connection, random, sequence);
            }

            WorldPosition before = player.Position;
            server.Tick();
            AssertLegalStep(server, player, before);
            farthest = Math.Max(farthest, Distance(start, player.Position));
        }

        Assert.That(farthest, Is.GreaterThan(5f), "the run really moved the entity around");
        Assert.That(server.Transport.Disconnects, Is.Empty, "abuse is counted in this milestone, not punished");
    }

    private static uint NextHostileSequence(Random random, uint current)
    {
        switch (random.Next(0, 10))
        {
            case 0:
                return current;
            case 1:
                return unchecked(current - (uint)random.Next(1, 50));
            case 2:
                return unchecked(current + (uint)random.Next(1000, 100000));
            default:
                return unchecked(current + 1);
        }
    }

    private static void SendHostileMessage(TestServer server, ConnectionId connection, Random random, uint sequence)
    {
        switch (random.Next(0, 8))
        {
            case 0:
                server.SendStop(connection, sequence);
                break;
            case 1:
                server.Inbound.OnPayload(
                    connection,
                    ProtocolChannel.Input,
                    RawMove(sequence, (uint)random.Next(), float.NaN, float.PositiveInfinity));
                break;
            case 2:
                server.Inbound.OnPayload(connection, ProtocolChannel.Control, RawMove(sequence, 0, 1f, 0f));
                break;
            case 3:
                server.SendMove(connection, sequence, (uint)random.Next(), 1e30f, -1e30f);
                break;
            default:
                float angle = (float)(random.NextDouble() * Math.PI * 2.0);
                float magnitude = (float)Math.Pow(10.0, random.Next(-3, 6));
                server.SendMove(
                    connection,
                    sequence,
                    (uint)random.Next(),
                    (float)Math.Cos(angle) * magnitude,
                    (float)Math.Sin(angle) * magnitude);
                break;
        }
    }

    private static void AssertLegalStep(TestServer server, PlayerEntity player, WorldPosition before)
    {
        NavigationGrid grid = Grid(server);
        WorldPosition after = player.Position;
        Assert.That(float.IsNaN(after.X) || float.IsNaN(after.Y) || float.IsNaN(after.Z), Is.False);
        Assert.That(grid.CanOccupy(after.X, after.Z), Is.True, "standing somewhere a body cannot stand: " + after);
        Assert.That(grid.TrySampleHeight(after.X, after.Z, out float ground), Is.True);
        Assert.That(after.Y, Is.EqualTo(ground).Within(Tolerance), "off the ground at " + after);
        float horizontal = Distance(before, after);
        Assert.That(
            horizontal,
            Is.LessThanOrEqualTo((player.MovementSpeed * TickSeconds) + Tolerance),
            "moved " + horizontal + " m in one tick from " + before + " to " + after);
    }

    private static NavigationGrid Grid(TestServer server)
    {
        return server.Content.Maps[TrainingGround].Navigation;
    }

    /// <summary>
    /// Finds a level raised cell whose southern neighbour is level ground too far below to step down to, and
    /// returns a starting point one cell further back on the same level, so there is room to walk up to the edge.
    /// </summary>
    private static void FindLedge(NavigationGrid grid, out WorldPosition onTop, out float ledgeHeight)
    {
        for (int row = 1; row < grid.Rows - 1; row++)
        {
            for (int column = 0; column < grid.Columns; column++)
            {
                NavigationCell upper = grid.GetCell(column, row);
                NavigationCell lower = grid.GetCell(column, row - 1);
                bool isLedge = upper.IsWalkable && lower.IsWalkable
                    && upper.Axis == RampAxis.None && lower.Axis == RampAxis.None
                    && upper.HeightAtMin - lower.HeightAtMin > grid.MaxStepHeight;
                if (isLedge && grid.GetCell(column, row + 1) == upper)
                {
                    WorldPosition center = grid.GetCellCenter(column, row + 1);
                    onTop = new WorldPosition(center.X, upper.HeightAtMin, center.Z);
                    ledgeHeight = upper.HeightAtMin - lower.HeightAtMin;
                    return;
                }
            }
        }

        throw new InvalidOperationException("The training ground has no ledge to test against.");
    }

    private static byte[] RawMove(uint sequence, uint clientTick, float directionX, float directionZ)
    {
        byte[] payload = new byte[MoveInput.EncodedLength];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)MessageOpcode.MoveInput);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(2), sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(6), clientTick);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(10), directionX);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(14), directionZ);
        return payload;
    }

    private static float Distance(WorldPosition from, WorldPosition to)
    {
        float deltaX = to.X - from.X;
        float deltaZ = to.Z - from.Z;
        return (float)Math.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));
    }
}
}
