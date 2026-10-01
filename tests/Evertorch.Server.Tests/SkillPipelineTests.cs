using System.Linq;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The cast pipeline on the server (Gameplay Systems §9 and the vectors of the skills research note): Strike
///     (8 SP at resolution, 130 %, a 500 ms delay, a 2 s cooldown) and First Aid (3 SP, 15 HP, 500 + 1,000 ms cast,
///     1,331 ms for the level 1 adventurer). At 20 Hz a tick is 50 ms.
/// </summary>
[TestFixture]
public sealed class SkillPipelineTests
{
    private const string Strike = "skill.strike";
    private const string FirstAid = "skill.first_aid";

    // Asks for the cast the way a command would: during the next tick, before its combat phase.
    private static CastRefusal Cast(CombatRig rig, string skill, EntityId target)
    {
        CastRefusal refusal = CastRefusal.None;
        rig.Server.AfterCommandsOnce(() => refusal = rig.Server.Combat.TryBeginCast(
            rig.Map,
            rig.Entity,
            new SkillDefinitionId(skill),
            target,
            rig.Server.CurrentTick));
        rig.Server.Tick();
        return refusal;
    }

    [Test]
    public void Cancel_DuringACast_InterruptsIt_WithoutPayingOrStartingAnyDelay()
    {
        var rig = new CombatRig();
        rig.Entity.CurrentHealth = 50;
        Cast(rig, FirstAid, default);

        rig.Cancel();
        rig.Server.Tick(40);

        Assert.That(rig.Entity.Combat.IsCasting, Is.False);
        Assert.That(rig.Entity.CurrentHealth, Is.EqualTo(50));
        Assert.That(rig.Entity.CurrentSpirit, Is.EqualTo(24), "SP due at resolution is never paid");
        Assert.That(rig.Entity.Combat.CooldownEndMs(new SkillDefinitionId(FirstAid)), Is.EqualTo(long.MinValue));
        Assert.That(rig.Entity.Combat.DelayEndsMs, Is.EqualTo(long.MinValue));
    }

    [Test]
    public void Death_DuringACast_InterruptsIt()
    {
        var rig = new CombatRig();
        Cast(rig, FirstAid, default);

        rig.Server.Combat.Kill(rig.Map, rig.Entity, null, rig.Server.CurrentTick);

        Assert.That(rig.Entity.Combat.IsCasting, Is.False);
        Assert.That(rig.Entity.CurrentSpirit, Is.EqualTo(24));
    }

    [Test]
    public void FirstAid_HoldsTheCasterStill_ThenLetsItWalk()
    {
        var rig = new CombatRig();
        WorldPosition start = rig.Entity.Position;
        Cast(rig, FirstAid, default);

        for (int tick = 0; tick < 20; tick++)
        {
            rig.Move(1f, 0f);
            rig.Server.Tick();
        }

        WorldPosition during = rig.Entity.Position;
        rig.Server.Tick(10);
        for (int tick = 0; tick < 5; tick++)
        {
            rig.Move(1f, 0f);
            rig.Server.Tick();
        }

        Assert.That(during, Is.EqualTo(start), "input is consumed but applied as standing still");
        Assert.That(rig.Entity.Position, Is.Not.EqualTo(start), "after the cast the walk resumes");
    }

    [Test]
    public void FirstAid_NearFullHp_StopsAtTheMaximum()
    {
        var rig = new CombatRig();
        rig.Entity.CurrentHealth = 60;

        Cast(rig, FirstAid, default);
        rig.Server.Tick(30);

        Assert.That(rig.Entity.CurrentHealth, Is.EqualTo(71));
    }

    [Test]
    public void FirstAid_ResolvesOnTheFirstTickAtOrAfterItsCastTime_AndHealsItsFullAmount()
    {
        var rig = new CombatRig();
        rig.Entity.CurrentHealth = 50;

        Cast(rig, FirstAid, default);
        rig.Server.Tick(26);
        int beforeTheEnd = rig.Entity.CurrentHealth;
        bool isCastingBefore = rig.Entity.Combat.IsCasting;
        rig.Server.Tick();

        Assert.That((beforeTheEnd, isCastingBefore), Is.EqualTo((50, true)), "1,300 ms into a 1,331 ms cast");
        Assert.That(rig.Entity.CurrentHealth, Is.EqualTo(65), "at 1,350 ms");
        Assert.That(rig.Entity.CurrentSpirit, Is.EqualTo(24 - 3));
    }

    // An instant cast is checked as it begins and resolves in the same tick; the movement between, a target walking off
    // or the caster stepping on, does not cut it short (finding R1 holds for a cast time alone).
    [Test]
    public void InstantStrike_WhoseTargetIsOutOfReachByItsResolution_StillResolves()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        rig.Entity.CurrentSpirit = rig.Entity.MaxSpirit;
        CastRefusal refusal = CastRefusal.NotAllowedNow;
        rig.Server.AfterCommandsOnce(() =>
        {
            refusal = rig.Server.Combat.TryBeginCast(
                rig.Map,
                rig.Entity,
                new SkillDefinitionId(Strike),
                rig.Slime.Id,
                rig.Server.CurrentTick);
            rig.StandBeside(rig.Slime, 3f);
        });
        rig.Server.Tick();

        Assert.That(refusal, Is.EqualTo(CastRefusal.None));
        Assert.That(rig.Entity.Combat.IsCasting, Is.False);
        Assert.That(rig.Slime.CurrentHealth, Is.LessThan(rig.Slime.MaxHealth), "Strike landed");
        Assert.That(rig.Entity.Combat.CooldownEndMs(new SkillDefinitionId(Strike)), Is.Not.EqualTo(long.MinValue));
    }

    // A monster's cast is checked again when it resolves too (finding R1): its target ran beyond the skill's range.
    [Test]
    public void MonsterCast_WhoseTargetRanOutOfRange_IsInterrupted()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        int health = rig.Entity.CurrentHealth;
        rig.Server.AfterCommandsOnce(() => rig.Server.Combat.BeginMonsterCast(
            rig.Map,
            rig.Slime,
            new SkillDefinitionId("skill.spark_bolt"),
            rig.Entity,
            rig.Server.CurrentTick));
        rig.Server.Tick();
        Assume.That(rig.Slime.Combat.IsCasting, Is.True);

        rig.StandBeside(rig.Slime, 10f);
        rig.Server.Tick(40);

        Assert.That(rig.Slime.Combat.IsCasting, Is.False);
        Assert.That(rig.Entity.CurrentHealth, Is.EqualTo(health));
        Assert.That(rig.Slime.Combat.CooldownEndMs(new SkillDefinitionId("skill.spark_bolt")),
            Is.EqualTo(long.MinValue));
    }

    [Test]
    public void Refusals_FollowTheCheckOrder()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        rig.Slime.CurrentHealth = 10_000;

        Assert.That(Cast(rig, "skill.basic_attack", rig.Slime.Id), Is.EqualTo(CastRefusal.NotAllowedNow), "unknown");
        Assert.That(Cast(rig, "skill.none", rig.Slime.Id), Is.EqualTo(CastRefusal.NotAllowedNow), "undefined");
        Assert.That(Cast(rig, Strike, default), Is.EqualTo(CastRefusal.InvalidTarget), "no target");
        Assert.That(Cast(rig, Strike, rig.Entity.Id), Is.EqualTo(CastRefusal.InvalidTarget), "not a monster");
        Assert.That(Cast(rig, FirstAid, rig.Slime.Id), Is.EqualTo(CastRefusal.InvalidTarget), "self skill");
        rig.Entity.CurrentSpirit = 7;
        Assert.That(Cast(rig, Strike, default), Is.EqualTo(CastRefusal.NotEnoughSp), "SP before the target");
        rig.Entity.CurrentSpirit = 24;
        rig.StandBeside(rig.Slime, 3f);
        Assert.That(Cast(rig, Strike, rig.Slime.Id), Is.EqualTo(CastRefusal.OutOfRange));
        rig.StandBeside(rig.Slime, 1.2f);
        Assert.That(Cast(rig, Strike, rig.Slime.Id), Is.EqualTo(CastRefusal.None));
        rig.Entity.CurrentSpirit = 7;
        Assert.That(Cast(rig, Strike, rig.Slime.Id), Is.EqualTo(CastRefusal.NotAllowedNow), "cooldown before SP");
        rig.Entity.CurrentSpirit = 24;
        rig.Server.Tick(40);
        Assert.That(Cast(rig, FirstAid, default), Is.EqualTo(CastRefusal.None));
        Assert.That(Cast(rig, Strike, rig.Slime.Id), Is.EqualTo(CastRefusal.NotAllowedNow), "while casting");
        rig.Server.Tick(40);
        rig.Attack(rig.Slime.Id);
        rig.Server.TickUntil(() => rig.Entity.Combat.IsSwinging);
        Assert.That(Cast(rig, FirstAid, default), Is.EqualTo(CastRefusal.NotAllowedNow), "while swinging");
        rig.Server.Combat.Kill(rig.Map, rig.Entity, null, rig.Server.CurrentTick);
        Assert.That(Cast(rig, FirstAid, default), Is.EqualTo(CastRefusal.NotAllowedNow), "dead");
    }

    [Test]
    public void Strike_HoldsItselfForItsCooldown()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        rig.Slime.CurrentHealth = 1_000;
        Cast(rig, Strike, rig.Slime.Id);
        rig.Server.Tick(38);

        CastRefusal duringCooldown = Cast(rig, Strike, rig.Slime.Id);
        CastRefusal afterCooldown = Cast(rig, Strike, rig.Slime.Id);

        Assert.That(duringCooldown, Is.EqualTo(CastRefusal.NotAllowedNow), "1,950 ms after");
        Assert.That(afterCooldown, Is.EqualTo(CastRefusal.None), "2,000 ms after");
    }

    [Test]
    public void Strike_HoldsTheNextCastForItsDelay()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        Cast(rig, Strike, rig.Slime.Id);
        rig.Server.Tick(8);

        CastRefusal duringDelay = Cast(rig, FirstAid, default);
        CastRefusal afterDelay = Cast(rig, FirstAid, default);

        Assert.That(duringDelay, Is.EqualTo(CastRefusal.NotAllowedNow), "450 ms after");
        Assert.That(afterDelay, Is.EqualTo(CastRefusal.None), "500 ms after");
    }

    [Test]
    public void Strike_OnTheSlime_Deals130PercentAndPaysItsSpWhenItResolves()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());

        CastRefusal refusal = Cast(rig, Strike, rig.Slime.Id);

        Assert.That(refusal, Is.EqualTo(CastRefusal.None));
        Assert.That(rig.Slime.CurrentHealth, Is.EqualTo(50 - 17), "a 0 ms cast resolves in the tick it begins");
        Assert.That(rig.Entity.CurrentSpirit, Is.EqualTo(24 - 8));
        Assert.That(rig.Entity.Combat.IsCasting, Is.False);
        Assert.That(rig.Slime.DamageLog.Single().Damage, Is.EqualTo(17), "skill damage counts toward experience");
    }

    [Test]
    public void Swings_WaitOutACastAndItsDelay_ThenTheAutoAttackGoesOn()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        rig.Slime.CurrentHealth = 10_000;
        rig.Attack(rig.Slime.Id);
        rig.TickUntilStarts(1);
        rig.Server.TickUntil(() => !rig.Entity.Combat.IsSwinging);
        rig.Server.Tick(8);
        int startsBefore = rig.Starts().Count;

        CastRefusal refusal = Cast(rig, Strike, rig.Slime.Id);
        uint resolved = rig.Server.CurrentTick;
        rig.TickUntilStarts(startsBefore + 1);

        Assert.That(refusal, Is.EqualTo(CastRefusal.None));
        Assert.That(rig.Entity.Combat.IsAutoAttacking, Is.True, "a cast leaves the auto-attack on");
        Assert.That(rig.Starts().Last().StartTick, Is.GreaterThanOrEqualTo(resolved + 10), "not before the delay");
    }
}
}
