using System.Linq;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class HealthOnTheWireTests
{
    private static EntitySpawn[] Spawns(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Select(message => EntitySpawn.TryRead(message.Payload, out EntitySpawn? spawn) ? spawn : null)
            .Where(spawn => spawn != null)
            .Select(spawn => spawn!)
            .ToArray();
    }

    // Another player's HP travels only to its party, in thousandths (Network Protocol §9, Milestone 12): a member
    // hears it, a player beside it outside the party hears nothing of it, and the owner's own numbers stay its own.
    [Test]
    public void AWoundedPlayersHealth_ReachesItsPartyAsThousandths_AndNoOneElse()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Join(seven, "Tester7", eight, "Tester8");
        rig.Server.Tick(TestServer.TickRate);
        rig.Server.Transport.ClearSent();
        PlayerEntity wounded = rig.Server.PlayerOf(seven);

        int half = wounded.MaxHealth / 2;
        wounded.CurrentHealth = half;
        rig.Server.Tick(TestServer.TickRate);

        PartyMemberStatus heard = rig.Server.Transport.ControlSentTo(eight)
            .Where(message => message.Opcode == MessageOpcode.PartyMemberStatus)
            .Select(message =>
                PartyMemberStatus.TryRead(message.Payload, out PartyMemberStatus? status) ? status! : null!)
            .Single();
        Assert.That(
            (heard.Name, (int)heard.HealthPermille),
            Is.EqualTo(("Tester7", half * 1000 / wounded.MaxHealth)));
        Assert.That(rig.Server.Transport.ControlOpcodesSentTo(eight), Has.No.Member(MessageOpcode.CharacterHealth));
        Assert.That(
            rig.Server.Transport.ControlOpcodesSentTo(nine),
            Has.No.Member(MessageOpcode.PartyMemberStatus).And.No.Member(MessageOpcode.CharacterHealth),
            "nothing for a player outside the party");
    }

    [Test]
    public void EntitySpawn_ForAMonster_CarriesItsHealthRatioAndForAPlayerNone()
    {
        var server = new TestServer(withMonsters: true);
        ConnectionId first = server.EnterWorld(1);
        server.EnterWorld(2);
        server.Tick();

        EntitySpawn[] spawns = Spawns(server, first);

        Assert.That(
            spawns.Where(spawn => spawn.Kind == EntityKind.Monster).Select(spawn => spawn.HealthPermille),
            Is.All.EqualTo(HealthRatio.Full));
        Assert.That(spawns.Single(spawn => spawn.Kind == EntityKind.Player).HealthPermille, Is.Zero);
    }

    [Test]
    public void EntitySpawn_ForAWoundedMonster_RoundsTheRatioUp()
    {
        var server = new TestServer(withMonsters: true);
        MonsterEntity slime = server.MonstersNear(server.World.Maps.First().Definition.SpawnPosition).First();
        slime.CurrentHealth = 1;

        ConnectionId player = server.EnterWorld(1);

        EntitySpawn spawn = Spawns(server, player).Single(message => message.Entity == slime.Id);
        Assert.That(spawn.HealthPermille, Is.EqualTo(20), "1 of 50 HP");
    }

    [Test]
    public void WorldEntered_CarriesTheCharactersHealthAndAttackRange()
    {
        var server = new TestServer();
        ConnectionId player = server.EnterWorld(1);

        WorldEntered.TryRead(
            server.Transport.ControlSentTo(player).Single(m => m.Opcode == MessageOpcode.WorldEntered).Payload,
            out WorldEntered? entered);

        PlayerEntity entity = server.PlayerOf(player);
        Assert.That(entered!.CurrentHealth, Is.EqualTo(71u), "(60 + 8 × level 1) × 1.05 for vit 5, truncated");
        Assert.That(entered.MaximumHealth, Is.EqualTo(71u));
        Assert.That(entered.AttackRange, Is.EqualTo(1.5f), "skill.basic_attack server.range");
        Assert.That(entity.MaxHealth, Is.EqualTo(entity.Stats.MaxHp));
        Assert.That(entity.CurrentHealth, Is.EqualTo(71));
    }
}
}
