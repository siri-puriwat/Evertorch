using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A supplied seed replays the build too (Milestone 9 verification: the effects of the statistics and the skill
///     levels replay identically with a supplied seed): a scripted Adventurer at base level 10 and job level 6 raises
///     AGI, DEX, and VIT, learns Strike to level 3 and Focus, and fights the training slimes with Strike and Focus,
///     raising and learning again halfway.
/// </summary>
[TestFixture]
public sealed class BuildDeterminismTests
{
    private const int Ticks = 3000;
    private const int Halfway = Ticks / 2;
    private const string Slime = "monster.training_slime";

    private static readonly MessageOpcode[] Outcomes =
    {
        MessageOpcode.AttackStarted, MessageOpcode.Damage, MessageOpcode.EntityDied, MessageOpcode.CharacterHealth,
        MessageOpcode.SkillCastStarted, MessageOpcode.SkillResolved, MessageOpcode.CharacterProgress,
        MessageOpcode.StatusEffects, MessageOpcode.CharacterSheet, MessageOpcode.SkillList
    };

    // What the fight did, without the build's own messages.
    private static readonly MessageOpcode[] Combat =
    {
        MessageOpcode.AttackStarted, MessageOpcode.Damage, MessageOpcode.SkillResolved
    };

    private static readonly Lazy<Recording> SeedEleven = new(() => Record(11, true));

    /// <summary>
    ///     Every step depends only on server state: the build's commands at the first tick and halfway, then an attack
    ///     on the live slime with the lowest ID from beside it, Focus whenever it is not focused, and Strike every
    ///     forty ticks. With <paramref name="isBuilt" /> false the same character fights with nothing spent but Strike
    ///     1 and Focus 1.
    /// </summary>
    private static Recording Record(ulong seed, bool isBuilt)
    {
        var server = new TestServer(withMonsters: true, randomSeed: seed, withAdventurerBuild: false);
        ConnectionId player = server.EnterWorld(1);
        PlayerEntity self = server.PlayerOf(player);
        self.Level = 10;
        self.JobLevel = 6;
        MapInstance map = server.World.Maps.Single();
        uint sequence = 0;
        server.SendLearnSkill(player, "skill.strike", ++sequence);
        server.SendLearnSkill(player, "skill.focus", ++sequence);
        EntityId target = default;
        for (int tick = 0; tick < Ticks; tick++)
        {
            if (isBuilt && tick == 0)
            {
                server.SendAllocateStat(player, PrimaryStat.Agi, 5, ++sequence);
                server.SendAllocateStat(player, PrimaryStat.Dex, 5, ++sequence);
                server.SendLearnSkill(player, "skill.strike", ++sequence);
            }
            else if (isBuilt && tick == Halfway)
            {
                server.SendAllocateStat(player, PrimaryStat.Vit, 3, ++sequence);
                server.SendLearnSkill(player, "skill.strike", ++sequence);
            }

            self = server.PlayerOf(player);
            if (self.IsDead)
            {
                server.SendRespawn(player, ++sequence);
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
                    self.Position = new WorldPosition(next.Position.X + 1.2f, next.Position.Y, next.Position.Z);
                    target = next.Id;
                    server.SendAttack(player, target, ++sequence);
                }
            }
            else if (self.StatusEffects.Count == 0)
            {
                server.SendUseSkill(player, "skill.focus", default, ++sequence);
            }
            else if (tick % 40 == 0)
            {
                server.SendUseSkill(player, "skill.strike", target, ++sequence);
            }

            server.Tick();
        }

        PlayerEntity final = server.PlayerOf(player);
        final.Skills.TryGetValue(new SkillDefinitionId("skill.strike"), out int strike);
        return new Recording(
            Describe(server, player, Outcomes),
            Describe(server, player, Combat),
            final.Primary,
            strike);
    }

    private static List<string> Describe(TestServer server, ConnectionId player, MessageOpcode[] opcodes)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => opcodes.Contains(message.Opcode))
            .Select(message => BitConverter.ToString(message.Payload))
            .ToList();
    }

    private sealed class Recording
    {
        public Recording(List<string> outcomes, List<string> combat, PrimaryStats primary, int strike)
        {
            Outcomes = outcomes;
            Combat = combat;
            Primary = primary;
            Strike = strike;
        }

        public List<string> Outcomes { get; }

        public List<string> Combat { get; }

        public PrimaryStats Primary { get; }

        public int Strike { get; }
    }

    [Test]
    public void AnotherSeed_ProducesADifferentRun()
    {
        Assert.That(Record(12, true).Outcomes, Is.Not.EqualTo(SeedEleven.Value.Outcomes));
    }

    [Test]
    public void TheSameSeed_ReplaysTheBuildsEffects_Identically()
    {
        Recording again = Record(11, true);

        Assert.That(SeedEleven.Value.Outcomes, Has.Count.GreaterThan(100), "a long fight was recorded");
        Assert.That(
            (SeedEleven.Value.Primary, SeedEleven.Value.Strike),
            Is.EqualTo((new PrimaryStats(5, 10, 8, 5, 10, 5), 3)),
            "every raise and learned level was made");
        Assert.That(again.Outcomes, Is.EqualTo(SeedEleven.Value.Outcomes));
    }

    // The build is part of what the seed replays: the same seed without it fights differently, its swings, hits, and
    // skills, not only its sheets.
    [Test]
    public void TheSameSeed_WithoutTheBuild_FightsDifferently()
    {
        Recording unbuilt = Record(11, false);

        Assert.That((unbuilt.Primary, unbuilt.Strike), Is.EqualTo((new PrimaryStats(5, 5, 5, 5, 5, 5), 1)));
        Assert.That(unbuilt.Combat, Is.Not.EqualTo(SeedEleven.Value.Combat));
    }
}
}
