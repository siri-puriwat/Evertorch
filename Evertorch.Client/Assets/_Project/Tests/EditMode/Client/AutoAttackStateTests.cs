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

    private static readonly EntityId Slime = new EntityId(300);

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

        public List<string> Sent { get; } = new List<string>();

        public void SendAttack(EntityId target)
        {
            Sent.Add($"attack {target.Value}");
        }

        public void SendCancel()
        {
            Sent.Add("cancel");
        }

        public WorldDirection Tick()
        {
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

    private static readonly EntityId OtherSlime = new EntityId(301);

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
    public void OtherAttackStarted_DoesNotLockTheLocalPlayer()
    {
        var rig = new Rig(ClientTestGrids.Center(6, 8));
        rig.World.OnAttackStarted(new AttackStarted(Slime, ClientWorldFixture.LocalEntity, 1, SwingTiming()));
        rig.Controller.SetManualDirection(0f, 1f);

        Assert.That(rig.Tick(), Is.EqualTo(new WorldDirection(0f, 1f)));
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
}
}
