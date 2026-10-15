using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using Evertorch.Rules;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The monsters' ailments (Gameplay Systems §10): the Forest Crawler's Venom Bite and the Grotto Crawler's Crushing
///     Bite riders, the Gloom Wisp's Hush, the Hollow Wisp's Lull, the Slime Monarch's Frost Breath over the players
///     around it, and the riders Spark Bolt and Grave Bite gained, each cast by the test with every ailment's draw
///     scripted.
/// </summary>
[TestFixture]
public sealed class AilmentMonsterSkillTests
{
    private static readonly MapDefinitionId Ground = new("map.training_ground");
    private static readonly MapDefinitionId Field = new("map.training_field");
    private static readonly MapDefinitionId Grotto = new("map.umbral_grotto");

    private static MapPortal PortalOf(TestServer server, MapDefinitionId from, MapDefinitionId to)
    {
        server.World.TryGetMap(from, out MapInstance? map);
        return map!.Definition.Portals.First(portal => portal.DestinationMap == to);
    }

    // Players 1 and 2 on the monster's map, the first a metre from it and the second beside the first; the AI is off,
    // so only the test casts, and every ailment's draw is the one given.
    private static (TestServer Server, MapInstance Map, MonsterEntity Monster, PlayerEntity[] Players) Beside(
        string monster,
        int ailmentDraw)
    {
        var server = new TestServer(withEveryMap: true, withGrotto: true, withMonsters: true, withMonsterAi: false,
            combatRandom: new AilmentDraws(ailmentDraw));
        ConnectionId[] connections = { server.EnterWorld(1), server.EnterWorld(2) };
        bool isInTheGrotto = monster != "monster.forest_crawler" && monster != "monster.spark_wisp";
        foreach (ConnectionId connection in connections)
        {
            if (isInTheGrotto)
            {
                server.CrossIntoTheGrotto(connection);
            }
            else
            {
                server.PlayerOf(connection).Position = PortalOf(server, Ground, Field).Center;
                server.Tick(TestServer.TickRate + 1);
            }
        }

        server.World.TryGetMap(isInTheGrotto ? Grotto : Field, out MapInstance? map);
        MonsterEntity found = map!.Monsters.First(candidate => candidate.Definition.Id.Value == monster);
        PlayerEntity[] players = connections.Select(server.PlayerOf).ToArray();
        for (int index = 0; index < players.Length; index++)
        {
            players[index].Position = new WorldPosition(found.Position.X + 1f, found.Position.Y,
                found.Position.Z + 0.5f * index);
            players[index].CurrentHealth = players[index].MaxHealth;
        }

        server.Tick();
        return (server, map, found, players);
    }

    private static void Cast(TestServer server, MapInstance map, MonsterEntity monster, string skill, WorldEntity at)
    {
        var id = new SkillDefinitionId(skill);
        Assert.That(server.Combat.CanMonsterCast(map, monster, id, at, server.CurrentTick), Is.True, skill);
        server.Combat.BeginMonsterCast(map, monster, id, at, server.CurrentTick);
        server.Tick(2 * TestServer.TickRate);
    }

    [TestCase("monster.forest_crawler", "skill.venom_bite", Ailment.Poison, 19_000)]
    [TestCase("monster.grotto_crawler", "skill.crushing_bite", Ailment.Stun, 4000)]
    [TestCase("monster.spark_wisp", "skill.spark_bolt", Ailment.Blind, 19_000)]
    [TestCase("monster.barrow_crawler", "skill.grave_bite", Ailment.Poison, 20_000)]
    public void Rider_OnALandedHit_BringsItsAilment_AndAtTheTopDrawNone(
        string monster,
        string skill,
        Ailment ailment,
        int longest)
    {
        (TestServer server, MapInstance map, MonsterEntity caster, PlayerEntity[] players) = Beside(monster, 0);
        (TestServer missed, MapInstance missedMap, MonsterEntity missedCaster, PlayerEntity[] missedPlayers) =
            Beside(monster, 9999);

        Cast(server, map, caster, skill, players[0]);
        Cast(missed, missedMap, missedCaster, skill, missedPlayers[0]);

        Assert.That(players[0].HasAilment(ailment), Is.True, $"{skill} brought {ailment}");
        Assert.That(
            players[0].StatusEffects.Single().EndMs - server.World.NowMs,
            Is.InRange(1, longest),
            "its duration, less what the player resists");
        Assert.That(missedPlayers[0].StatusEffects, Is.Empty, "the top draw lands nothing");
    }

    [TestCase("monster.gloom_wisp", "skill.hush", Ailment.Silence)]
    [TestCase("monster.hollow_wisp", "skill.lull", Ailment.Sleep)]
    public void AilmentSkill_LandsOnItsTarget_AndAnswersApplied(string monster, string skill, Ailment ailment)
    {
        (TestServer server, MapInstance map, MonsterEntity caster, PlayerEntity[] players) = Beside(monster, 0);
        server.Transport.ClearSent();

        Cast(server, map, caster, skill, players[0]);

        Assert.That(players[0].HasAilment(ailment), Is.True);
        Assert.That(players[1].StatusEffects, Is.Empty, "the other player stands outside the skill");
        Assert.That(
            server.Transport.SentTo(server.Sessions.Sessions.First(session => session.Player == players[0]).Connection)
                .Where(message => message.Opcode == MessageOpcode.SkillResolved)
                .Select(message => SkillResolved.TryRead(message.Payload, out SkillResolved? read) ? read! : null!)
                .Single(resolved => resolved.Skill.Value == skill).Outcome,
            Is.EqualTo(SkillOutcome.Applied));
    }

    // Every hit lands at the least of its draws, and every ailment's roll draws the given number.
    private sealed class AilmentDraws : IRandomSource
    {
        private readonly int m_draw;

        public AilmentDraws(int draw)
        {
            m_draw = draw;
        }

        public int Next(int exclusiveMax)
        {
            return exclusiveMax switch
            {
                1000 => 999,
                10_000 => m_draw,
                _ => 0
            };
        }
    }

    [Test]
    public void FrostBreath_FreezesEveryPlayerAroundTheMonarch_EachWithItsOwnDraw()
    {
        (TestServer server, MapInstance map, MonsterEntity monarch, PlayerEntity[] players) =
            Beside("monster.slime_monarch", 0);

        Cast(server, map, monarch, "skill.frost_breath", monarch);

        Assert.That(players.Select(player => player.HasAilment(Ailment.Freeze)), Is.All.True);
        Assert.That(monarch.StatusEffects, Is.Empty, "the breath is around its caster, never on it");
    }

    [Test]
    public void Lull_IsBrokenByTheNextLandedHit()
    {
        (TestServer server, MapInstance map, MonsterEntity wisp, PlayerEntity[] players) =
            Beside("monster.hollow_wisp", 0);
        Cast(server, map, wisp, "skill.lull", players[0]);
        bool wasAsleep = players[0].HasAilment(Ailment.Sleep);

        wisp.Target = players[0].Id;
        wisp.Combat.IsAutoAttacking = true;
        server.TickUntil(() => !players[0].HasAilment(Ailment.Sleep));

        Assert.That(wasAsleep, Is.True);
        Assert.That(players[0].HasAilment(Ailment.Sleep), Is.False, "the wisp's own swing woke it");
    }
}
}
