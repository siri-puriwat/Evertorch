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
///     A supplied seed replays the Ashen Quarry (Milestone 21 line 6): a scripted player fights a Cinder Crawler, which
///     bites with Cinder Bite; a Basalt Crawler, whose partner answers its call; an Ash Wisp, which silences it; and the
///     Furnace Heart, which begins a cast; then the boss falls to the player, which rolls its prize and draws its
///     return. The same seed replays every outcome's bytes, every monster's place, and the boss's return.
/// </summary>
[TestFixture]
public sealed class QuarryDeterminismTests
{
    private const int PhaseTicks = 600;
    private const string Cinder = "monster.cinder_crawler";
    private const string Basalt = "monster.basalt_crawler";
    private const string Wisp = "monster.ash_wisp";
    private const string Heart = "monster.furnace_heart";

    private static readonly MapDefinitionId Quarry = new("map.ashen_quarry");

    private static readonly MessageOpcode[] Outcomes =
    {
        MessageOpcode.AttackStarted, MessageOpcode.Damage, MessageOpcode.EntityDied, MessageOpcode.ItemDropped,
        MessageOpcode.EntityRevived, MessageOpcode.SkillCastStarted, MessageOpcode.SkillResolved,
        MessageOpcode.StatusEffects, MessageOpcode.BossAnnouncement, MessageOpcode.MvpAwarded
    };

    private static readonly Lazy<Recording> SeedTwentyOne = new(() => Record(21));

    private static MapInstance QuarryOf(TestServer server)
    {
        server.World.TryGetMap(Quarry, out MapInstance? map);
        return map!;
    }

    /// <summary>
    ///     The player's DEX of 99 lands its swings, so a passive monster it attacks answers. For
    ///     <see cref="PhaseTicks" /> ticks each, it fights the live Cinder Crawler, then Basalt Crawler, then Ash Wisp,
    ///     then the boss with the lowest ID: it attacks from beside it, and from 5 m once a wisp has taken it as its
    ///     target, so the wisp casts. Its HP are topped up before every tick. Then the boss falls to it. Every step
    ///     depends only on server state.
    /// </summary>
    private static Recording Record(ulong seed)
    {
        var server = new TestServer(withEveryMap: true, withQuarry: true, withMonsters: true, randomSeed: seed);
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        server.CrossIntoTheQuarry(player);
        PlayerEntity scripted = server.PlayerOf(player);
        scripted.SetPrimary(new PrimaryStats(5, 5, 5, 5, 99, 5));
        server.Stats.Recalculate(scripted, server.Content.Jobs[scripted.Job]);
        MapInstance quarry = QuarryOf(server);
        var positions = new List<string>();
        uint sequence = 0;
        foreach (string prey in new[] { Cinder, Basalt, Wisp, Heart })
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
                else if (!quarry.TryGetMonster(target, out MonsterEntity? monster)
                         || monster == null
                         || monster.IsDead)
                {
                    MonsterEntity? next = quarry.Monsters
                        .Where(candidate => !candidate.IsDead && candidate.Definition.Id.Value == prey)
                        .OrderBy(candidate => candidate.Id.Value)
                        .FirstOrDefault();
                    if (next != null && TryStandAt(quarry, self, next, 1.2f))
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
                        TryStandAt(quarry, self, monster, distance);
                    }
                }

                // HP to spare, so the pair gathers, the silence lands, and the boss gets to cast.
                if (!self.IsDead)
                {
                    self.CurrentHealth = 1_000_000;
                }

                server.Tick();
                positions.Add(DescribeMonsters(quarry));
            }
        }

        PlayerEntity winner = server.PlayerOf(player);
        MonsterEntity heart = quarry.Monsters.Single(monster => monster.Definition.Id.Value == Heart);
        if (winner.IsDead)
        {
            server.SendRespawn(player, ++sequence);
            server.Tick(2);
        }

        heart.LogMvpDealt(winner.Character, 1);
        server.Combat.Kill(quarry, heart, winner, server.CurrentTick);
        server.Tick(TestServer.TickRate);
        string bosses = string.Join(
            ";",
            server.MonsterAi.DescribeBosses(server.CurrentTick)
                .Where(boss => boss.Map == Quarry)
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
        Recording first = SeedTwentyOne.Value;
        Recording other = Record(22);

        Assert.That(other.Outcomes, Is.Not.EqualTo(first.Outcomes));
        Assert.That(other.Positions, Is.Not.EqualTo(first.Positions));
    }

    [Test]
    public void SameSeed_ReplaysTheBiteTheCallTheSilenceTheBossesCastAndTheMvpsRoll()
    {
        Recording first = SeedTwentyOne.Value;
        Recording second = Record(21);
        long self = first.Messages
            .Where(message => message.Opcode == MessageOpcode.WorldEntered)
            .Select(message => WorldEntered.TryRead(message.Payload, out WorldEntered? entered)
                ? entered!.LocalEntity.Value
                : 0)
            .Last();
        var basalts = Read(first, MessageOpcode.EntitySpawn, payload =>
                EntitySpawn.TryRead(payload, out EntitySpawn? spawn) ? spawn : null)
            .Where(spawn => spawn.DefinitionId == Basalt)
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
                     && basalts.Contains(started.Attacker)
                     && !struck.Contains(started.Attacker))
            {
                joined.Add(started.Attacker);
            }
        }

        Assert.That(joined, Is.Not.Empty, "a basalt crawler the player had not struck swung at it: its pair's call");
        Assert.That(
            Read(first, MessageOpcode.SkillResolved, payload =>
                    SkillResolved.TryRead(payload, out SkillResolved? resolved) ? resolved : null)
                .Select(resolved => resolved.Skill.Value)
                .Distinct(),
            Is.SupersetOf(new[] { "skill.cinder_bite", "skill.hush" }),
            "the crawler bit the player and the wisp silenced it");
        Assert.That(
            Read(first, MessageOpcode.SkillCastStarted, payload =>
                    SkillCastStarted.TryRead(payload, out SkillCastStarted? started) ? started : null)
                .Select(started => started.Skill.Value),
            Has.Some.Matches<string>(skill => skill == "skill.furnace_burst" || skill == "skill.ash_cloud"),
            "the boss began a cast");
        Assert.That(
            first.Messages.Count(message => message.Opcode == MessageOpcode.MvpAwarded),
            Is.EqualTo(1),
            "the prize was rolled and told");
        Assert.That(first.Bosses, Does.StartWith($"{Heart} False "), "the boss's return drawn");
        Assert.That(second.Outcomes, Is.EqualTo(first.Outcomes), "combat, skills, the silence, and the award bytes");
        Assert.That(second.Positions, Is.EqualTo(first.Positions), "monster positions every tick");
        Assert.That(second.Bosses, Is.EqualTo(first.Bosses), "the boss's return, to the millisecond");
    }
}
}
