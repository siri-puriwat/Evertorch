using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The client world's side of skills (Gameplay Systems §5.1, Network Protocol §9): the local caster holds still
///     for its cast from when it hears of it, until the cast's time runs out or its caster or target dies or leaves,
///     or the player cancels.
/// </summary>
[TestFixture]
public sealed class SkillWorldTests
{
    private static readonly EntityId Slime = new(300);
    private static readonly EntityId Other = new(301);
    private static readonly SkillDefinitionId FirstAid = new("skill.first_aid");
    private static readonly SkillDefinitionId Strike = new("skill.strike");

    private static ClientWorld CreateWorld()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), ClientTestGrids.Center(2, 8));
        foreach (EntityId entity in new[] { Slime, Other })
        {
            world.OnSpawn(
                new EntitySpawn(
                    entity,
                    entity == Slime ? EntityKind.Monster : EntityKind.Player,
                    entity == Slime ? "monster.a" : "job.adventurer",
                    ClientTestGrids.Center(4, 8),
                    new WorldDirection(0f, 1f),
                    EntityStateFlags.None,
                    1000));
        }

        return world;
    }

    private static int LockedTicks(ClientWorld world)
    {
        int ticks = 0;
        while (world.ActionLock.Advance())
        {
            ticks++;
        }

        return ticks;
    }

    // Starts a cast of the local player at the slime in a fresh world, applies one event, and checks the lock.
    private sealed class CastEnding
    {
        public void Check(Action<ClientWorld> apply, string why, bool isEnded = true)
        {
            ClientWorld world = CreateWorld();
            world.OnSkillCastStarted(new SkillCastStarted(ClientWorldFixture.LocalEntity, Strike, Slime, 10, 1500));

            apply(world);

            Assert.That(world.ActionLock.IsCastLocked, Is.EqualTo(!isEnded), why);
        }
    }

    [Test]
    public void AnotherCastersCast_IsAnnouncedButHoldsNothing()
    {
        ClientWorld world = CreateWorld();
        var heard = new List<SkillCastStarted>();
        world.SkillCastStartedReceived += heard.Add;

        world.OnSkillCastStarted(new SkillCastStarted(Other, FirstAid, default, 10, 1331));
        world.OnSkillCastStarted(new SkillCastStarted(new EntityId(999), FirstAid, default, 10, 1331));

        Assert.That(world.ActionLock.IsCastLocked, Is.False);
        Assert.That(heard, Has.Count.EqualTo(1));
        Assert.That(world.UnknownEntityEvents, Is.EqualTo(1), "a caster this client has no spawn for");
    }

    [Test]
    public void OwnCastStarted_HoldsTheLocalPlayerForTheCastTime()
    {
        ClientWorld world = CreateWorld();
        var heard = new List<SkillCastStarted>();
        world.SkillCastStartedReceived += heard.Add;

        world.OnSkillCastStarted(new SkillCastStarted(ClientWorldFixture.LocalEntity, FirstAid, default, 10, 1331));

        Assert.That(LockedTicks(world), Is.EqualTo(27), "1,331 ms at 20 Hz");
        Assert.That(heard, Has.Count.EqualTo(1));
    }

    [Test]
    public void OwnCast_EndingEarly_IsAnnounced_ButItsNaturalEndIsNot()
    {
        ClientWorld world = CreateWorld();
        int ended = 0;
        world.LocalCastEnded += () => ended++;

        world.OnSkillCastStarted(new SkillCastStarted(ClientWorldFixture.LocalEntity, Strike, Slime, 10, 100));
        LockedTicks(world);
        world.OnLocalCancel();
        int afterItsTime = ended;
        world.OnSkillCastStarted(new SkillCastStarted(ClientWorldFixture.LocalEntity, Strike, Slime, 20, 1500));
        world.OnLocalCancel();
        world.OnLocalCancel();

        Assert.That(afterItsTime, Is.Zero, "a cast that ran its time ends without the event");
        Assert.That(ended, Is.EqualTo(1), "one cancel ends one cast");
    }

    [Test]
    public void OwnCast_EndsEarly_WhenItsTargetDiesOrLeaves_ItsCasterDies_OrThePlayerCancels()
    {
        var endings = new CastEnding();
        endings.Check(world => world.OnEntityDied(new EntityDied(Slime, default, 12)), "the target died");
        endings.Check(world => world.OnDespawn(new EntityDespawn(Slime, DespawnReason.OutOfRange)), "left view");
        endings.Check(
            world => world.OnEntityDied(new EntityDied(ClientWorldFixture.LocalEntity, Slime, 12)),
            "the caster died");
        endings.Check(world => world.OnLocalCancel(), "the player cancelled");
        endings.Check(world => world.OnEntityDied(new EntityDied(Other, default, 12)), "someone else died", false);
    }

    [Test]
    public void SkillList_CooldownsCountDownFromWhenTheListArrived()
    {
        ClientWorld world = CreateWorld();
        world.Advance(3f);

        world.OnSkillList(new SkillList(new[] { new SkillListEntry(Strike, 1.5f, 8, 2000, 500, 1250) }));
        double atArrival = world.CooldownRemaining(Strike);
        world.Advance(0.5f);
        double later = world.CooldownRemaining(Strike);
        world.Advance(1f);

        Assert.That(atArrival, Is.EqualTo(1.25).Within(1e-6));
        Assert.That(later, Is.EqualTo(0.75).Within(1e-6));
        Assert.That(world.CooldownRemaining(Strike), Is.Zero, "never below 0");
        Assert.That(world.CooldownRemaining(FirstAid), Is.Zero, "not listed");
    }

    [Test]
    public void SkillList_ReplacesTheSkills_AndIsAnnounced()
    {
        ClientWorld world = CreateWorld();
        int changes = 0;
        world.SkillsChanged += () => changes++;

        world.OnSkillList(new SkillList(new[] { new SkillListEntry(Strike, 1.5f, 8, 2000, 500, 0) }));
        world.OnSkillList(new SkillList(new[] { new SkillListEntry(Strike, 1.5f, 8, 2000, 500, 1250) }));

        Assert.That(world.Skills.Count, Is.EqualTo(1));
        Assert.That(world.Skills[0].RemainingCooldownMs, Is.EqualTo(1250u));
        Assert.That(changes, Is.EqualTo(2));
    }

    [Test]
    public void SkillResolved_OnAMonster_SetsItsHealthBar_AndIsAnnounced()
    {
        ClientWorld world = CreateWorld();
        var heard = new List<SkillResolved>();
        world.SkillResolvedReceived += heard.Add;

        world.OnSkillResolved(
            new SkillResolved(ClientWorldFixture.LocalEntity, Slime, Strike, SkillOutcome.Hit, 17, 12, 660));
        world.OnSkillResolved(new SkillResolved(default, new EntityId(999), Strike, SkillOutcome.Hit, 17, 12, 0));

        Assert.That(world.Remotes[Slime].HealthPermille, Is.EqualTo(660));
        Assert.That(heard, Has.Count.EqualTo(1));
        Assert.That(world.UnknownEntityEvents, Is.EqualTo(1));
    }

    [Test]
    public void StatusEffects_ReplaceTheOwnersEffects_AndCountDownFromWhenTheyArrived()
    {
        ClientWorld world = CreateWorld();
        var focus = new StatusDefinitionId("status.focus");
        int changes = 0;
        world.StatusEffectsChanged += () => changes++;
        world.Advance(2f);

        world.OnStatusEffects(new StatusEffects(new[] { new StatusEffectEntry(focus, 60_000) }));
        double atArrival = world.StatusRemaining(focus);
        world.Advance(18.5f);
        double later = world.StatusRemaining(focus);
        world.OnStatusEffects(new StatusEffects(new StatusEffectEntry[0]));

        Assert.That(atArrival, Is.EqualTo(60.0).Within(1e-6));
        Assert.That(later, Is.EqualTo(41.5).Within(1e-6));
        Assert.That(world.StatusEffects, Is.Empty, "the effect ended");
        Assert.That(world.StatusRemaining(focus), Is.Zero);
        Assert.That(changes, Is.EqualTo(2));
    }
}
}
