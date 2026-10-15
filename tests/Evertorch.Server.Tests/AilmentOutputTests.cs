using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     What the ailments send (Network Protocol §6, §9): an afflicted player's flags reach everyone who knows its
///     body, its status list, sheet, and health its owner alone, its ailment bits its party alone, and its poison ticks
///     every watcher; a resisted ailment skill reaches every watcher as "Resisted". And a seed replays a grotto fight
///     with its ailments: the same seed gives the same bytes and the same draws, ailment rolls among them.
/// </summary>
[TestFixture]
public sealed class AilmentOutputTests
{
    private const int ChanceRange = 10_000;
    private const int FightTicks = 1200;

    private static readonly StatusDefinitionId Poisoned = new("status.poisoned");
    private static readonly StatusDefinitionId Silenced = new("status.silenced");
    private static readonly SkillDefinitionId NumbingSpark = new("skill.numbing_spark");
    private static readonly MapDefinitionId Grotto = new("map.umbral_grotto");

    private static readonly MessageOpcode[] OwnerOnly =
    {
        MessageOpcode.StatusEffects, MessageOpcode.CharacterSheet, MessageOpcode.CharacterHealth
    };

    private static readonly MessageOpcode[] Outcomes =
    {
        MessageOpcode.AttackStarted, MessageOpcode.Damage, MessageOpcode.EntityDied, MessageOpcode.EntityRevived,
        MessageOpcode.SkillCastStarted, MessageOpcode.SkillResolved, MessageOpcode.StatusEffects
    };

    private static IEnumerable<T> Read<T>(TestServer server, ConnectionId connection, MessageOpcode opcode,
        Func<byte[], T?> read)
        where T : class
    {
        return server.Transport.SentTo(connection)
            .Where(message => message.Opcode == opcode)
            .Select(message => read(message.Payload))
            .Where(message => message != null)
            .Select(message => message!);
    }

    private static EntityStateFlags[] FlagsSeenOf(TestServer server, ConnectionId watcher, EntityId body)
    {
        return Read(server, watcher, MessageOpcode.EntitySnapshot, payload =>
                EntitySnapshot.TryRead(payload, out EntitySnapshot? snapshot) ? snapshot : null)
            .SelectMany(snapshot => snapshot.Entities)
            .Where(state => state.Entity == body)
            .Select(state => state.StateFlags)
            .ToArray();
    }

    private static Damage[] PoisonTicksSeen(TestServer server, ConnectionId watcher, EntityId body)
    {
        var ticks = new List<Damage>();
        foreach (InMemoryServerTransport.SentMessage message in server.Transport.SentTo(watcher))
        {
            if (message.Opcode == MessageOpcode.Damage
                && Damage.TryRead(message.Payload, out Damage damage)
                && damage.Target == body
                && damage.Result == CombatResult.Poison)
            {
                ticks.Add(damage);
            }
        }

        return ticks.ToArray();
    }

    private static PartyMemberStatus[] PartyStatusesOf(TestServer server, ConnectionId listener, string name)
    {
        return Read(server, listener, MessageOpcode.PartyMemberStatus, payload =>
                PartyMemberStatus.TryRead(payload, out PartyMemberStatus? status) ? status : null)
            .Where(status => status.Name == name)
            .ToArray();
    }

    // A seeded fight in the grotto's crawler hall and wisp gallery at five times every ailment's chance: the player
    // attacks the nearest Grotto Crawler, then Gloom Wisp, HP topped up, while their bites, Hushes, and sparks land
    // their ailments. Every draw is counted, an ailment's roll by its range.
    private static (List<string> Bytes, int Draws, int AilmentDraws, bool HasAilments) Record(ulong seed)
    {
        var random = new CountingRandom(new SeededRandomSource(seed));
        var server = new TestServer(withEveryMap: true, withGrotto: true, withMonsters: true, randomSeed: seed,
            combatRandom: random, ailmentChanceScale: 5);
        ConnectionId player = server.EnterWorld(1);
        server.CrossIntoTheGrotto(player);
        server.World.TryGetMap(Grotto, out MapInstance? grotto);
        uint sequence = 0;
        bool hasAilments = false;
        foreach (string prey in new[] { "monster.grotto_crawler", "monster.gloom_wisp" })
        {
            EntityId target = default;
            for (int tick = 0; tick < FightTicks; tick++)
            {
                PlayerEntity self = server.PlayerOf(player);
                if (self.IsDead)
                {
                    server.SendRespawn(player, ++sequence);
                    target = default;
                }
                else if (!grotto!.TryGetMonster(target, out MonsterEntity? monster) || monster == null ||
                         monster.IsDead)
                {
                    MonsterEntity? next = grotto.Monsters
                        .Where(candidate => !candidate.IsDead && candidate.Definition.Id.Value == prey)
                        .OrderBy(candidate => candidate.Id.Value)
                        .FirstOrDefault();
                    if (next != null)
                    {
                        self.Position = new WorldPosition(next.Position.X + 1.2f, next.Position.Y, next.Position.Z);
                        target = next.Id;
                        server.Tick();
                        server.SendAttack(player, target, ++sequence);
                    }
                }

                // HP to spare, so the fight goes on where it began, as in DungeonDeterminismTests.
                if (!self.IsDead)
                {
                    self.CurrentHealth = 1_000_000;
                }

                hasAilments |= self.StatusEffects.Any(effect => effect.Ailment != Ailment.None);
                server.Tick();
            }
        }

        var bytes = server.Transport.ControlSentTo(player)
            .Where(message => Outcomes.Contains(message.Opcode))
            .Select(message => BitConverter.ToString(message.Payload))
            .ToList();
        return (bytes, random.Calls, random.CallsWithin(ChanceRange), hasAilments);
    }

    private sealed class CountingRandom : IRandomSource
    {
        private readonly IRandomSource m_inner;
        private readonly Dictionary<int, int> m_byRange = new();

        public CountingRandom(IRandomSource inner)
        {
            m_inner = inner;
        }

        public int Calls { get; private set; }

        public int Next(int exclusiveMax)
        {
            Calls++;
            m_byRange.TryGetValue(exclusiveMax, out int calls);
            m_byRange[exclusiveMax] = calls + 1;
            return m_inner.Next(exclusiveMax);
        }

        public int CallsWithin(int exclusiveMax)
        {
            return m_byRange.TryGetValue(exclusiveMax, out int calls) ? calls : 0;
        }
    }

    // The Gloom Wisp's Numbing Spark as a silence at even odds, its draw 9,999: every watcher hears "Resisted".
    [Test]
    public void AResistedAilmentSkill_ReachesEveryWatcher_AsResisted_WithNoAmount()
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildRepositoryPackage();
        PackageFixture.SetValue(files, "skills.json", NumbingSpark.Value, "levels[0].effect.status",
            "\"status.silenced\"");
        PackageFixture.SetValue(files, "skills.json", NumbingSpark.Value, "levels[0].effect.statPercent",
            "{\"str\": 0, \"agi\": 0, \"vit\": 0, \"int\": 0, \"dex\": 0, \"luk\": 0}");
        PackageFixture.SetValue(files, "skills.json", NumbingSpark.Value, "levels[0].effect.chance", "0.5");
        var server = new TestServer(withEveryMap: true, withGrotto: true, withMonsters: true, withMonsterAi: false,
            content: ServerContentLoader.Load(files), combatRandom: new ScriptedRandom(9999));
        ConnectionId target = server.EnterWorld(1);
        ConnectionId watcher = server.EnterWorld(2);
        server.CrossIntoTheGrotto(target);
        server.CrossIntoTheGrotto(watcher);
        server.PlayerOf(target).Position = new WorldPosition(12f, 0f, -19.5f);
        server.PlayerOf(watcher).Position = new WorldPosition(12f, 0f, -18.5f);
        server.World.TryGetMap(Grotto, out MapInstance? grotto);
        MonsterEntity wisp = grotto!.Monsters.First(monster => monster.Definition.Id.Value == "monster.gloom_wisp");
        wisp.Position = new WorldPosition(16f, 0f, -19.5f);
        server.Tick();
        server.Transport.ClearSent();

        server.Combat.BeginMonsterCast(grotto, wisp, NumbingSpark, server.PlayerOf(target), server.CurrentTick);
        server.Tick(TestServer.TickRate);

        foreach (ConnectionId heard in new[] { target, watcher })
        {
            SkillResolved resolved = Read(server, heard, MessageOpcode.SkillResolved, payload =>
                    SkillResolved.TryRead(payload, out SkillResolved? read) ? read : null)
                .Single();
            Assert.That((resolved.Outcome, resolved.Amount), Is.EqualTo((SkillOutcome.Resisted, 0u)));
        }

        Assert.That(server.PlayerOf(target).StatusEffects, Is.Empty);
    }

    // Character 7, poisoned and silenced, stands beside 8, its party, and 9, a stranger.
    [Test]
    public void AnAfflictedPlayer_ShowsItsFlagsToAll_ItsOwnToItself_ItsBitsToItsParty_AndItsTicksToAll()
    {
        var rig = new PartyRig();
        ConnectionId seven = rig.Enter(7);
        ConnectionId eight = rig.Enter(8);
        ConnectionId nine = rig.Enter(9);
        rig.Join(seven, "Tester7", eight, "Tester8");
        TestServer server = rig.Server;
        server.Tick(TestServer.TickRate);
        server.Transport.ClearSent();
        PlayerEntity afflicted = server.PlayerOf(seven);

        server.StatusEffects.Apply(afflicted, Poisoned, default, server.World.NowMs + 60_000);
        server.StatusEffects.Apply(afflicted, Silenced, default, server.World.NowMs + 60_000);
        server.Tick(3 * TestServer.TickRate);

        const EntityStateFlags both = EntityStateFlags.Poisoned | EntityStateFlags.Silenced;
        foreach (ConnectionId watcher in new[] { eight, nine })
        {
            Assert.That(FlagsSeenOf(server, watcher, afflicted.Id), Has.Some.EqualTo(both), "its flags");
            Assert.That(server.Transport.ControlOpcodesSentTo(watcher).Intersect(OwnerOnly), Is.Empty,
                "nothing that is the owner's own");
            Assert.That(PoisonTicksSeen(server, watcher, afflicted.Id), Has.Length.GreaterThanOrEqualTo(2),
                "its poison ticks");
        }

        Assert.That(FlagsSeenOf(server, seven, afflicted.Id), Has.Some.EqualTo(both), "its own flags");
        Assert.That(server.Transport.ControlOpcodesSentTo(seven), Has.Member(MessageOpcode.StatusEffects)
            .And.Member(MessageOpcode.CharacterHealth), "its list and its health");
        Assert.That(PoisonTicksSeen(server, seven, afflicted.Id), Has.Length.GreaterThanOrEqualTo(2));
        Assert.That(PartyStatusesOf(server, eight, "Tester7").Last().Ailments, Is.EqualTo((byte)(0x01 | 0x10)));
        Assert.That(PartyStatusesOf(server, nine, "Tester7"), Is.Empty, "a stranger hears no party status");
    }

    [Test]
    public void SameSeed_ReplaysAFightWithItsAilments_ByteForByte_DrawForDraw()
    {
        (List<string> bytes, int draws, int ailmentDraws, bool hasAilments) first = Record(19);
        (List<string> bytes, int draws, int ailmentDraws, bool hasAilments) second = Record(19);
        List<string> other = Record(20).Bytes;

        Assert.That(first.hasAilments, Is.True, "ailments landed on the player");
        Assert.That(first.ailmentDraws, Is.GreaterThan(0), "ailment rolls were drawn");
        Assert.That(second.bytes, Is.EqualTo(first.bytes), "the same bytes");
        Assert.That((second.draws, second.ailmentDraws), Is.EqualTo((first.draws, first.ailmentDraws)),
            "the same draws");
        Assert.That(other, Is.Not.EqualTo(first.bytes), "another seed, another fight");
    }
}
}
