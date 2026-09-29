using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The choice of a skill's target after its press, as in the reference game (Prototype Content §4): a skill for an
///     enemy or an ally waits for a click or tap on its target, a skill on the caster never waits, and Mend pressed again
///     goes on the caster.
/// </summary>
[TestFixture]
public sealed class SkillTargetingTests
{
    private static readonly SkillDefinitionId Strike = new("skill.strike");
    private static readonly SkillDefinitionId ArcaneBolt = new("skill.arcane_bolt");
    private static readonly SkillDefinitionId FirstAid = new("skill.first_aid");
    private static readonly SkillDefinitionId Mend = new("skill.mend");
    private static readonly EntityId Slime = new(300);
    private static readonly EntityId DeadSlime = new(301);
    private static readonly EntityId Player = new(302);
    private static readonly EntityId Npc = new(303);
    private static readonly EntityId Drop = new(304);

    private static ClientWorld CreateWorld()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), ClientTestGrids.Center(1, 8));
        Spawn(world, Slime, EntityKind.Monster, "monster.a", EntityStateFlags.None, 3);
        Spawn(world, DeadSlime, EntityKind.Monster, "monster.a", EntityStateFlags.Dead, 4);
        Spawn(world, Player, EntityKind.Player, "job.arcanist", EntityStateFlags.None, 5);
        Spawn(world, Npc, EntityKind.Npc, "npc.guildmaster", EntityStateFlags.None, 6);
        Spawn(world, Drop, EntityKind.ItemDrop, "item.material.slime_gel", EntityStateFlags.None, 7);
        return world;
    }

    private static void Spawn(
        ClientWorld world,
        EntityId entity,
        EntityKind kind,
        string definition,
        EntityStateFlags flags,
        int column)
    {
        world.OnSpawn(
            new EntitySpawn(
                entity,
                kind,
                definition,
                ClientTestGrids.Center(column, 8),
                new WorldDirection(0f, 1f),
                flags,
                1000));
    }

    [Test]
    public void Accepts_ForAnAllySkill_ALivePlayerOrTheCaster()
    {
        ClientWorld world = CreateWorld();
        var targeting = new SkillTargeting();
        targeting.Press(Mend, SkillTargetType.Ally);

        EntityId[] accepted = new[] { Slime, DeadSlime, Player, Npc, Drop, ClientWorldFixture.LocalEntity, default }
            .Where(entity => targeting.Accepts(world, entity))
            .ToArray();

        Assert.That(accepted, Is.EqualTo(new[] { Player, ClientWorldFixture.LocalEntity }));
    }

    [Test]
    public void Accepts_ForAnEnemySkill_OnlyALiveMonster()
    {
        ClientWorld world = CreateWorld();
        var targeting = new SkillTargeting();
        bool isAcceptedWhileIdle = targeting.Accepts(world, Slime);
        targeting.Press(Strike, SkillTargetType.Enemy);

        EntityId[] accepted = new[] { Slime, DeadSlime, Player, Npc, Drop, ClientWorldFixture.LocalEntity, default }
            .Where(entity => targeting.Accepts(world, entity))
            .ToArray();

        Assert.That(isAcceptedWhileIdle, Is.False, "nothing waits");
        Assert.That(accepted, Is.EqualTo(new[] { Slime }));
    }

    // Only what the waiting skill can take is a candidate, so an NPC or a drop in front of it never takes the click.
    [Test]
    public void CollectCandidates_AreOnlyWhatTheWaitingSkillCanTake()
    {
        ClientWorld world = CreateWorld();
        var targeting = new SkillTargeting();
        var drawnAt = new WorldPosition(1.5f, 0f, 8.5f);
        var idle = new List<PickCandidate>();
        var enemy = new List<PickCandidate>();
        var ally = new List<PickCandidate>();

        targeting.CollectCandidates(world, drawnAt, idle);
        targeting.Press(Strike, SkillTargetType.Enemy);
        targeting.CollectCandidates(world, drawnAt, enemy);
        targeting.Press(Mend, SkillTargetType.Ally);
        targeting.CollectCandidates(world, drawnAt, ally);

        Assert.That(idle, Is.Empty);
        Assert.That(enemy.Select(candidate => candidate.Entity), Is.EqualTo(new[] { Slime }));
        Assert.That(
            ally.Select(candidate => candidate.Entity),
            Is.EqualTo(new[] { Player, ClientWorldFixture.LocalEntity }));
        Assert.That(ally.Last().Position, Is.EqualTo(drawnAt), "the caster where it was drawn");
    }

    [Test]
    public void Press_OfASkillOnTheCaster_IsInstant_AndEndsAChoice()
    {
        var targeting = new SkillTargeting();
        targeting.Press(Strike, SkillTargetType.Enemy);

        SkillPress press = targeting.Press(FirstAid, SkillTargetType.Self);

        Assert.That(press, Is.EqualTo(SkillPress.Instant));
        Assert.That((targeting.IsChoosing, targeting.Skill), Is.EqualTo((false, default(SkillDefinitionId))));
    }

    // Mend pressed again goes on the caster, as a click on the caster would (owner's walk, 2026-09-30).
    [Test]
    public void Press_OfAnAllySkillAgain_IsForTheCaster_AndEndsTheChoice()
    {
        var targeting = new SkillTargeting();

        SkillPress first = targeting.Press(Mend, SkillTargetType.Ally);
        SkillPress again = targeting.Press(Mend, SkillTargetType.Ally);

        Assert.That((first, again), Is.EqualTo((SkillPress.Choosing, SkillPress.OnCaster)));
        Assert.That(targeting.IsChoosing, Is.False);
    }

    [Test]
    public void Press_OfAnEnemySkill_BeginsTheChoice_AndAgainKeepsIt()
    {
        var targeting = new SkillTargeting();

        SkillPress first = targeting.Press(ArcaneBolt, SkillTargetType.Enemy);
        SkillPress again = targeting.Press(ArcaneBolt, SkillTargetType.Enemy);

        Assert.That((first, again), Is.EqualTo((SkillPress.Choosing, SkillPress.StillChoosing)));
        Assert.That(
            (targeting.IsChoosing, targeting.Skill, targeting.TargetType),
            Is.EqualTo((true, ArcaneBolt, SkillTargetType.Enemy)));
    }

    [Test]
    public void Press_OfAnotherTargetedSkill_ReplacesTheChoice()
    {
        var targeting = new SkillTargeting();
        targeting.Press(Strike, SkillTargetType.Enemy);

        SkillPress press = targeting.Press(Mend, SkillTargetType.Ally);

        Assert.That(press, Is.EqualTo(SkillPress.Choosing));
        Assert.That((targeting.Skill, targeting.TargetType), Is.EqualTo((Mend, SkillTargetType.Ally)));
        Assert.That(targeting.Cancel(), Is.True, "a choice was under way");
        Assert.That(targeting.Cancel(), Is.False, "and is no more");
    }

    [Test]
    public void PromptFor_SaysWhatToClickOrTap_AndHowToCancel()
    {
        Assert.That(
            FeedbackLines.PromptFor("Arcane Bolt", SkillTargetType.Enemy, false),
            Is.EqualTo("Arcane Bolt: click a target. Esc cancels."));
        Assert.That(
            FeedbackLines.PromptFor("Mend", SkillTargetType.Ally, false),
            Is.EqualTo("Mend: click a player or yourself. Esc cancels."));
        Assert.That(
            FeedbackLines.PromptFor("Arcane Bolt", SkillTargetType.Enemy, true),
            Is.EqualTo("Arcane Bolt: tap a target. Clear cancels."));
    }
}
}
