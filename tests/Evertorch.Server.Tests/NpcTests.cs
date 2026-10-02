using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The NPCs the maps place (Gameplay Systems §6.1; Network Protocol §6, §9): the entity IDs they take, what their
///     services hold, when a client is told of them, and that they are never in a snapshot nor a target.
/// </summary>
[TestFixture]
public sealed class NpcTests
{
    private const string Quartermaster = "npc.quartermaster";
    private const string GateWarden = "npc.gate_warden";
    private const string Guildmaster = "npc.guildmaster";
    private const int GraceMs = 1000;

    private static readonly MapDefinitionId Ground = new("map.training_ground");
    private static readonly MapDefinitionId Field = new("map.training_field");

    private static NpcEntity NpcOf(TestServer server, string npc)
    {
        return server.World.Maps.SelectMany(map => map.Npcs).Single(entity => entity.DefinitionId == npc);
    }

    private static byte[] Encode(NpcServices services)
    {
        byte[] buffer = new byte[services.GetEncodedLength()];
        services.Write(buffer);
        return buffer;
    }

    // The NPCs spawned to the client, in the order it was told; each spawn is followed at once by the NPC's services
    // as built at load, or it is listed as without them.
    private static string[] NpcsToldTo(TestServer server, ConnectionId connection)
    {
        IReadOnlyList<InMemoryServerTransport.SentMessage> sent = server.Transport.ControlSentTo(connection);
        var told = new List<string>();
        for (int index = 0; index < sent.Count; index++)
        {
            if (!EntitySpawn.TryRead(sent[index].Payload, out EntitySpawn? spawn) || spawn!.Kind != EntityKind.Npc)
            {
                continue;
            }

            NpcEntity npc = NpcOf(server, spawn.DefinitionId);
            bool isFollowed = spawn.Entity == npc.Id
                && index + 1 < sent.Count
                && sent[index + 1].Payload.SequenceEqual(Encode(npc.Services));
            told.Add(isFollowed ? spawn.DefinitionId : $"{spawn.DefinitionId} without its services");
        }

        return told.ToArray();
    }

    private static void StandIn(TestServer server, ConnectionId player, MapDefinitionId from, MapDefinitionId to)
    {
        server.World.TryGetMap(from, out MapInstance? instance);
        server.PlayerOf(player).Position =
            instance!.Definition.Portals.Single(portal => portal.DestinationMap == to).Center;
    }

    private static CommandRejected[] Rejections(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message =>
            {
                CommandRejected.TryRead(message.Payload, out CommandRejected rejected);
                return rejected;
            })
            .ToArray();
    }

    // The field's portal lands a player east of x = 16, where the Gate Warden's interest cell and, diagonally, the
    // Guildmaster's are in view; the Quartermaster comes into view once the player walks west of it.
    [Test]
    public void CrossingBack_TellsTheGateWardenOnArrival_AndTheQuartermasterOnceThePlayerWalksWestOfSixteen()
    {
        var server = new TestServer(withEveryMap: true, withNpcs: true);
        ConnectionId player = server.EnterWorld(1);
        StandIn(server, player, Ground, Field);
        server.Tick();
        Assert.That(server.SessionOf(player).Character!.Map.Definition.Id, Is.EqualTo(Field), "crossed");
        server.Tick(TestServer.TickRate);
        Assert.That(NpcsToldTo(server, player).Length, Is.EqualTo(3), "all three were told before the crossing");
        server.Transport.ClearSent();

        StandIn(server, player, Field, Ground);
        server.Tick(TestServer.TickRate);
        string[] onArrival = NpcsToldTo(server, player);
        server.Transport.ClearSent();
        server.Place(player, 16.5f, 0f);
        server.Tick(2);
        string[] eastOfSixteen = NpcsToldTo(server, player);
        server.Place(player, 15.5f, 0f);
        server.Tick(2);

        Assert.That(server.SessionOf(player).Character!.Map.Definition.Id, Is.EqualTo(Ground), "back in town");
        Assert.That(onArrival, Is.EquivalentTo(new[] { GateWarden, Guildmaster }));
        Assert.That(eastOfSixteen, Is.Empty);
        Assert.That(NpcsToldTo(server, player), Is.EqualTo(new[] { Quartermaster }));
    }

    [Test]
    public void Entering_AtTheSpawn_TellsEveryNpcEachFollowedByItsServices_Once()
    {
        var server = new TestServer(withNpcs: true);

        ConnectionId player = server.EnterWorld(1);
        server.Tick(5);

        Assert.That(NpcsToldTo(server, player), Is.EquivalentTo(new[] { GateWarden, Quartermaster, Guildmaster }));
    }

    [Test]
    public void Load_PlacesEachNpcOnceAfterEveryMapsMonsters_LeavingTheMonstersIds()
    {
        var withoutNpcs = new TestServer(withEveryMap: true, withMonsters: true, withMonsterAi: false);
        var server = new TestServer(withEveryMap: true, withMonsters: true, withMonsterAi: false, withNpcs: true);
        long[] monsters = server.World.Maps.SelectMany(map => map.Monsters).Select(monster => monster.Id.Value)
            .OrderBy(id => id)
            .ToArray();

        Assert.That(
            monsters,
            Is.EqualTo(
                withoutNpcs.World.Maps.SelectMany(map => map.Monsters).Select(monster => monster.Id.Value)
                    .OrderBy(id => id)));
        Assert.That(
            server.World.Maps.SelectMany(map => map.Npcs).Select(npc => npc.Id.Value),
            Is.All.GreaterThan(monsters.Max()));
        foreach (NpcPlacement placement in server.Content.Maps[Ground].Npcs)
        {
            NpcEntity npc = NpcOf(server, placement.Npc.Value);
            Assert.That(npc.Position, Is.EqualTo(placement.Position), placement.Npc.Value);
            Assert.That(server.World.Maps.Single(map => map.Npcs.Contains(npc)).Definition.Id, Is.EqualTo(Ground));
            double length =
                Math.Sqrt(placement.Facing.X * placement.Facing.X + placement.Facing.Z * placement.Facing.Z);
            Assert.That(npc.Facing.X, Is.EqualTo(placement.Facing.X / length).Within(1e-6), "a unit facing");
            Assert.That(npc.Facing.Z, Is.EqualTo(placement.Facing.Z / length).Within(1e-6), "a unit facing");
        }

        Assert.That(
            server.World.Maps.SelectMany(map => map.Npcs).Select(npc => npc.DefinitionId),
            Is.EquivalentTo(new[] { GateWarden, Quartermaster, Guildmaster }));
        Assert.That(withoutNpcs.World.Maps.SelectMany(map => map.Npcs), Is.Empty, "a test asks for them");
    }

    [Test]
    public void Reconnect_WithinGrace_TellsTheNpcsAndTheirServicesAgain()
    {
        var server = new TestServer(reconnectGraceMs: GraceMs, withNpcs: true);
        ConnectionId first = server.EnterWorld(7);
        server.Tick();
        EntityId entity = server.PlayerOf(first).Id;
        server.Disconnect(first);
        server.Tick();

        ConnectionId second = server.Connect();
        server.SignInWithCharacter(second, 7);
        server.SendEnterWorld(second, 7);
        server.TickUntil(() => server.SessionOf(second).State == SessionState.InWorld);
        server.Tick(2);

        Assert.That(server.PlayerOf(second).Id, Is.EqualTo(entity), "the same body, attached again");
        Assert.That(NpcsToldTo(server, second), Is.EquivalentTo(new[] { GateWarden, Quartermaster, Guildmaster }));
    }

    // The shop trades its stock at the stock's prices and buys every item with a sell price at that price, sorted by
    // item ID; the Gate Warden gives the quest that names it (Prototype Content §2).
    [Test]
    public void Services_OfTheRepositoryNpcs_AreTheShopTheQuestTheResetAndTheJobChanges()
    {
        var server = new TestServer(withNpcs: true);

        NpcServices shop = NpcOf(server, Quartermaster).Services;
        NpcServices warden = NpcOf(server, GateWarden).Services;

        Assert.That(shop.Npc, Is.EqualTo(NpcOf(server, Quartermaster).Id));
        Assert.That(
            shop.Entries.Select(entry => (entry.Item.Value, entry.BuyPrice, entry.SellPrice)),
            Is.EqualTo(
                new[]
                {
                    ("item.armor.cloth", 40u, 20u), ("item.armor.leather", 0u, 45u),
                    ("item.consumable.minor_health", 20u, 10u), ("item.consumable.minor_mana", 30u, 15u),
                    ("item.material.crawler_shell", 0u, 5u), ("item.material.grotto_carapace", 0u, 12u),
                    ("item.material.slime_gel", 0u, 2u), ("item.weapon.ash_staff", 0u, 60u),
                    ("item.weapon.iron_sword", 0u, 60u), ("item.weapon.training_staff", 50u, 25u),
                    ("item.weapon.training_sword", 50u, 25u)
                }),
            "the second tier is bought back, never sold");
        Assert.That(shop.Offers, Is.Empty);
        Assert.That(warden.Npc, Is.EqualTo(NpcOf(server, GateWarden).Id));
        Assert.That(warden.Entries, Is.Empty);
        Assert.That(
            warden.Offers.Select(offer =>
                (offer.Quest.Value, offer.Monster.Value, offer.Count, offer.BaseExperience, offer.Coins)),
            Is.EqualTo(new[] { ("quest.crawler_hunt", "monster.forest_crawler", (ushort)5, 150ul, 100u) }));
        NpcServices guildmaster = server.World.Maps.SelectMany(map => map.Npcs)
            .Single(npc => npc.DefinitionId == "npc.guildmaster")
            .Services;
        Assert.That((shop.OffersReset, warden.OffersReset, guildmaster.OffersReset), Is.EqualTo((false, false, true)));
        Assert.That((guildmaster.Entries.Count, guildmaster.Offers.Count), Is.EqualTo((0, 0)));
        Assert.That(
            guildmaster.JobChanges.Select(change => (change.Job.Value, change.FromJob.Value, change.Level)),
            Is.EqualTo(
                new[]
                {
                    ("job.arcanist", "job.adventurer", (ushort)10), ("job.vanguard", "job.adventurer", (ushort)10)
                }),
            "every first job, at its base job's cap");
        Assert.That(shop.JobChanges.Concat(warden.JobChanges), Is.Empty, "only the Guildmaster changes jobs");
        Assert.That(
            (shop.GetEncodedLength(), warden.GetEncodedLength(), guildmaster.GetEncodedLength()),
            Is.EqualTo((386, 80, 78)));
    }

    [Test]
    public void Snapshot_NeverCarriesAnNpc()
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId player = server.EnterWorld(1);

        server.Tick(5);

        EntityId[] npcs = server.World.Maps.SelectMany(map => map.Npcs).Select(npc => npc.Id).ToArray();
        Assert.That(server.SessionOf(player).KnownEntities, Is.SupersetOf(npcs), "the client knows both");
        EntityId[] states = server.Transport.SnapshotsSentTo(player)
            .SelectMany(snapshot => snapshot.Entities)
            .Select(state => state.Entity)
            .ToArray();
        Assert.That(states, Is.Not.Empty);
        Assert.That(states.Intersect(npcs), Is.Empty);
    }

    // An NPC is refused as a target, an attack, a skill's target, and a pickup, exactly like a missing entity, and
    // nothing else changes (Gameplay Systems §6).
    [Test]
    public void Target_Attack_Skill_OrPickup_OnAnNpc_IsRefusedLikeAMissingEntity()
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId player = server.EnterWorld(1);
        server.Tick();
        EntityId quartermaster = NpcOf(server, Quartermaster).Id;
        Assert.That(server.SessionOf(player).KnownEntities, Does.Contain(quartermaster), "the client knows it");
        server.Transport.ClearSent();

        server.SendTarget(player, quartermaster);
        server.SendAttack(player, quartermaster, 4);
        server.SendUseSkill(player, "skill.strike", quartermaster, 5);
        server.SendPickup(player, quartermaster, 6);
        server.Tick();

        Assert.That(server.PlayerOf(player).Target, Is.EqualTo(default(EntityId)));
        Assert.That(server.PlayerOf(player).Combat.IsAutoAttacking || server.PlayerOf(player).Combat.IsCasting,
            Is.False);
        Assert.That(
            Rejections(server, player).Select(rejected => (rejected.CommandSequence, rejected.Reason)),
            Is.EquivalentTo(
                new[]
                {
                    (4u, CommandRejectionReason.InvalidTarget), (5u, CommandRejectionReason.InvalidTarget),
                    (6u, CommandRejectionReason.InvalidTarget)
                }));
        Assert.That(server.Transport.ControlOpcodesSentTo(player), Has.None.EqualTo(MessageOpcode.TargetChanged));
        Assert.That(server.World.Maps.Single().Contains(quartermaster), Is.True, "the NPC stays");
    }
}
}
