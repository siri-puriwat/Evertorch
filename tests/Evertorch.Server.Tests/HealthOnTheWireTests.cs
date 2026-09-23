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
