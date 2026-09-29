using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The local side of a skill (Gameplay Systems §5.1): a skill on the caster is asked for at once, an enemy skill
///     walks within its range less the approach margin first, and either waits for the player's own swing or cast.
/// </summary>
[TestFixture]
public sealed class SkillStateTests
{
    private static readonly EntityId Slime = new(300);
    private static readonly EntityId Other = new(301);
    private static readonly SkillDefinitionId Strike = new("skill.strike");
    private static readonly SkillDefinitionId FirstAid = new("skill.first_aid");
    private static readonly SkillDefinitionId Mend = new("skill.mend");
    private static readonly EntityId Ally = new(302);
    private static readonly WorldPosition Start = ClientTestGrids.Center(1, 8);

    private sealed class Rig : ISkillCommandSink
    {
        private uint m_moveSequence;

        public Rig(float slimeDistance)
        {
            World = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), Start);
            Spawn(Slime, slimeDistance);
            Spawn(Other, 2f);
            World.OnSkillList(
                new SkillList(
                    new[]
                    {
                        new SkillListEntry(Strike, 1.5f, 8, 2000, 500, 0, 1, 1, SkillListEntry.NoPrerequisite, 0),
                        new SkillListEntry(FirstAid, 0f, 3, 0, 0, 0, 1, 1, SkillListEntry.NoPrerequisite, 0),
                        new SkillListEntry(Mend, 6f, 12, 0, 0, 0, 1, 1, SkillListEntry.NoPrerequisite, 0)
                    }));
            World.OnTargetChanged(new TargetChanged(ClientWorldFixture.LocalEntity, Slime));
            Controller = new MovementController(World.Grid);
            Skill = new SkillState(World, Controller, this, 1.0 / ClientWorldFixture.TickRate);
        }

        public ClientWorld World { get; }

        public MovementController Controller { get; }

        public SkillState Skill { get; }

        public List<(SkillDefinitionId Skill, EntityId Target)> Sent { get; } = new();

        public uint SendUseSkill(SkillDefinitionId skill, EntityId target)
        {
            Sent.Add((skill, target));
            return (uint)Sent.Count;
        }

        // As the driver runs a tick: the lock first, then the skill, then the movement.
        public void Tick()
        {
            Controller.IsLocked = World.ActionLock.Advance();
            Skill.Tick(World.Predictor.Position);
            WorldDirection direction = Controller.Tick(World.Predictor.Position, World.Predictor.StepDistance);
            m_moveSequence++;
            World.Predictor.Apply(new MoveIntent(m_moveSequence, m_moveSequence, direction.X, direction.Z));
        }

        public int TickUntilSent(int limit)
        {
            int ticks = 0;
            while (Sent.Count == 0 && ticks < limit)
            {
                Tick();
                ticks++;
            }

            return ticks;
        }

        // As the frame loop runs: the world's time moves on by a tick, then the tick.
        public int AdvanceUntilSent(int limit)
        {
            int ticks = 0;
            while (Sent.Count == 0 && ticks < limit)
            {
                World.Advance(1f / ClientWorldFixture.TickRate);
                Tick();
                ticks++;
            }

            return ticks;
        }

        public void SpawnPlayer(EntityId entity, float dx)
        {
            World.OnSpawn(
                new EntitySpawn(
                    entity,
                    EntityKind.Player,
                    "job.adventurer",
                    new WorldPosition(Start.X + dx, Start.Y, Start.Z),
                    new WorldDirection(0f, 1f),
                    EntityStateFlags.None,
                    0));
        }

        private void Spawn(EntityId entity, float dx)
        {
            World.OnSpawn(
                new EntitySpawn(
                    entity,
                    EntityKind.Monster,
                    "monster.a",
                    new WorldPosition(Start.X + dx, Start.Y, Start.Z),
                    new WorldDirection(0f, 1f),
                    EntityStateFlags.None,
                    1000));
        }
    }

    private static float DistanceToSlime(Rig rig)
    {
        WorldPosition at = rig.World.Predictor.Position;
        return (float)Math.Sqrt(Math.Pow(at.X - (Start.X + 4f), 2) + Math.Pow(at.Z - Start.Z, 2));
    }

    // An ally skill goes to the selected player, walking within its range less the margin first (Gameplay Systems §9).
    [Test]
    public void AllySkill_ForASelectedPlayer_WalksWithinRangeThenAsksAtIt()
    {
        var rig = new Rig(4f);
        rig.SpawnPlayer(Ally, 8f);
        rig.World.OnTargetChanged(new TargetChanged(ClientWorldFixture.LocalEntity, Ally));

        bool isStarted = rig.Skill.Use(Mend, SkillTargetType.Ally);
        int ticks = rig.TickUntilSent(200);
        float distance = Math.Abs(rig.World.Predictor.Position.X - (Start.X + 8f));

        Assert.That(isStarted, Is.True);
        Assert.That(rig.Sent, Is.EqualTo(new[] { (Mend, Ally) }));
        Assert.That(ticks, Is.GreaterThan(1), "it walked first");
        Assert.That(distance, Is.LessThanOrEqualTo(6f).And.GreaterThan(4.5f));
    }

    // With a monster or nothing selected, an ally skill lands on the caster, as a gamepad's always does
    // (Prototype Content §4).
    [Test]
    public void AllySkill_WithAMonsterOrNothingSelected_IsForTheCaster()
    {
        var monster = new Rig(4f);
        var nothing = new Rig(4f);
        nothing.World.OnTargetChanged(new TargetChanged(ClientWorldFixture.LocalEntity, default));

        bool[] started =
            { monster.Skill.Use(Mend, SkillTargetType.Ally), nothing.Skill.Use(Mend, SkillTargetType.Ally) };
        monster.Tick();
        nothing.Tick();

        Assert.That(started, Is.EqualTo(new[] { true, true }));
        Assert.That(monster.Sent, Is.EqualTo(new[] { (Mend, default(EntityId)) }), "never at the monster");
        Assert.That(nothing.Sent, Is.EqualTo(new[] { (Mend, default(EntityId)) }));
        Assert.That(monster.Controller.IsChasing, Is.False);
    }

    [Test]
    public void EnemySkill_AfterTheTargetDied_OrAnotherWasConfirmed_EndsWithoutAsking()
    {
        var died = new Rig(4f);
        died.Skill.Use(Strike, SkillTargetType.Enemy);
        died.Tick();
        died.World.OnEntityDied(new EntityDied(Slime, default, 3));
        died.Tick();
        var retargeted = new Rig(4f);
        retargeted.Skill.Use(Strike, SkillTargetType.Enemy);
        retargeted.Tick();
        retargeted.World.OnTargetChanged(new TargetChanged(ClientWorldFixture.LocalEntity, Other));
        retargeted.Tick();

        Assert.That(died.Sent, Is.Empty);
        Assert.That((died.Skill.IsActive, died.Controller.IsChasing), Is.EqualTo((false, false)));
        Assert.That(retargeted.Sent, Is.Empty);
        Assert.That((retargeted.Skill.IsActive, retargeted.Controller.IsChasing), Is.EqualTo((false, false)));
    }

    // An enemy skill is never asked for at a player, which the server would refuse (Gameplay Systems §6).
    [Test]
    public void EnemySkill_AtASelectedPlayer_StartsNothing()
    {
        var rig = new Rig(4f);
        rig.SpawnPlayer(Ally, 1f);
        rig.World.OnTargetChanged(new TargetChanged(ClientWorldFixture.LocalEntity, Ally));

        bool isStarted = rig.Skill.Use(Strike, SkillTargetType.Enemy);
        rig.Tick();

        Assert.That(isStarted, Is.False);
        Assert.That(rig.Sent, Is.Empty);
    }

    [Test]
    public void EnemySkill_FromAfar_WalksWithinItsRangeLessTheMargin_ThenAsksOnce()
    {
        var rig = new Rig(4f);

        bool isStarted = rig.Skill.Use(Strike, SkillTargetType.Enemy);
        rig.TickUntilSent(100);
        rig.Tick();

        Assert.That(isStarted, Is.True);
        Assert.That(rig.Sent, Is.EqualTo(new[] { (Strike, Slime) }));
        Assert.That(DistanceToSlime(rig), Is.LessThanOrEqualTo(1.5f - AutoAttackState.StopMargin + 1e-3f));
        Assert.That(DistanceToSlime(rig), Is.GreaterThan(1.5f - AutoAttackState.StopMargin - 0.3f), "no closer");
        Assert.That(rig.Skill.IsActive, Is.False);
        Assert.That(rig.Controller.IsChasing, Is.False);
    }

    [Test]
    public void ManualMovement_BeforeReachingTheTarget_EndsTheApproachWithoutAsking()
    {
        var rig = new Rig(4f);
        rig.Skill.Use(Strike, SkillTargetType.Enemy);
        rig.Tick();

        rig.Controller.SetManualDirection(0f, 1f);
        rig.Tick();

        Assert.That(rig.Skill.IsActive, Is.False);
        Assert.That(rig.Sent, Is.Empty);
    }

    [Test]
    public void Request_DuringTheAfterCastDelay_IsHeldUntilItEnds()
    {
        var rig = new Rig(1f);
        rig.World.OnSkillResolved(
            new SkillResolved(ClientWorldFixture.LocalEntity, Slime, Strike, SkillOutcome.Hit, 17, 12, 660));

        rig.Skill.Use(FirstAid, SkillTargetType.Self);
        int ticks = rig.AdvanceUntilSent(40);

        Assert.That(ticks, Is.EqualTo(10), "Strike's after-cast delay of 500 ms");
        Assert.That(rig.Sent, Is.EqualTo(new[] { (FirstAid, default(EntityId)) }));
    }

    [Test]
    public void Request_DuringTheSkillsCooldown_IsSentOnce_WhenItIsReady()
    {
        var cooling = new SkillList(
            new[]
            {
                new SkillListEntry(Strike, 1.5f, 8, 2000, 500, 1000, 1, 1, SkillListEntry.NoPrerequisite, 0),
                new SkillListEntry(FirstAid, 0f, 3, 0, 0, 0, 1, 1, SkillListEntry.NoPrerequisite, 0)
            });
        var strike = new Rig(1f);
        strike.World.OnSkillList(cooling);
        var firstAid = new Rig(1f);
        firstAid.World.OnSkillList(cooling);

        strike.Skill.Use(Strike, SkillTargetType.Enemy);
        firstAid.Skill.Use(FirstAid, SkillTargetType.Self);
        int strikeTicks = strike.AdvanceUntilSent(40);
        int firstAidTicks = firstAid.AdvanceUntilSent(40);
        for (int tick = 0; tick < 5; tick++)
        {
            strike.World.Advance(1f / ClientWorldFixture.TickRate);
            strike.Tick();
        }

        Assert.That(strikeTicks, Is.EqualTo(20), "the 1,000 ms left of Strike's cooldown");
        Assert.That(strike.Sent, Is.EqualTo(new[] { (Strike, Slime) }), "sent once");
        Assert.That(firstAidTicks, Is.EqualTo(1), "Strike's cooldown is not First Aid's");
    }

    [Test]
    public void Request_DuringTheSwingOrCastLock_WaitsForItsEnd()
    {
        var swinging = new Rig(1f);
        swinging.World.ActionLock.LockForSwing(3);
        var casting = new Rig(1f);
        casting.World.ActionLock.LockForCast(5);

        swinging.Skill.Use(Strike, SkillTargetType.Enemy);
        casting.Skill.Use(FirstAid, SkillTargetType.Self);
        int swingTicks = swinging.TickUntilSent(20);
        int castTicks = casting.TickUntilSent(20);

        Assert.That(swingTicks, Is.EqualTo(5), "two ticks after the swing's last held tick");
        Assert.That(castTicks, Is.EqualTo(7), "two ticks after the cast's last held tick");
        Assert.That(swinging.Sent, Is.EqualTo(new[] { (Strike, Slime) }));
        Assert.That(casting.Sent, Is.EqualTo(new[] { (FirstAid, default(EntityId)) }));
    }

    [Test]
    public void Request_JustBeforeTheAutoAttacksNextSwing_WaitsForThatSwingsImpact()
    {
        var rig = new Rig(1f);
        rig.World.ActionLock.LockForSwing(0, 3);

        rig.Skill.Use(FirstAid, SkillTargetType.Self);
        rig.Tick();
        rig.Tick();
        int sentBeforeTheSwing = rig.Sent.Count;
        rig.World.ActionLock.LockForSwing(2, 19);
        int ticks = rig.TickUntilSent(20);

        Assert.That(sentBeforeTheSwing, Is.Zero, "the next swing was due within the margin");
        Assert.That(ticks, Is.EqualTo(4), "sent two ticks after the new swing's impact freed the player");
        Assert.That(rig.Sent, Is.EqualTo(new[] { (FirstAid, default(EntityId)) }));
    }

    [Test]
    public void Request_OnceSent_HoldsTheNextPressUntilItsCastIsHeard_OrItIsRefused()
    {
        var heard = new Rig(1f);
        var refused = new Rig(1f);
        foreach (Rig rig in new[] { heard, refused })
        {
            rig.Skill.Use(FirstAid, SkillTargetType.Self);
            rig.Tick();
            rig.Skill.Use(FirstAid, SkillTargetType.Self);
            rig.Tick();
            rig.Tick();
        }

        int[] sentWhileAwaited = { heard.Sent.Count, refused.Sent.Count };
        heard.World.OnSkillCastStarted(
            new SkillCastStarted(ClientWorldFixture.LocalEntity, FirstAid, ClientWorldFixture.LocalEntity, 1, 0));
        refused.World.OnCommandRejected(new CommandRejected(1, CommandRejectionReason.NotEnoughSp));
        heard.Tick();
        refused.Tick();

        Assert.That(sentWhileAwaited, Is.EqualTo(new[] { 1, 1 }), "the second press waits for word of the first");
        Assert.That((heard.Sent.Count, refused.Sent.Count), Is.EqualTo((2, 2)), "then goes");
    }

    [Test]
    public void Request_WhenTheExpectedSwingNeverCame_IsSentOnceItsTimePassed()
    {
        var rig = new Rig(1f);
        rig.World.ActionLock.LockForSwing(0, 3);

        rig.Skill.Use(FirstAid, SkillTargetType.Self);
        int ticks = rig.TickUntilSent(20);

        Assert.That(ticks, Is.EqualTo(3), "the auto-attack had ended; no swing came");
    }

    [Test]
    public void SelfSkill_IsAskedForOnTheNextTick_ForTheCasterItself()
    {
        var rig = new Rig(4f);

        bool isStarted = rig.Skill.Use(FirstAid, SkillTargetType.Self);
        rig.Tick();

        Assert.That(isStarted, Is.True);
        Assert.That(rig.Sent, Is.EqualTo(new[] { (FirstAid, default(EntityId)) }), "no approach and no target");
        Assert.That(rig.Controller.IsChasing, Is.False);
        Assert.That(rig.Skill.SkillsSent, Is.EqualTo(1));
    }

    [Test]
    public void SkillSlots_HoldStrikeFirstAidAndFocus_ByTheirKeys()
    {
        var skills = new List<string>();
        for (int slot = 0; slot <= SkillSlots.Count + 1; slot++)
        {
            skills.Add(SkillSlots.TryGetSkill(slot, out SkillDefinitionId skill) ? skill.Value : "-");
        }

        Assert.That(
            skills,
            Is.EqualTo(new[] { "-", "skill.strike", "skill.first_aid", "skill.focus", "-", "-", "-" }));
    }

    [Test]
    public void SkillSlots_HoldTheTwoPotions_OnFourAndFive()
    {
        var items = new List<string>();
        for (int slot = 0; slot <= SkillSlots.Count + 1; slot++)
        {
            items.Add(SkillSlots.TryGetItem(slot, out ItemDefinitionId item) ? item.Value : "-");
        }

        Assert.That(
            items,
            Is.EqualTo(
                new[]
                {
                    "-", "-", "-", "-", "item.consumable.minor_health", "item.consumable.minor_mana", "-"
                }));
    }

    [Test]
    public void Use_WithoutATarget_ForAnUnlistedSkill_OrWhileDead_StartsNothing()
    {
        var untargeted = new Rig(4f);
        untargeted.World.OnTargetChanged(new TargetChanged(ClientWorldFixture.LocalEntity, default));
        var unlisted = new Rig(4f);
        var dead = new Rig(4f);
        dead.World.OnEntityDied(new EntityDied(ClientWorldFixture.LocalEntity, Slime, 2));

        bool[] started =
        {
            untargeted.Skill.Use(Strike, SkillTargetType.Enemy),
            unlisted.Skill.Use(new SkillDefinitionId("skill.focus"), SkillTargetType.Self),
            dead.Skill.Use(FirstAid, SkillTargetType.Self)
        };
        foreach (Rig rig in new[] { untargeted, unlisted, dead })
        {
            rig.Tick();
        }

        Assert.That(started, Is.EqualTo(new[] { false, false, false }));
        Assert.That(untargeted.Sent.Count + unlisted.Sent.Count + dead.Sent.Count, Is.Zero);
    }
}
}
