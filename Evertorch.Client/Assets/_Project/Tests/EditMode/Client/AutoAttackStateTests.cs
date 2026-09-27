using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class AutoAttackStateTests
{
    private const double TickSeconds = 0.05;

    private static readonly EntityId Slime = new(300);
    private static readonly SkillDefinitionId FirstAid = new("skill.first_aid");

    private static AttackTiming SwingTiming()
    {
        return new AttackTiming(
            TimeSpan.FromMilliseconds(940),
            TimeSpan.FromMilliseconds(470),
            TimeSpan.FromMilliseconds(470),
            TimeSpan.FromMilliseconds(235));
    }

    private sealed class Rig : ICombatCommandSink
    {
        public Rig(WorldPosition slimeAt)
        {
            World = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), ClientTestGrids.Center(1, 8));
            World.OnSpawn(
                new EntitySpawn(
                    Slime,
                    EntityKind.Monster,
                    "monster.a",
                    slimeAt,
                    new WorldDirection(0f, 1f),
                    EntityStateFlags.None,
                    1000));
            Controller = new MovementController(World.Grid);
            AutoAttack = new AutoAttackState(World, Controller, this, TickSeconds);
        }

        public ClientWorld World { get; }

        public MovementController Controller { get; }

        public AutoAttackState AutoAttack { get; }

        public List<string> Sent { get; } = new();

        public uint LastSequence { get; private set; }

        public uint SendAttack(EntityId target)
        {
            Sent.Add($"attack {target.Value}");
            LastSequence++;
            return LastSequence;
        }

        public void SendCancel()
        {
            Sent.Add("cancel");
        }

        // The lock is applied as the driver applies it, before the auto-attack's own tick.
        public WorldDirection Tick()
        {
            Controller.IsLocked = World.ActionLock.Advance();
            AutoAttack.Tick(World.Predictor.Position);
            return Controller.Tick(World.Predictor.Position, World.Predictor.StepDistance);
        }

        public void MoveSlime(WorldPosition position, uint tick)
        {
            World.OnSnapshot(
                ClientWorldFixture.Snapshot(
                    tick,
                    0,
                    ClientWorldFixture.State(ClientWorldFixture.LocalEntity, World.Predictor.Position),
                    ClientWorldFixture.State(Slime, position)));
            World.Advance(1f);
        }
    }

    private static readonly EntityId OtherSlime = new(301);

    private static void SpawnOtherSlime(Rig rig, WorldPosition at)
    {
        rig.World.OnSpawn(
            new EntitySpawn(
                OtherSlime,
                EntityKind.Monster,
                "monster.a",
                at,
                new WorldDirection(0f, 1f),
                EntityStateFlags.None,
                1000));
    }

    private static void Confirm(Rig rig, EntityId target)
    {
        rig.World.OnTargetChanged(new TargetChanged(ClientWorldFixture.LocalEntity, target));
    }

    // Auto-attacking the slime from afar when the player's own cast begins, holding it for 5 ticks (250 ms).
    private static Rig AttackingDuringOwnCast()
    {
        var rig = new Rig(ClientTestGrids.Center(6, 8));
        rig.AutoAttack.Attack(Slime);
        rig.Tick();
        rig.World.OnSkillCastStarted(
            new SkillCastStarted(ClientWorldFixture.LocalEntity, FirstAid, ClientWorldFixture.LocalEntity, 1, 250));
        return rig;
    }

    [Test]
    public void Attack_DuringAWalk_EndsTheWalkSoTheSwingCanStart()
    {
        WorldPosition start = ClientTestGrids.Center(1, 8);
        var rig = new Rig(new WorldPosition(start.X + 1.2f, start.Y, start.Z));
        Assert.That(rig.Controller.TryMoveTo(start, ClientTestGrids.Center(9, 1)), Is.True);

        rig.AutoAttack.Attack(Slime);
        WorldDirection direction = rig.Tick();

        Assert.That(rig.Controller.HasPath, Is.False);
        Assert.That(direction, Is.EqualTo(default(WorldDirection)), "the target is in range: stand and swing");
        Assert.That(rig.Sent, Is.EqualTo(new[] { "attack 300" }), "no cancel for the player's own walk");
    }

    [Test]
    public void Attack_FromAfar_SendsOneRequestAndChasesUntilInRange()
    {
        var rig = new Rig(ClientTestGrids.Center(6, 8));

        rig.AutoAttack.Attack(Slime);
        WorldDirection first = rig.Tick();
        bool wasChasing = rig.Controller.IsChasing;

        Assert.That(rig.Sent, Is.EqualTo(new[] { "attack 300" }));
        Assert.That(wasChasing, Is.True);
        Assert.That(first, Is.Not.EqualTo(default(WorldDirection)));
    }

    [Test]
    public void Attack_OnASecondMonsterBeforeTheFirstIsConfirmed_KeepsAttackingTheSecond()
    {
        var rig = new Rig(ClientTestGrids.Center(6, 8));
        SpawnOtherSlime(rig, ClientTestGrids.Center(8, 8));
        rig.AutoAttack.Attack(Slime);
        rig.AutoAttack.Attack(OtherSlime);

        Confirm(rig, Slime);
        bool isActiveAfterTheOlderReply = rig.AutoAttack.IsActive;
        Confirm(rig, OtherSlime);
        rig.Tick();

        Assert.That(isActiveAfterTheOlderReply, Is.True, "the reply to the first click crossed the second");
        Assert.That(rig.AutoAttack.IsActive, Is.True);
        Assert.That(rig.AutoAttack.Target, Is.EqualTo(OtherSlime));
        Assert.That(rig.Controller.IsChasing, Is.True);
        Confirm(rig, default);
        Assert.That(rig.AutoAttack.IsActive, Is.False, "once confirmed, a clear from the server ends it");
    }

    [Test]
    public void Attack_WhenAnOlderAttackIsRejected_KeepsChasing()
    {
        var rig = new Rig(ClientTestGrids.Center(6, 8));
        SpawnOtherSlime(rig, ClientTestGrids.Center(6, 9));
        rig.AutoAttack.Attack(OtherSlime);
        uint older = rig.LastSequence;
        rig.AutoAttack.Attack(Slime);

        rig.World.OnCommandRejected(new CommandRejected(older, CommandRejectionReason.InvalidTarget));
        rig.Tick();

        Assert.That(rig.AutoAttack.IsActive, Is.True);
        Assert.That(rig.AutoAttack.Target, Is.EqualTo(Slime));
    }

    [Test]
    public void Attack_WhenTheOldTargetDiesBeforeTheNewOneIsConfirmed_KeepsAttackingTheNewOne()
    {
        var rig = new Rig(ClientTestGrids.Center(6, 8));
        SpawnOtherSlime(rig, ClientTestGrids.Center(8, 8));
        rig.AutoAttack.Attack(Slime);
        Confirm(rig, Slime);

        rig.AutoAttack.Attack(OtherSlime);
        Confirm(rig, default);
        Confirm(rig, OtherSlime);

        Assert.That(rig.AutoAttack.IsActive, Is.True);
        Assert.That(rig.AutoAttack.Target, Is.EqualTo(OtherSlime));
    }

    [Test]
    public void Attack_WhenTheServerRejectsIt_EndsTheChaseWithoutACancel()
    {
        var rig = new Rig(ClientTestGrids.Center(6, 8));
        rig.AutoAttack.Attack(Slime);
        rig.Tick();

        rig.World.OnCommandRejected(new CommandRejected(rig.LastSequence, CommandRejectionReason.InvalidTarget));
        WorldDirection direction = rig.Tick();

        Assert.That(rig.AutoAttack.IsActive, Is.False);
        Assert.That(rig.Controller.IsChasing, Is.False);
        Assert.That(direction, Is.EqualTo(default(WorldDirection)));
        Assert.That(rig.Sent, Is.EqualTo(new[] { "attack 300" }), "the server already refused; nothing to cancel");
        Assert.That(rig.World.LastRejection, Is.EqualTo(CommandRejectionReason.InvalidTarget));
    }

    [Test]
    public void Attack_WhileDead_SendsNothingAndDoesNotChase()
    {
        var rig = new Rig(ClientTestGrids.Center(8, 8));
        rig.World.OnEntityDied(new EntityDied(ClientWorldFixture.LocalEntity, Slime, 5));

        rig.AutoAttack.Attack(Slime);
        rig.AutoAttack.Tick(rig.World.Predictor.Position);

        Assert.That(rig.Sent, Is.Empty);
        Assert.That(rig.AutoAttack.IsActive, Is.False);
        Assert.That(rig.Controller.IsChasing, Is.False);
    }

    [Test]
    public void Chase_WhenTheTargetMovesMoreThanTheRepathDistance_FindsANewPath()
    {
        var rig = new Rig(ClientTestGrids.Center(6, 8));
        rig.AutoAttack.Attack(Slime);
        rig.Tick();
        WorldPosition firstGoal = rig.Controller.Path[rig.Controller.Path.Count - 1];

        rig.MoveSlime(ClientTestGrids.Center(6, 6), 1);
        rig.Tick();

        WorldPosition newGoal = rig.Controller.Path[rig.Controller.Path.Count - 1];
        Assert.That(newGoal, Is.Not.EqualTo(firstGoal));
        Assert.That(rig.Controller.IsChasing, Is.True);
    }

    [Test]
    public void Chase_WithinRangeLessTheMargin_Stops()
    {
        WorldPosition start = ClientTestGrids.Center(1, 8);
        var rig = new Rig(new WorldPosition(start.X + 1.2f, start.Y, start.Z));

        rig.AutoAttack.Attack(Slime);
        WorldDirection direction = rig.Tick();

        Assert.That(rig.Controller.IsChasing, Is.False, "1.2 m is within 1.5 − 0.2");
        Assert.That(direction, Is.EqualTo(default(WorldDirection)));
    }

    [Test]
    public void InRangeDuringOwnCast_IsNoStall_AndASecondWithoutASwingAfterItClosesIn()
    {
        WorldPosition start = ClientTestGrids.Center(1, 8);
        var rig = new Rig(new WorldPosition(start.X + 1.2f, start.Y, start.Z));
        rig.AutoAttack.Attack(Slime);
        rig.World.OnSkillCastStarted(
            new SkillCastStarted(
                ClientWorldFixture.LocalEntity,
                new SkillDefinitionId("skill.first_aid"),
                ClientWorldFixture.LocalEntity,
                1,
                1500));

        // The cast holds the player for 30 ticks; then 21 free ticks are one short of closing in.
        for (int index = 0; index < 30 + 21; index++)
        {
            rig.Tick();
        }

        bool wasChasing = rig.Controller.IsChasing;
        rig.Tick();

        Assert.That(wasChasing, Is.False, "the cast held the swing back, so it was no stall");
        Assert.That(rig.Controller.IsChasing, Is.True, "a second in range without a swing after it closes in");
    }

    [Test]
    public void InRangeWithoutASwing_ForASecond_ClosesIn()
    {
        WorldPosition start = ClientTestGrids.Center(1, 8);
        var rig = new Rig(new WorldPosition(start.X + 1.2f, start.Y, start.Z));
        rig.AutoAttack.Attack(Slime);

        for (int index = 0; index < 21; index++)
        {
            rig.Tick();
        }

        bool wasChasingAfterTwentyOne = rig.Controller.IsChasing;
        rig.Tick();

        Assert.That(wasChasingAfterTwentyOne, Is.False, "a second in range without a swing, then");
        Assert.That(rig.Controller.IsChasing, Is.True, "the next tick closes in to half the range");
    }

    [Test]
    public void LocalDeath_WithoutAClearFromTheServer_EndsTheAttackWithoutACancel()
    {
        var rig = new Rig(ClientTestGrids.Center(6, 8));
        rig.AutoAttack.Attack(Slime);
        Confirm(rig, Slime);

        rig.World.OnEntityDied(new EntityDied(ClientWorldFixture.LocalEntity, Slime, 5));
        rig.Tick();

        Assert.That(rig.AutoAttack.IsActive, Is.False);
        Assert.That(rig.Controller.IsChasing, Is.False);
        Assert.That(rig.Sent, Is.EqualTo(new[] { "attack 300" }));
    }

    [Test]
    public void ManualDirection_AfterASkillWasAskedFor_WaitsUntilItsCastIsHeardAndOver()
    {
        var rig = new Rig(ClientTestGrids.Center(6, 8));
        rig.AutoAttack.Attack(Slime);
        rig.Tick();
        rig.World.ActionLock.AwaitCast(40);

        rig.Controller.SetManualDirection(0f, 1f);
        rig.Tick();
        rig.Tick();
        int sentBeforeTheCastIsHeard = rig.Sent.Count;
        rig.World.OnSkillCastStarted(
            new SkillCastStarted(ClientWorldFixture.LocalEntity, FirstAid, ClientWorldFixture.LocalEntity, 1, 250));
        var sentByTick = new List<int>();
        for (int tick = 0; tick < 8; tick++)
        {
            rig.Tick();
            sentByTick.Add(rig.Sent.Count);
        }

        Assert.That(sentBeforeTheCastIsHeard, Is.EqualTo(1), "the cast asked for may already be under way");
        Assert.That(sentByTick, Is.EqualTo(new[] { 1, 1, 1, 1, 1, 1, 2, 2 }), "then its hold and two free ticks");
    }

    [Test]
    public void ManualDirection_DuringTheOwnCast_CancelsOnlyOnceTheCastIsOver()
    {
        Rig rig = AttackingDuringOwnCast();

        rig.Controller.SetManualDirection(0f, 1f);
        var sentByTick = new List<int>();
        for (int tick = 0; tick < 8; tick++)
        {
            rig.Tick();
            sentByTick.Add(rig.Sent.Count);
        }

        Assert.That(
            sentByTick,
            Is.EqualTo(new[] { 1, 1, 1, 1, 1, 1, 2, 2 }),
            "five ticks held by the cast, then two free ones, before the server's cast is surely over");
        Assert.That(rig.Sent, Is.EqualTo(new[] { "attack 300", "cancel" }), "a cancel would end the cast there too");
        Assert.That(rig.AutoAttack.IsActive, Is.False);
    }

    [Test]
    public void ManualDirection_ReleasedBeforeTheOwnCastIsOver_LeavesTheAttackGoing()
    {
        Rig rig = AttackingDuringOwnCast();

        rig.Controller.SetManualDirection(0f, 1f);
        rig.Tick();
        rig.Tick();
        rig.Controller.SetManualDirection(0f, 0f);
        for (int tick = 0; tick < 6; tick++)
        {
            rig.Tick();
        }

        Assert.That(rig.Sent, Is.EqualTo(new[] { "attack 300" }), "the player no longer asks to move");
        Assert.That(rig.AutoAttack.IsActive, Is.True);
        Assert.That(rig.Controller.IsChasing, Is.True, "the approach goes on");
    }

    [Test]
    public void ManualDirection_WhileAttacking_SendsOneCancelAndEndsTheChase()
    {
        var rig = new Rig(ClientTestGrids.Center(6, 8));
        rig.AutoAttack.Attack(Slime);
        rig.Tick();

        rig.Controller.SetManualDirection(0f, 1f);
        rig.Tick();
        rig.Tick();

        Assert.That(rig.Sent, Is.EqualTo(new[] { "attack 300", "cancel" }));
        Assert.That(rig.AutoAttack.IsActive, Is.False);
        Assert.That(rig.Controller.IsChasing, Is.False);
    }

    [Test]
    public void OtherAttackStarted_DoesNotLockTheLocalPlayer()
    {
        var rig = new Rig(ClientTestGrids.Center(6, 8));
        rig.World.OnAttackStarted(new AttackStarted(Slime, ClientWorldFixture.LocalEntity, 1, SwingTiming()));
        rig.Controller.SetManualDirection(0f, 1f);

        Assert.That(rig.Tick(), Is.EqualTo(new WorldDirection(0f, 1f)));
    }

    // The training sword's swing (equipment research note): an impact at 530 ms holds the player for 11 ticks, one more
    // than a fist's 470 ms.
    [Test]
    public void OwnArmedAttackStarted_LocksMovementForItsLongerWindup()
    {
        var rig = new Rig(ClientTestGrids.Center(6, 8));
        var sword = new AttackTiming(
            TimeSpan.FromMilliseconds(1060),
            TimeSpan.FromMilliseconds(530),
            TimeSpan.FromMilliseconds(530),
            TimeSpan.FromMilliseconds(265));
        rig.World.OnAttackStarted(new AttackStarted(ClientWorldFixture.LocalEntity, Slime, 1, sword));
        rig.Controller.SetManualDirection(0f, 1f);

        var directions = new List<WorldDirection>();
        for (int index = 0; index < 12; index++)
        {
            directions.Add(rig.Tick());
        }

        for (int index = 0; index < 11; index++)
        {
            Assert.That(directions[index], Is.EqualTo(default(WorldDirection)), $"tick {index}: 530 ms is 11 ticks");
        }

        Assert.That(directions[11], Is.EqualTo(new WorldDirection(0f, 1f)));
    }

    [Test]
    public void OwnAttackStarted_LocksMovementForTheWindupThenReleasesIt()
    {
        var rig = new Rig(ClientTestGrids.Center(6, 8));
        rig.World.OnAttackStarted(new AttackStarted(ClientWorldFixture.LocalEntity, Slime, 1, SwingTiming()));
        rig.Controller.SetManualDirection(0f, 1f);

        var directions = new List<WorldDirection>();
        for (int index = 0; index < 11; index++)
        {
            directions.Add(rig.Tick());
        }

        for (int index = 0; index < 10; index++)
        {
            Assert.That(directions[index], Is.EqualTo(default(WorldDirection)), $"tick {index}: 470 ms is 10 ticks");
        }

        Assert.That(directions[10], Is.EqualTo(new WorldDirection(0f, 1f)));
    }

    [Test]
    public void TargetClearedByTheServer_EndsTheAttackWithoutACancel()
    {
        var rig = new Rig(ClientTestGrids.Center(6, 8));
        rig.AutoAttack.Attack(Slime);
        rig.World.OnTargetChanged(new TargetChanged(ClientWorldFixture.LocalEntity, Slime));

        rig.World.OnTargetChanged(new TargetChanged(ClientWorldFixture.LocalEntity, default));

        Assert.That(rig.AutoAttack.IsActive, Is.False);
        Assert.That(rig.Sent, Is.EqualTo(new[] { "attack 300" }));
    }

    [Test]
    public void WalkRequested_CancelsOnlyWhileAttacking()
    {
        var rig = new Rig(ClientTestGrids.Center(6, 8));
        rig.AutoAttack.OnWalkRequested();
        rig.AutoAttack.Attack(Slime);

        rig.AutoAttack.OnWalkRequested();
        rig.AutoAttack.OnWalkRequested();

        Assert.That(rig.Sent, Is.EqualTo(new[] { "attack 300", "cancel" }));
    }

    [Test]
    public void WalkRequested_DuringTheOwnCast_EndsTheAttack_AndCancelsOnceTheCastIsOver_UnlessAttackingOrDead()
    {
        Rig[] rigs = { AttackingDuringOwnCast(), AttackingDuringOwnCast(), AttackingDuringOwnCast() };
        foreach (Rig rig in rigs)
        {
            Assert.That(
                rig.Controller.TryMoveTo(rig.World.Predictor.Position, ClientTestGrids.Center(1, 3)),
                Is.True);
            rig.AutoAttack.OnWalkRequested();
        }

        bool wasActive = rigs[0].AutoAttack.IsActive;
        rigs[1].AutoAttack.Attack(Slime);
        rigs[2].World.OnEntityDied(new EntityDied(ClientWorldFixture.LocalEntity, Slime, 5));
        var sentByTick = new List<int>();
        for (int tick = 0; tick < 8; tick++)
        {
            foreach (Rig rig in rigs)
            {
                rig.Tick();
            }

            sentByTick.Add(rigs[0].Sent.Count);
        }

        Assert.That(wasActive, Is.False, "the walk ends the auto-attack here at once");
        Assert.That(sentByTick, Is.EqualTo(new[] { 1, 1, 1, 1, 1, 1, 2, 2 }), "and the server's once the cast is over");
        Assert.That(rigs[0].Sent, Is.EqualTo(new[] { "attack 300", "cancel" }));
        Assert.That(
            (rigs[0].Controller.HasPath, rigs[0].Controller.IsChasing),
            Is.EqualTo((true, false)),
            "the walk goes on");
        Assert.That(rigs[1].Sent, Is.EqualTo(new[] { "attack 300", "attack 300" }), "the new attack replaced the stop");
        Assert.That(rigs[2].Sent, Is.EqualTo(new[] { "attack 300" }), "death ended the auto-attack there too");
    }
}
}
