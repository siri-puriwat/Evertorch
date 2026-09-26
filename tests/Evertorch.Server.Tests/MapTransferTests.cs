using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Crossing between the training ground and the training field (Gameplay Systems §4.2; Network Protocol §3, §10,
///     §12): who crosses and when, what the crossing ends, what everyone is told, and the map epoch on movement input.
/// </summary>
[TestFixture]
public sealed class MapTransferTests
{
    private static readonly MapDefinitionId Ground = new("map.training_ground");
    private static readonly MapDefinitionId Field = new("map.training_field");

    private static MapInstance MapOf(TestServer server, MapDefinitionId id)
    {
        server.World.TryGetMap(id, out MapInstance? map);
        return map!;
    }

    private static MapPortal PortalOf(TestServer server, MapDefinitionId id)
    {
        return MapOf(server, id).Definition.Portals.Single();
    }

    private static void StandIn(TestServer server, ConnectionId player, MapPortal portal)
    {
        server.PlayerOf(player).Position = portal.Center;
    }

    private static CharacterSession CharacterOf(TestServer server, ConnectionId player)
    {
        return server.SessionOf(player).Character!;
    }

    private static List<WorldEntered> WorldEnteredOf(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.WorldEntered)
            .Select(message => WorldEntered.TryRead(message.Payload, out WorldEntered? read) ? read! : null)
            .Where(message => message != null)
            .Select(message => message!)
            .ToList();
    }

    [Test]
    public void Crossing_KeepsTheCommandSequence_AndEndsTheTargetAttackAndCast()
    {
        var server = new TestServer(withEveryMap: true, withMonsters: true, withMonsterAi: false);
        ConnectionId player = server.EnterWorld(1);
        PlayerEntity entity = server.PlayerOf(player);
        MonsterEntity slime = server.MonstersNear(entity.Position).First();
        server.SendAttack(player, slime.Id, 7);
        server.Tick(2);
        Assert.That(entity.Target, Is.EqualTo(slime.Id));

        StandIn(server, player, PortalOf(server, Ground));
        server.Tick(2);

        Assert.That(CharacterOf(server, player).Map.Definition.Id, Is.EqualTo(Field));
        Assert.That(entity.Target, Is.EqualTo(default(EntityId)));
        Assert.That(entity.Combat.IsAutoAttacking || entity.Combat.IsSwinging || entity.Combat.IsCasting, Is.False);
        Assert.That(WorldEnteredOf(server, player).Last().LastCommandSequence, Is.EqualTo(7u));
    }

    [Test]
    public void Crossing_TellsTheOldMapItWasRemoved_AndTheNewMapItArrived()
    {
        var server = new TestServer(withEveryMap: true);
        ConnectionId traveller = server.EnterWorld(1);
        ConnectionId onGround = server.EnterWorld(2);
        ConnectionId onField = server.EnterWorld(3);
        MapPortal toField = PortalOf(server, Ground);
        StandIn(server, onField, toField);
        server.Tick(3);
        server.PlayerOf(onGround).Position = new WorldPosition(toField.Center.X - 3f, 0f, toField.Center.Z);
        server.PlayerOf(traveller).Position = new WorldPosition(toField.Center.X - 2f, 0f, toField.Center.Z);
        server.Tick(2);
        EntityId travellerEntity = server.PlayerOf(traveller).Id;
        Assert.That(server.SessionOf(onGround).KnownEntities, Does.Contain(travellerEntity), "the ground saw it");
        server.Transport.ClearSent();

        StandIn(server, traveller, toField);
        server.Tick();
        server.Tick();

        EntityDespawn despawn = server.Transport.ControlSentTo(onGround)
            .Where(message => message.Opcode == MessageOpcode.EntityDespawn)
            .Select(message => EntityDespawn.TryRead(message.Payload, out EntityDespawn read) ? read : default)
            .Single(read => read.Entity == travellerEntity);
        Assert.That(despawn.Reason, Is.EqualTo(DespawnReason.Removed));
        Assert.That(server.SessionOf(onField).KnownEntities, Does.Contain(travellerEntity), "the field saw it arrive");
    }

    [Test]
    public void Crossing_WaitsForAPickupInFlight_AndNeverTakesTheDeadOrTheLeaving()
    {
        var server = new TestServer(withEveryMap: true);
        ConnectionId waiting = server.EnterWorld(1);
        ConnectionId dead = server.EnterWorld(2);
        ConnectionId leaving = server.EnterWorld(3);
        MapPortal portal = PortalOf(server, Ground);
        MapInstance ground = MapOf(server, Ground);
        ItemDropEntity drop = server.World.SpawnItemDrop(
            ground,
            new ItemDefinitionId("item.material.slime_gel"),
            1,
            portal.Center,
            server.CurrentTick,
            long.MaxValue,
            default);
        CharacterOf(server, waiting).Operation = InventoryOperation.ForPickup(drop, 1);
        server.PlayerOf(dead).CurrentHealth = 0;
        server.PlayerOf(dead).StateFlags |= EntityStateFlags.Dead;
        CharacterOf(server, leaving).IsLoggingOut = true;
        foreach (ConnectionId player in new[] { waiting, dead, leaving })
        {
            StandIn(server, player, portal);
        }

        server.Tick(3);
        bool[] stayed = new[] { waiting, dead, leaving }
            .Select(player => CharacterOf(server, player).Map.Definition.Id == Ground)
            .ToArray();
        CharacterOf(server, waiting).Operation = null;
        server.Tick();

        Assert.That(stayed, Is.EqualTo(new[] { true, true, true }));
        Assert.That(CharacterOf(server, waiting).Map.Definition.Id, Is.EqualTo(Field), "once the pickup settled");
        Assert.That(CharacterOf(server, dead).Map.Definition.Id, Is.EqualTo(Ground));
    }

    [Test]
    public void Death_OnTheField_RespawnsAtTheFieldsSpawnPoint()
    {
        var server = new TestServer(withEveryMap: true);
        ConnectionId player = server.EnterWorld(1);
        StandIn(server, player, PortalOf(server, Ground));
        server.Tick(2);
        PlayerEntity entity = server.PlayerOf(player);
        entity.Position = new WorldPosition(0f, 0f, 0f);

        server.Combat.Kill(MapOf(server, Field), entity, null, server.CurrentTick);
        server.Tick();
        server.SendRespawn(player, 1);
        server.Tick();

        Assert.That(entity.IsDead, Is.False);
        Assert.That(CharacterOf(server, player).Map.Definition.Id, Is.EqualTo(Field));
        Assert.That(entity.Position, Is.EqualTo(MapOf(server, Field).Definition.SpawnPosition));
    }

    [Test]
    public void Input_MadeForTheMapBeforeTheCrossing_IsDroppedUnscored_AndTheNewEpochMoves()
    {
        var server = new TestServer(withEveryMap: true);
        ConnectionId player = server.EnterWorld(1);
        server.Tick(2);
        StandIn(server, player, PortalOf(server, Ground));
        server.Tick(2);
        PlayerEntity entity = server.PlayerOf(player);
        WorldPosition arrived = entity.Position;

        for (uint sequence = 1; sequence <= 5; sequence++)
        {
            server.SendMove(player, sequence, 1f, 0f, 0);
            server.Tick();
        }

        WorldPosition afterOldEpoch = entity.Position;
        server.SendMove(player, 6, 1f, 0f, 1);
        server.Tick(3);

        Assert.That(afterOldEpoch, Is.EqualTo(arrived), "nothing made for the ground moves it on the field");
        Assert.That(server.SessionOf(player).OtherEpochInputs, Is.EqualTo(5));
        Assert.That(server.SessionOf(player).Violations!.Value, Is.Zero);
        Assert.That(entity.Position.X, Is.GreaterThan(arrived.X), "input of the new epoch moves it");
    }

    [Test]
    public void Phases_InTheTestServer_TransferBeforeTheTestsOwnHook_AndEndEffectsBeforeCombat()
    {
        var server = new TestServer();

        var order = server.Phases.Select(phase => phase.GetType()).ToList();
        var applyCommands = server.Phases
            .Where(phase => phase.Phase == TickPhase.ApplyCommands)
            .Select(phase => phase.GetType())
            .ToList();

        Assert.That(applyCommands, Is.EqualTo(new[] { typeof(MapTransferPhase), typeof(RecordingPhase) }));
        Assert.That(order.IndexOf(typeof(StatusEffectSystem)), Is.LessThan(order.IndexOf(typeof(CombatSystem))));
        Assert.That(order.IndexOf(typeof(InventorySyncPhase)), Is.LessThan(order.IndexOf(typeof(CharacterSyncPhase))));
    }

    [Test]
    public void Portal_ToAMapThisServerHasNotLoaded_KeepsTheCharacter_AndIsLoggedOncePerVisit()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(1);
        MapPortal portal = server.World.Maps.Single().Definition.Portals.Single();

        StandIn(server, player, portal);
        server.Tick(5);
        server.PlayerOf(player).Position = server.World.Maps.Single().Definition.SpawnPosition;
        server.Tick();
        StandIn(server, player, portal);
        server.Tick(2);

        Assert.That(CharacterOf(server, player).Map.Definition.Id, Is.EqualTo(Ground));
        Assert.That(server.SessionOf(player).MapEpoch, Is.Zero);
        Assert.That(server.Log.Entries.Count(entry => entry.EventId.Name == "MapTransferSkipped"), Is.EqualTo(2));
    }

    [Test]
    public void Standing_InTheGroundsPortal_MovesTheCharacterToTheFieldWithAWholeBaseline()
    {
        var server = new TestServer(withEveryMap: true);
        ConnectionId player = server.EnterWorld(1);
        server.Tick(2);
        server.Transport.ClearSent();
        MapPortal portal = PortalOf(server, Ground);

        StandIn(server, player, portal);
        server.Tick();
        server.Tick();

        PlayerEntity entity = server.PlayerOf(player);
        Assert.That(CharacterOf(server, player).Map.Definition.Id, Is.EqualTo(Field));
        Assert.That(MapOf(server, Field).Players, Does.Contain(entity));
        Assert.That(MapOf(server, Ground).Players, Does.Not.Contain(entity));
        Assert.That(entity.Position, Is.EqualTo(portal.DestinationPosition));
        Assert.That((entity.Facing.X, entity.Facing.Z), Is.EqualTo((1f, 0f)), "facing east, away from the gate");
        Assert.That(server.SessionOf(player).MapEpoch, Is.EqualTo(1));
        MessageOpcode[] sent = server.Transport.ControlOpcodesSentTo(player).ToArray();
        Assert.That(sent.First(), Is.EqualTo(MessageOpcode.WorldEntered));
        Assert.That(
            sent.SkipWhile(opcode => opcode != MessageOpcode.InventorySnapshot),
            Is.EqualTo(new[] { MessageOpcode.InventorySnapshot, MessageOpcode.SkillList, MessageOpcode.StatusEffects }),
            "the whole baseline of the new map");
        WorldEntered entered = WorldEnteredOf(server, player).Single();
        Assert.That((entered.Map, entered.MapEpoch), Is.EqualTo((Field, (byte)1)));
        Assert.That(server.Store.Stored(1).MapDefinitionId, Is.EqualTo(Field.Value), "checkpointed on the field");
        Assert.That(server.Log.Entries.Count(entry => entry.EventId.Name == "MapTransferred"), Is.EqualTo(1));
    }
}
}
