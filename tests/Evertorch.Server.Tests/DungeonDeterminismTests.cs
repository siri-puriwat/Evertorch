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
///     A supplied seed replays Milestone 13's systems too (verification line "assist, the debuff, the slam, the spread,
///     and the MVP's rolls replay with a seed"): in the Umbral Grotto a scripted player fights a Grotto Crawler, whose
///     kin join in; a Gloom Wisp, which numbs it; and the Slime Monarch, which slams it; then the boss falls to the
///     player, which rolls its prize and draws its return.
/// </summary>
[TestFixture]
public sealed class DungeonDeterminismTests
{
    private const int PhaseTicks = 600;
    private const string Crawler = "monster.grotto_crawler";
    private const string Wisp = "monster.gloom_wisp";
    private const string Monarch = "monster.slime_monarch";

    private static readonly MapDefinitionId Grotto = new("map.umbral_grotto");

    private static readonly MessageOpcode[] Outcomes =
    {
        MessageOpcode.AttackStarted, MessageOpcode.Damage, MessageOpcode.EntityDied, MessageOpcode.ItemDropped,
        MessageOpcode.EntityRevived, MessageOpcode.SkillCastStarted, MessageOpcode.SkillResolved,
        MessageOpcode.StatusEffects, MessageOpcode.BossAnnouncement, MessageOpcode.MvpAwarded
    };

    private static readonly Lazy<Recording> SeedThirteen = new(() => Record(13));

    private static MapInstance GrottoOf(TestServer server)
    {
        server.World.TryGetMap(Grotto, out MapInstance? map);
        return map!;
    }

    /// <summary>
    ///     The player's DEX of 99 lands its swings, so a passive monster it attacks answers. For
    ///     <see cref="PhaseTicks" /> ticks each, it fights the live crawler, then wisp, then boss with the
    ///     lowest ID: it attacks from beside it, and from 5 m once a wisp has taken it as its target, so the wisp casts.
    ///     Its HP are topped up before every tick. Then the boss falls to it. Every step depends only on server state.
    /// </summary>
    private static Recording Record(ulong seed)
    {
        var server = new TestServer(withEveryMap: true, withGrotto: true, withMonsters: true, randomSeed: seed);
        ConnectionId player = server.EnterWorld(1);
        server.CrossIntoTheGrotto(player);
        PlayerEntity scripted = server.PlayerOf(player);
        scripted.SetPrimary(new PrimaryStats(5, 5, 5, 5, 99, 5));
        server.Stats.Recalculate(scripted, server.Content.Jobs[scripted.Job]);
        MapInstance grotto = GrottoOf(server);
        var positions = new List<string>();
        uint sequence = 0;
        foreach (string prey in new[] { Crawler, Wisp, Monarch })
        {
            EntityId target = default;
            bool isAttacking = false;
            for (int tick = 0; tick < PhaseTicks; tick++)
            {
                PlayerEntity self = server.PlayerOf(player);
                if (self.IsDead)
                {
                    server.SendRespawn(player, ++sequence);
                    target = default;
                }
                else if (!grotto.TryGetMonster(target, out MonsterEntity? monster)
                         || monster == null
                         || monster.IsDead)
                {
                    MonsterEntity? next = grotto.Monsters
                        .Where(candidate => !candidate.IsDead && candidate.Definition.Id.Value == prey)
                        .OrderBy(candidate => candidate.Id.Value)
                        .FirstOrDefault();
                    if (next != null && TryStandAt(grotto, self, next, 1.2f))
                    {
                        target = next.Id;
                        isAttacking = false;
                    }
                }
                else if (!isAttacking)
                {
                    // A tick after standing beside it, once the player sees it.
                    server.SendAttack(player, target, ++sequence);
                    isAttacking = true;
                }
                else
                {
                    float distance = prey == Wisp && monster.Target == self.Id ? 5f : 1.2f;
                    if (Math.Abs(Distance(self.Position, monster.Position) - distance) > 0.5f)
                    {
                        TryStandAt(grotto, self, monster, distance);
                    }
                }

                // HP to spare, so the pack gathers, the numbing lands, and the boss gets to slam.
                if (!self.IsDead)
                {
                    self.CurrentHealth = 1_000_000;
                }

                server.Tick();
                positions.Add(DescribeMonsters(grotto));
            }
        }

        PlayerEntity winner = server.PlayerOf(player);
        MonsterEntity monarch = grotto.Monsters.Single(monster => monster.Definition.Id.Value == Monarch);
        if (winner.IsDead)
        {
            server.SendRespawn(player, ++sequence);
            server.Tick(2);
        }

        monarch.LogMvpDealt(winner.Character, 1);
        server.Combat.Kill(grotto, monarch, winner, server.CurrentTick);
        server.Tick(TestServer.TickRate);
        string bosses = string.Join(
            ";",
            server.MonsterAi.DescribeBosses(server.CurrentTick)
                .Select(boss => $"{boss.Monster.Value} {boss.IsAlive} {boss.ReturnsInMs}"));
        var messages = server.Transport.ControlSentTo(player).ToList();
        var outcomes = messages
            .Where(message => Outcomes.Contains(message.Opcode))
            .Select(message => BitConverter.ToString(message.Payload))
            .ToList();
        return new Recording(outcomes, positions, bosses, messages);
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

    private static IEnumerable<T> Read<T>(Recording recording, MessageOpcode opcode, Func<byte[], T?> read)
        where T : class
    {
        return recording.Messages
            .Where(message => message.Opcode == opcode)
            .Select(message => read(message.Payload))
            .Where(message => message != null)
            .Select(message => message!);
    }

    private sealed class Recording
    {
        public Recording(
            List<string> outcomes,
            List<string> positions,
            string bosses,
            IReadOnlyList<InMemoryServerTransport.SentMessage> messages)
        {
            Outcomes = outcomes;
            Positions = positions;
            Bosses = bosses;
            Messages = messages;
        }

        public List<string> Outcomes { get; }

        public List<string> Positions { get; }

        public string Bosses { get; }

        public IReadOnlyList<InMemoryServerTransport.SentMessage> Messages { get; }
    }

    [Test]
    public void AnotherSeed_ProducesADifferentRun()
    {
        Recording thirteen = SeedThirteen.Value;
        Recording fourteen = Record(14);

        Assert.That(fourteen.Outcomes, Is.Not.EqualTo(thirteen.Outcomes));
        Assert.That(fourteen.Positions, Is.Not.EqualTo(thirteen.Positions));
    }

    [Test]
    public void SameSeed_ReplaysTheAssistTheDebuffTheSlamTheSpreadAndTheMvpsRoll()
    {
        Recording first = SeedThirteen.Value;
        Recording second = Record(13);
        long self = first.Messages
            .Where(message => message.Opcode == MessageOpcode.WorldEntered)
            .Select(message => WorldEntered.TryRead(message.Payload, out WorldEntered? entered)
                ? entered!.LocalEntity.Value
                : 0)
            .Last();

        var crawlers = Read(first, MessageOpcode.EntitySpawn, payload =>
                EntitySpawn.TryRead(payload, out EntitySpawn? spawn) ? spawn : null)
            .Where(spawn => spawn.DefinitionId == Crawler)
            .Select(spawn => spawn.Entity)
            .ToHashSet();

        var struck = new HashSet<EntityId>();
        var joined = new HashSet<EntityId>();
        foreach (AttackStarted started in first.Messages
                     .Where(message => message.Opcode == MessageOpcode.AttackStarted)
                     .Select(message =>
                         AttackStarted.TryRead(message.Payload, out AttackStarted read) ? read : default))
        {
            if (started.Attacker.Value == self)
            {
                struck.Add(started.Target);
            }
            else if (started.Target.Value == self
                     && crawlers.Contains(started.Attacker)
                     && !struck.Contains(started.Attacker))
            {
                joined.Add(started.Attacker);
            }
        }

        Assert.That(joined, Is.Not.Empty, "a crawler the player had not struck swung at it: its kin's call");
        Assert.That(
            Read(first, MessageOpcode.SkillResolved, payload =>
                    SkillResolved.TryRead(payload, out SkillResolved? resolved) ? resolved : null)
                .Select(resolved => resolved.Skill.Value)
                .Distinct(),
            Is.SupersetOf(new[] { "skill.numbing_spark", "skill.quake_slam" }),
            "the wisp numbed the player and the boss slammed it");
        Assert.That(
            first.Messages.Count(message => message.Opcode == MessageOpcode.MvpAwarded),
            Is.EqualTo(1),
            "the prize was rolled and told");
        Assert.That(first.Bosses, Does.StartWith($"{Monarch} False "), "the boss's return drawn");
        Assert.That(second.Outcomes, Is.EqualTo(first.Outcomes), "combat, skills, the numbing, and the award bytes");
        Assert.That(second.Positions, Is.EqualTo(first.Positions), "monster positions every tick");
        Assert.That(second.Bosses, Is.EqualTo(first.Bosses), "the boss's return, to the millisecond");
    }
}
}
