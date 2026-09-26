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
///     A supplied seed replays Milestone 6's systems too (verification line "skills, monster skills, experience, and
///     regeneration are deterministic with a supplied seed"): on the training field a scripted player provokes a spark
///     wisp, which keeps its range and casts Spark Bolt, then uses its skills on the forest crawlers, gains experience
///     from each kill, and regenerates.
/// </summary>
[TestFixture]
public sealed class SkillAndProgressionDeterminismTests
{
    private const int Ticks = 6000;
    private const int WispTicks = 1200;
    private const string Wisp = "monster.spark_wisp";
    private const string Crawler = "monster.forest_crawler";

    private static readonly MapDefinitionId Ground = new("map.training_ground");
    private static readonly MapDefinitionId Field = new("map.training_field");

    private static readonly MessageOpcode[] Outcomes =
    {
        MessageOpcode.AttackStarted, MessageOpcode.Damage, MessageOpcode.EntityDied, MessageOpcode.ItemDropped,
        MessageOpcode.EntityRevived, MessageOpcode.CharacterHealth, MessageOpcode.SkillCastStarted,
        MessageOpcode.SkillResolved, MessageOpcode.CharacterProgress, MessageOpcode.StatusEffects
    };

    private static readonly Lazy<Recording> SeedEleven = new(() => Record(11));

    private static MapInstance MapOf(TestServer server, MapDefinitionId id)
    {
        server.World.TryGetMap(id, out MapInstance? map);
        return map!;
    }

    /// <summary>
    ///     The player crosses to the field. For the first <see cref="WispTicks" /> ticks it attacks the wisp with the
    ///     lowest ID from beside it until the wisp takes it as its target, and then stands back at 5 m, within the
    ///     wisp's reach and beyond its keep distance, for the wisp to answer and cast. Then it fights the live crawler with
    ///     the lowest ID from beside it, casting Focus
    ///     whenever it is not focused, First Aid below half its HP, and Strike every forty ticks. It respawns when it
    ///     dies. Every step depends only on server state.
    /// </summary>
    private static Recording Record(ulong seed)
    {
        var server = new TestServer(withEveryMap: true, withMonsters: true, randomSeed: seed);
        ConnectionId player = server.EnterWorld(1);
        server.PlayerOf(player).Position = MapOf(server, Ground).Definition.Portals.Single().Center;
        server.Tick(2);
        Assert.That(server.SessionOf(player).Character!.Map.Definition.Id, Is.EqualTo(Field), "crossed");
        MapInstance field = MapOf(server, Field);
        var positions = new List<string>();
        uint sequence = 0;
        EntityId target = default;
        for (int tick = 0; tick < Ticks; tick++)
        {
            PlayerEntity self = server.PlayerOf(player);
            string prey = tick < WispTicks ? Wisp : Crawler;
            if (self.IsDead)
            {
                server.SendRespawn(player, ++sequence);
                target = default;
            }
            else if (!field.TryGetMonster(target, out MonsterEntity? monster)
                     || monster == null
                     || monster.IsDead
                     || monster.Definition.Id.Value != prey)
            {
                MonsterEntity? next = field.Monsters
                    .Where(candidate => !candidate.IsDead && candidate.Definition.Id.Value == prey)
                    .OrderBy(candidate => candidate.Id.Value)
                    .FirstOrDefault();
                if (next != null && TryStandAt(field, self, next, 1.2f))
                {
                    target = next.Id;
                    server.SendAttack(player, target, ++sequence);
                }
            }
            else if (prey == Wisp && monster.Target == self.Id)
            {
                float distance = Distance(self.Position, monster.Position);
                if (distance < 4.5f || distance > 5.5f)
                {
                    TryStandAt(field, self, monster, 5f);
                }
            }
            else
            {
                if (Distance(self.Position, monster.Position) > 1.6f)
                {
                    TryStandAt(field, self, monster, 1.2f);
                }

                if (self.StatusEffects.Count == 0)
                {
                    server.SendUseSkill(player, "skill.focus", default, ++sequence);
                }
                else if (self.CurrentHealth * 2 < self.MaxHealth)
                {
                    server.SendUseSkill(player, "skill.first_aid", default, ++sequence);
                }
                else if (tick % 40 == 0)
                {
                    server.SendUseSkill(player, "skill.strike", target, ++sequence);
                }
            }

            server.Tick();
            positions.Add(DescribeMonsters(field));
        }

        var outcomes = server.Transport.ControlSentTo(player)
            .Where(message => Outcomes.Contains(message.Opcode))
            .Select(message => BitConverter.ToString(message.Payload))
            .ToList();
        return new Recording(outcomes, positions, server.Transport.ControlSentTo(player).ToList());
    }

    private static bool TryStandAt(MapInstance map, PlayerEntity self, MonsterEntity monster, float distance)
    {
        NavigationGrid grid = map.Definition.Navigation;
        for (int step = 0; step < 8; step++)
        {
            double angle = step * Math.PI / 4.0;
            float x = monster.Position.X + distance * (float)Math.Cos(angle);
            float z = monster.Position.Z + distance * (float)Math.Sin(angle);
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

    private static int Count(Recording recording, MessageOpcode opcode, Func<byte[], bool> isCounted)
    {
        return recording.Messages.Count(message => message.Opcode == opcode && isCounted(message.Payload));
    }

    private sealed class Recording
    {
        public Recording(
            List<string> outcomes,
            List<string> positions,
            IReadOnlyList<InMemoryServerTransport.SentMessage> messages)
        {
            Outcomes = outcomes;
            Positions = positions;
            Messages = messages;
        }

        public List<string> Outcomes { get; }

        public List<string> Positions { get; }

        public IReadOnlyList<InMemoryServerTransport.SentMessage> Messages { get; }
    }

    [Test]
    public void AnotherSeed_ProducesADifferentRun()
    {
        Recording eleven = SeedEleven.Value;
        Recording twelve = Record(12);

        Assert.That(twelve.Outcomes, Is.Not.EqualTo(eleven.Outcomes));
        Assert.That(twelve.Positions, Is.Not.EqualTo(eleven.Positions));
    }

    [Test]
    public void SameSeed_ReplaysTheSameSkillsMonsterSkillsExperienceAndRegeneration()
    {
        Recording first = SeedEleven.Value;
        Recording second = Record(11);

        Assert.That(
            Count(first, MessageOpcode.SkillResolved, payload =>
                SkillResolved.TryRead(payload, out SkillResolved? resolved)
                && resolved!.Skill.Value == "skill.spark_bolt"),
            Is.GreaterThanOrEqualTo(1),
            "a wisp cast Spark Bolt");
        Assert.That(
            Count(first, MessageOpcode.SkillResolved, payload =>
                SkillResolved.TryRead(payload, out SkillResolved? resolved)
                && resolved!.Skill.Value == "skill.strike"),
            Is.GreaterThanOrEqualTo(1),
            "the player struck");
        Assert.That(
            Count(first, MessageOpcode.CharacterProgress, _ => true),
            Is.GreaterThanOrEqualTo(2),
            "kills gave experience");
        Assert.That(
            Count(first, MessageOpcode.StatusEffects, _ => true),
            Is.GreaterThanOrEqualTo(2),
            "Focus started and ended");
        Assert.That(second.Outcomes, Is.EqualTo(first.Outcomes), "skills, damage, experience, and health bytes");
        Assert.That(second.Positions, Is.EqualTo(first.Positions), "monster positions every tick");
    }
}
}
