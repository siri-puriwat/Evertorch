using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A supplied seed replays the first jobs too (Milestone 10 verification: the first jobs' skills and Mend replay
///     identically with a supplied seed): a scripted Vanguard fights the training slimes with Heavy Blow, War Cry, and
///     Iron Guard, while a scripted Arcanist beside it casts Arcane Bolt and Clarity at the same slime and Mends the
///     Vanguard.
/// </summary>
[TestFixture]
public sealed class JobDeterminismTests
{
    private const int Ticks = 3000;
    private const string Slime = "monster.training_slime";
    private const string HeavyBlow = "skill.heavy_blow";
    private const string WarCry = "skill.war_cry";
    private const string IronGuard = "skill.iron_guard";
    private const string ArcaneBolt = "skill.arcane_bolt";
    private const string Clarity = "skill.clarity";
    private const string Mend = "skill.mend";

    private static readonly MessageOpcode[] Outcomes =
    {
        MessageOpcode.AttackStarted, MessageOpcode.Damage, MessageOpcode.EntityDied, MessageOpcode.CharacterHealth,
        MessageOpcode.SkillCastStarted, MessageOpcode.SkillResolved, MessageOpcode.CharacterProgress,
        MessageOpcode.StatusEffects
    };

    private static readonly Lazy<Recording> SeedEleven = new(() => Record(11));

    private static PlayerEntity AsFirstJob(TestServer server, ConnectionId player, string job, params string[] skills)
    {
        PlayerEntity entity = server.PlayerOf(player);
        entity.ChangeJob(new JobDefinitionId(job));
        entity.Level = 10;
        entity.JobLevel = 5;
        foreach (string skill in skills)
        {
            entity.SetSkillLevel(new SkillDefinitionId(skill), 1);
        }

        return entity;
    }

    /// <summary>
    ///     Every step depends only on server state: both stand beside the live slime with the lowest ID, the Vanguard
    ///     attacking it, raising War Cry and Iron Guard when it has no effect, and striking with Heavy Blow every hundred
    ///     ticks; the Arcanist casting Clarity when it has no effect, Mend on the Vanguard when that is below its
    ///     maximum, and Arcane Bolt at the slime every sixty ticks. Each asks at most once in twenty ticks when it waits
    ///     on an effect, well within the command limits, and SP is topped up every tick so each cast is affordable.
    /// </summary>
    private static Recording Record(ulong seed)
    {
        var server = new TestServer(withMonsters: true, randomSeed: seed, withAdventurerBuild: false);
        ConnectionId vanguard = server.EnterWorld(1);
        ConnectionId arcanist = server.EnterWorld(2);
        AsFirstJob(server, vanguard, "job.vanguard", HeavyBlow, WarCry, IronGuard);
        AsFirstJob(server, arcanist, "job.arcanist", ArcaneBolt, Clarity, Mend);
        MapInstance map = server.World.Maps.Single();
        uint vanguardSequence = 0;
        uint arcanistSequence = 0;
        EntityId target = default;
        for (int tick = 0; tick < Ticks; tick++)
        {
            PlayerEntity fighter = server.PlayerOf(vanguard);
            PlayerEntity caster = server.PlayerOf(arcanist);
            fighter.CurrentSpirit = fighter.MaxSpirit;
            caster.CurrentSpirit = caster.MaxSpirit;
            if (fighter.IsDead)
            {
                server.SendRespawn(vanguard, ++vanguardSequence);
                target = default;
            }
            else if (!map.TryGetMonster(target, out MonsterEntity? monster) || monster == null || monster.IsDead)
            {
                MonsterEntity? next = map.Monsters
                    .Where(candidate => !candidate.IsDead && candidate.Definition.Id.Value == Slime)
                    .OrderBy(candidate => candidate.Id.Value)
                    .FirstOrDefault();
                if (next != null)
                {
                    fighter.Position = new WorldPosition(next.Position.X + 1.2f, next.Position.Y, next.Position.Z);
                    caster.Position = BesideOf(map, next.Position);
                    target = next.Id;
                    server.SendAttack(vanguard, target, ++vanguardSequence);
                }
            }
            else if (fighter.StatusEffects.Count == 0 && tick % 20 == 0)
            {
                server.SendUseSkill(vanguard, WarCry, default, ++vanguardSequence);
                server.SendUseSkill(vanguard, IronGuard, default, ++vanguardSequence);
            }
            else if (tick % 100 == 0)
            {
                server.SendUseSkill(vanguard, HeavyBlow, target, ++vanguardSequence);
            }

            if (caster.IsDead)
            {
                server.SendRespawn(arcanist, ++arcanistSequence);
            }
            else if (caster.StatusEffects.Count == 0 && tick % 20 == 10)
            {
                server.SendUseSkill(arcanist, Clarity, default, ++arcanistSequence);
            }
            else if (!fighter.IsDead && fighter.CurrentHealth < fighter.MaxHealth && tick % 20 == 0)
            {
                server.SendUseSkill(arcanist, Mend, fighter.Id, ++arcanistSequence);
            }
            else if (target != default && tick % 60 == 0)
            {
                server.SendUseSkill(arcanist, ArcaneBolt, target, ++arcanistSequence);
            }

            server.Tick();
        }

        return new Recording(
            Describe(server, vanguard),
            Describe(server, arcanist),
            Resolved(server, vanguard, HeavyBlow),
            Resolved(server, arcanist, ArcaneBolt),
            Resolved(server, vanguard, Mend));
    }

    // West of the slime, or north or south of it, where the caster can stand; never east, towards the portal.
    private static WorldPosition BesideOf(MapInstance map, WorldPosition slime)
    {
        NavigationGrid grid = map.Definition.Navigation;
        foreach ((float dx, float dz) in new[] { (-2.5f, 0f), (0f, 2.5f), (0f, -2.5f) })
        {
            if (grid.CanOccupy(slime.X + dx, slime.Z + dz)
                && grid.TrySampleHeight(slime.X + dx, slime.Z + dz, out float height))
            {
                return new WorldPosition(slime.X + dx, height, slime.Z + dz);
            }
        }

        return slime;
    }

    private static List<string> Describe(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => Outcomes.Contains(message.Opcode))
            .Select(message => BitConverter.ToString(message.Payload))
            .ToList();
    }

    // The resolutions of skill that player heard.
    private static int Resolved(TestServer server, ConnectionId player, string skill)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.SkillResolved)
            .Count(message => SkillResolved.TryRead(message.Payload, out SkillResolved? resolved)
                && resolved!.Skill.Value == skill);
    }

    private sealed class Recording
    {
        public Recording(List<string> vanguard, List<string> arcanist, int heavyBlows, int bolts, int mends)
        {
            Vanguard = vanguard;
            Arcanist = arcanist;
            HeavyBlows = heavyBlows;
            Bolts = bolts;
            Mends = mends;
        }

        public List<string> Vanguard { get; }

        public List<string> Arcanist { get; }

        public int HeavyBlows { get; }

        public int Bolts { get; }

        public int Mends { get; }
    }

    [Test]
    public void AnotherSeed_ProducesADifferentRun()
    {
        Recording other = Record(12);

        Assert.That(
            (other.Vanguard, other.Arcanist),
            Is.Not.EqualTo((SeedEleven.Value.Vanguard, SeedEleven.Value.Arcanist)));
    }

    [Test]
    public void TheSameSeed_ReplaysTheFirstJobsSkillsAndMend_Identically()
    {
        Recording again = Record(11);
        Recording first = SeedEleven.Value;

        Assert.That(first.Vanguard, Has.Count.GreaterThan(100), "a long fight was recorded");
        Assert.That(
            new[] { first.HeavyBlows, first.Bolts, first.Mends },
            Is.All.GreaterThan(0),
            "Heavy Blow, Arcane Bolt, and Mend on the Vanguard were all resolved");
        Assert.That(again.Vanguard, Is.EqualTo(first.Vanguard));
        Assert.That(again.Arcanist, Is.EqualTo(first.Arcanist));
    }
}
}
