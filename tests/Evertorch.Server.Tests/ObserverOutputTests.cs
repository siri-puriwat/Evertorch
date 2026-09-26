using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Only the values sent on purpose travel (Content Pipeline §5, Network Protocol §9; Milestone 6 verification):
///     an actor strikes and kills a slime, picks up its drop, casts First Aid and Focus, equips a sword, drinks a
///     potion, has a command refused, and crosses to the field, while an observer beside it at full HP and SP does
///     nothing. The observer hears what anyone near sees, and nothing of the actor's own target, health, progress,
///     skills, status effects, inventory, refusals, or new map.
/// </summary>
[TestFixture]
public sealed class ObserverOutputTests
{
    private const long Actor = 1;
    private const long Observer = 2;
    private const string Strike = "skill.strike";
    private const string FirstAid = "skill.first_aid";
    private const string Focus = "skill.focus";
    private const string Sword = "item.weapon.training_sword";
    private const string Potion = "item.consumable.minor_health";

    private static readonly MessageOpcode[] WhatAnyoneNearSees =
    {
        MessageOpcode.EntitySpawn, MessageOpcode.EntityDespawn, MessageOpcode.EntitySnapshot,
        MessageOpcode.AttackStarted, MessageOpcode.Damage, MessageOpcode.EntityDied, MessageOpcode.EntityRevived,
        MessageOpcode.SkillCastStarted, MessageOpcode.SkillResolved, MessageOpcode.ItemDropped,
        MessageOpcode.ItemPickedUp
    };

    private static MapInstance GroundOf(TestServer server)
    {
        server.World.TryGetMap(new MapDefinitionId("map.training_ground"), out MapInstance? ground);
        return ground!;
    }

    private static void StandBeside(TestServer server, ConnectionId player, MonsterEntity monster)
    {
        NavigationGrid grid = GroundOf(server).Definition.Navigation;
        for (int step = 0; step < 8; step++)
        {
            double angle = step * Math.PI / 4.0;
            float x = monster.Position.X + 1.2f * (float)Math.Cos(angle);
            float z = monster.Position.Z + 1.2f * (float)Math.Sin(angle);
            if (grid.CanOccupy(x, z)
                && grid.TrySampleHeight(x, z, out float height)
                && grid.HasLineOfSight(new WorldPosition(x, height, z), monster.Position))
            {
                server.PlayerOf(player).Position = new WorldPosition(x, height, z);
                return;
            }
        }

        throw new InvalidOperationException("No place to stand beside the monster.");
    }

    private static void TickUntil(TestServer server, Func<bool> condition, string what)
    {
        for (int tick = 0; tick < 200 && !condition(); tick++)
        {
            server.Tick();
        }

        Assert.That(condition(), Is.True, what);
    }

    private static void Cast(TestServer server, ConnectionId actor, string skill, EntityId target, uint sequence)
    {
        PlayerEntity player = server.PlayerOf(actor);
        player.CurrentSpirit = player.MaxSpirit;
        server.SendUseSkill(actor, skill, target, sequence);
        server.Tick();
        TickUntil(server, () => !player.Combat.IsCasting, $"{skill} resolved");
        server.Tick(20);
    }

    private static void Settle(TestServer server, ConnectionId actor)
    {
        server.Tick();
        TickUntil(server, () => server.SessionOf(actor).Character!.Operation == null, "the operation settled");
    }

    private static long RowOf(TestServer server, ConnectionId actor, string item)
    {
        return server.SessionOf(actor).Character!.Inventory.Rows.First(row => row.Item.Value == item).InventoryItem;
    }

    private static IEnumerable<T> Read<T>(TestServer server, ConnectionId connection, MessageOpcode opcode,
        Func<byte[], T?> read)
        where T : class
    {
        return server.Transport.SentTo(connection)
            .Where(message => message.Opcode == opcode)
            .Select(message => read(message.Payload)!);
    }

    [Test]
    public void AnotherPlayersPlay_ReachesAnObserverOnlyAsWhatAnyoneNearSees()
    {
        var server = new TestServer(
            withMonsters: true,
            combatRandom: new SureHitRandom(),
            withMonsterAi: false,
            dropRandom: new ScriptedRandom(0),
            withEveryMap: true);
        ConnectionId actor = server.Connect();
        server.SignInWithCharacter(actor, Actor);
        server.Store.GiveItems(Actor, Sword, 1, 1, 1);
        server.Store.GiveItems(Actor, Potion, 1, 2, 1);
        server.SendEnterWorld(actor, Actor);
        server.TickUntil(() => server.SessionOf(actor).State == SessionState.InWorld);
        ConnectionId observer = server.EnterWorld(Observer);
        MonsterEntity slime = server.MonstersNear(server.PlayerOf(observer).Position).First();
        StandBeside(server, actor, slime);
        server.Tick(2);
        server.Transport.ClearSent();

        Cast(server, actor, Strike, slime.Id, 1);
        slime.CurrentHealth = 1;
        server.SendAttack(actor, slime.Id, 2);
        TickUntil(server, () => slime.IsDead, "the slime died");
        ItemDropped gel = Read(server, actor, MessageOpcode.ItemDropped, payload =>
            ItemDropped.TryRead(payload, out ItemDropped? read) ? read : null).Single();
        server.SendPickup(actor, gel.Entity, 3);
        Settle(server, actor);
        Cast(server, actor, FirstAid, default, 4);
        Cast(server, actor, Focus, default, 5);
        server.SendEquip(actor, RowOf(server, actor, Sword), 6);
        Settle(server, actor);
        server.PlayerOf(actor).CurrentHealth = 10;
        server.SendUseItem(actor, RowOf(server, actor, Potion), 7);
        Settle(server, actor);
        server.SendUnequip(actor, EquipmentSlot.Armor, 8);
        server.Tick();
        server.PlayerOf(actor).Position = GroundOf(server).Definition.Portals.Single().Center;
        server.Tick(2);

        MessageOpcode[] actorHeard = server.Transport.SentTo(actor).Select(message => message.Opcode).ToArray();
        MessageOpcode[] observerHeard = server.Transport.SentTo(observer).Select(message => message.Opcode).ToArray();
        Assert.That(
            actorHeard,
            Is.SupersetOf(
                new[]
                {
                    MessageOpcode.CharacterProgress, MessageOpcode.InventoryChanged, MessageOpcode.CharacterHealth,
                    MessageOpcode.StatusEffects, MessageOpcode.CommandRejected, MessageOpcode.WorldEntered,
                    MessageOpcode.InventorySnapshot, MessageOpcode.SkillList
                }),
            "the actor was told of its own play");
        Assert.That(observerHeard.Distinct(), Is.SubsetOf(WhatAnyoneNearSees));
        Assert.That(
            observerHeard,
            Is.SupersetOf(
                new[]
                {
                    MessageOpcode.AttackStarted, MessageOpcode.Damage, MessageOpcode.EntityDied,
                    MessageOpcode.ItemDropped, MessageOpcode.ItemPickedUp, MessageOpcode.EntityDespawn
                }),
            "the observer watched the whole play");
        Assert.That(
            Read(server, observer, MessageOpcode.SkillCastStarted, payload =>
                    SkillCastStarted.TryRead(payload, out SkillCastStarted? read) ? read : null)
                .Select(cast => cast.Skill.Value),
            Is.EqualTo(new[] { Strike, FirstAid, Focus }));
    }
}
}
