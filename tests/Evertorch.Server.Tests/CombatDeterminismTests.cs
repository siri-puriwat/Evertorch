using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A supplied seed replays a whole fight: placement, AI, hit rolls, damage, deaths, and drops (Milestone 3
///     verification line "combat results are deterministic with a supplied random seed").
/// </summary>
[TestFixture]
public sealed class CombatDeterminismTests
{
    private const int Ticks = 1600;

    private static readonly MessageOpcode[] Outcomes =
    {
        MessageOpcode.AttackStarted, MessageOpcode.Damage, MessageOpcode.EntityDied, MessageOpcode.ItemDropped,
        MessageOpcode.EntityRevived, MessageOpcode.CharacterHealth
    };

    private static readonly Lazy<Recording> SeedSeven = new(() => Record(7));

    /// <summary>
    ///     One scripted player fights slimes for <see cref="Ticks" /> ticks: it stands beside the live monster with
    ///     the lowest ID, attacks it, follows it, and respawns when it dies. Every step depends only on server state.
    /// </summary>
    private static Recording Record(ulong seed)
    {
        var server = new TestServer(withMonsters: true, randomSeed: seed);
        ConnectionId player = server.EnterWorld(1);
        MapInstance map = server.World.Maps.Single();
        var positions = new List<string>();
        uint sequence = 0;
        EntityId target = default;
        for (int tick = 0; tick < Ticks; tick++)
        {
            PlayerEntity self = server.PlayerOf(player);
            if (self.IsDead)
            {
                server.SendRespawn(player, ++sequence);
                target = default;
            }
            else if (!map.TryGetMonster(target, out MonsterEntity? monster) || monster == null || monster.IsDead)
            {
                MonsterEntity? next = map.Monsters.Where(candidate => !candidate.IsDead)
                    .OrderBy(candidate => candidate.Id.Value)
                    .FirstOrDefault();
                if (next != null && TryStandBeside(map, self, next))
                {
                    target = next.Id;
                    server.SendAttack(player, target, ++sequence);
                }
            }
            else if (Distance(self.Position, monster.Position) > 1.6f)
            {
                TryStandBeside(map, self, monster);
            }

            server.Tick();
            positions.Add(DescribeMonsters(map));
        }

        var outcomes = server.Transport.ControlSentTo(player)
            .Where(message => Outcomes.Contains(message.Opcode))
            .Select(message => BitConverter.ToString(message.Payload))
            .ToList();
        return new Recording(outcomes, positions, server.Transport.ControlOpcodesSentTo(player));
    }

    private static bool TryStandBeside(MapInstance map, PlayerEntity self, MonsterEntity monster)
    {
        NavigationGrid grid = map.Definition.Navigation;
        for (int step = 0; step < 8; step++)
        {
            double angle = step * Math.PI / 4.0;
            float x = monster.Position.X + 1.2f * (float)Math.Cos(angle);
            float z = monster.Position.Z + 1.2f * (float)Math.Sin(angle);
            if (grid.CanOccupy(x, z)
                && grid.TrySampleHeight(x, z, out float height)
                && grid.HasLineOfSight(new WorldPosition(x, height, z), monster.Position))
            {
                self.Position = new WorldPosition(x, height, z);
                return true;
            }
        }

        return false;
    }

    private static float Distance(WorldPosition a, WorldPosition b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private static string DescribeMonsters(MapInstance map)
    {
        return string.Join(
            ";",
            map.Monsters.OrderBy(monster => monster.Id.Value)
                .Select(monster => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}:{1:R},{2:R},{3}",
                    monster.Id.Value,
                    monster.Position.X,
                    monster.Position.Z,
                    monster.CurrentHealth)));
    }

    private sealed class Recording
    {
        public Recording(List<string> outcomes, List<string> positions, IReadOnlyList<MessageOpcode> opcodes)
        {
            Outcomes = outcomes;
            Positions = positions;
            Opcodes = opcodes;
        }

        public List<string> Outcomes { get; }

        public List<string> Positions { get; }

        public IReadOnlyList<MessageOpcode> Opcodes { get; }
    }

    [Test]
    public void AnotherSeed_ProducesADifferentFight()
    {
        Recording seven = SeedSeven.Value;
        Recording eight = Record(8);

        Assert.That(eight.Outcomes, Is.Not.EqualTo(seven.Outcomes));
        Assert.That(eight.Positions, Is.Not.EqualTo(seven.Positions));
    }

    [Test]
    public void SameSeed_ReplaysTheSameOutcomesAndMonsterPositions()
    {
        Recording first = SeedSeven.Value;
        Recording second = Record(7);

        Assert.That(first.Opcodes.Count(opcode => opcode == MessageOpcode.EntityDied), Is.GreaterThanOrEqualTo(3));
        Assert.That(first.Opcodes.Count(opcode => opcode == MessageOpcode.ItemDropped), Is.GreaterThanOrEqualTo(1));
        Assert.That(first.Opcodes.Count(opcode => opcode == MessageOpcode.Damage), Is.GreaterThanOrEqualTo(20));
        Assert.That(second.Outcomes, Is.EqualTo(first.Outcomes), "Damage, EntityDied, ItemDropped bytes");
        Assert.That(second.Positions, Is.EqualTo(first.Positions), "monster positions every tick");
    }
}
}
