using System.Linq;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A character is on one map, never on two or none (verification line V5; Gameplay Systems §4.2, Persistence §6):
///     standing in the training ground's portal while its connection closes, it logs out, a pickup is in flight, the
///     database is down, it is kicked, or the server shuts down, it is on exactly one map after every tick while it is
///     in the world and on none once it has left, and its checkpoint names the map it was on.
/// </summary>
[TestFixture]
public sealed class MapPresenceTests
{
    private const long Character = 1;

    private static readonly MapDefinitionId Ground = new("map.training_ground");
    private static readonly MapDefinitionId Field = new("map.training_field");
    private static readonly int ViolationsToClose = new AbuseOptions().ViolationThreshold / ViolationScore.Points;

    private static TestServer Watched(TestServer server)
    {
        server.AfterEachTick = () => Assert.That(
            server.World.Maps.Count(map => map.Players.Any(player => player.Character.Value == Character)),
            Is.EqualTo(IsInTheWorld(server) ? 1 : 0),
            $"maps holding the character after tick {server.CurrentTick}");
        return server;
    }

    private static bool IsInTheWorld(TestServer server)
    {
        return server.Sessions.TryGetCharacter(new CharacterId(Character), out _);
    }

    private static MapDefinitionId? MapOfTheCharacter(TestServer server)
    {
        return server.World.Maps
            .Where(map => map.Players.Any(player => player.Character.Value == Character))
            .Select(map => (MapDefinitionId?)map.Definition.Id)
            .SingleOrDefault();
    }

    private static MapInstance GroundOf(TestServer server)
    {
        server.World.TryGetMap(Ground, out MapInstance? ground);
        return ground!;
    }

    private static void StandInThePortal(TestServer server, ConnectionId player)
    {
        server.PlayerOf(player).Position = GroundOf(server).Definition.Portals.Single().Center;
    }

    private static MapDefinitionId LastSavedMap(TestServer server)
    {
        return server.Store.Checkpoints.Last(checkpoint => checkpoint.CharacterId == Character).Map;
    }

    // A pickup whose commit waits until the test lets persistence run again.
    private static void PickUpWithoutPersistence(TestServer server, ConnectionId player)
    {
        ItemDropEntity drop = server.World.SpawnItemDrop(
            GroundOf(server),
            new ItemDefinitionId("item.material.slime_gel"),
            1,
            server.PlayerOf(player).Position,
            server.CurrentTick,
            long.MaxValue,
            default);
        server.Tick();
        server.RunsPersistence = false;
        server.SendPickup(player, drop.Id, 1);
        server.Tick();
        Assert.That(server.SessionOf(player).Character!.Operation, Is.Not.Null, "the pickup is in flight");
    }

    [Test]
    public void ConnectionClosed_InThePortalWithGrace_WaitsOnTheGround_AndCrossesOnceAConnectionAttaches()
    {
        TestServer server = Watched(new TestServer(withEveryMap: true, reconnectGraceMs: 30000));
        ConnectionId player = server.EnterWorld(Character);

        StandInThePortal(server, player);
        server.Disconnect(player);
        server.Tick(5);
        MapDefinitionId? retainedOn = MapOfTheCharacter(server);
        server.EnterWorld(Character);
        server.Tick(2);

        Assert.That(retainedOn, Is.EqualTo(Ground), "no connection controls it, so it does not cross");
        Assert.That(MapOfTheCharacter(server), Is.EqualTo(Field));
    }

    [Test]
    public void ConnectionClosed_InThePortalWithoutGrace_LeavesFromTheGround()
    {
        TestServer server = Watched(new TestServer(withEveryMap: true));
        ConnectionId player = server.EnterWorld(Character);

        StandInThePortal(server, player);
        server.Disconnect(player);
        server.Tick(3);

        Assert.That(IsInTheWorld(server), Is.False);
        Assert.That(LastSavedMap(server), Is.EqualTo(Ground));
    }

    [Test]
    public void Kick_InThePortal_NeverCrosses_AndLeavesFromTheGroundDespiteTheGracePeriod()
    {
        TestServer server = Watched(new TestServer(withEveryMap: true, reconnectGraceMs: 30000));
        ConnectionId player = server.EnterWorld(Character);

        StandInThePortal(server, player);
        for (int index = 0; index < ViolationsToClose; index++)
        {
            server.Inbound.OnPayload(player, ProtocolChannel.Control, new byte[] { 0xFF, 0x7F, 0x01 });
        }

        server.Tick(3);

        Assert.That(server.Transport.Disconnects[player], Is.EqualTo(DisconnectReason.Kicked));
        Assert.That(IsInTheWorld(server), Is.False, "expelled, so not retained");
        Assert.That(LastSavedMap(server), Is.EqualTo(Ground));
    }

    [Test]
    public void Logout_InThePortal_NeverCrosses_AndLeavesFromTheGround()
    {
        TestServer server = Watched(new TestServer(withEveryMap: true));
        ConnectionId player = server.EnterWorld(Character);

        StandInThePortal(server, player);
        server.SendLogout(player, 1);
        server.Tick(5);

        Assert.That(server.Transport.ControlOpcodesSentTo(player), Has.Some.EqualTo(MessageOpcode.LogoutComplete));
        Assert.That(IsInTheWorld(server), Is.False);
        Assert.That(LastSavedMap(server), Is.EqualTo(Ground));
    }

    [Test]
    public void Outage_DuringACrossing_KeepsTheCharacterOnTheField_AndItsCheckpointLandsOnceTheDatabaseAnswers()
    {
        TestServer server = Watched(new TestServer(withEveryMap: true));
        ConnectionId player = server.EnterWorld(Character);
        server.Store.IsUnavailable = true;

        StandInThePortal(server, player);
        server.Tick(5);
        MapDefinitionId? crossedTo = MapOfTheCharacter(server);
        bool isSavedDuringTheOutage = server.Store.Checkpoints.Any(checkpoint => checkpoint.Map == Field);
        server.Store.IsUnavailable = false;
        server.Tick(3);

        Assert.That(crossedTo, Is.EqualTo(Field), "a crossing does not wait for the database");
        Assert.That(isSavedDuringTheOutage, Is.False);
        Assert.That(LastSavedMap(server), Is.EqualTo(Field));
        Assert.That(server.Store.Stored(Character).MapDefinitionId, Is.EqualTo(Field.Value));
    }

    [Test]
    public void PickupInFlight_InThePortal_WaitsOnTheGround_AndCrossesOnceItSettles()
    {
        TestServer server = Watched(new TestServer(withEveryMap: true));
        ConnectionId player = server.EnterWorld(Character);
        PickUpWithoutPersistence(server, player);

        StandInThePortal(server, player);
        server.Tick(5);
        MapDefinitionId? waitedOn = MapOfTheCharacter(server);
        server.RunsPersistence = true;
        server.Tick(3);

        Assert.That(waitedOn, Is.EqualTo(Ground));
        Assert.That(MapOfTheCharacter(server), Is.EqualTo(Field));
        Assert.That(server.Store.Stored(Character).Items.Single().Quantity, Is.EqualTo(1));
    }

    [Test]
    public void PickupInFlight_WhenTheConnectionClosesInThePortal_LeavesFromTheGroundOnceItSettles()
    {
        TestServer server = Watched(new TestServer(withEveryMap: true));
        ConnectionId player = server.EnterWorld(Character);
        PickUpWithoutPersistence(server, player);

        StandInThePortal(server, player);
        server.Disconnect(player);
        server.Tick(5);
        MapDefinitionId? waitedOn = MapOfTheCharacter(server);
        server.RunsPersistence = true;
        server.Tick(3);

        Assert.That(waitedOn, Is.EqualTo(Ground), "its removal waits for the pickup, and it cannot cross unconnected");
        Assert.That(IsInTheWorld(server), Is.False);
        Assert.That(LastSavedMap(server), Is.EqualTo(Ground));
        Assert.That(server.Store.Stored(Character).Items.Single().Quantity, Is.EqualTo(1));
    }

    [Test]
    public void Shutdown_RightAfterACrossing_SavesTheCharacterOnTheField()
    {
        TestServer server = Watched(new TestServer(withEveryMap: true));
        ConnectionId player = server.EnterWorld(Character);
        server.RunsPersistence = false;

        StandInThePortal(server, player);
        server.Tick();
        server.Place(player, 0f, 0f);
        server.Lifetime.CheckpointAll();
        server.Persistence.RunUntilIdle();

        StoredCharacter stored = server.Store.Stored(Character);
        Assert.That(MapOfTheCharacter(server), Is.EqualTo(Field));
        Assert.That(stored.MapDefinitionId, Is.EqualTo(Field.Value));
        Assert.That(
            (stored.Position.X, stored.Position.Z),
            Is.EqualTo((0f, 0f)),
            "the shutdown's checkpoint replaced the crossing's, still waiting");
    }
}
}
