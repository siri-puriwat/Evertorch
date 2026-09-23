using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class PickupStateTests
{
    private static readonly EntityId Gel = new(400);
    private static readonly EntityId Slime = new(300);
    private static readonly WorldPosition Start = ClientTestGrids.Center(1, 8);

    private sealed class Rig : IPickupCommandSink
    {
        private uint m_moveSequence;

        public Rig(float gelDistance)
        {
            World = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), Start);
            Spawn(Gel, EntityKind.ItemDrop, "item.material.slime_gel", gelDistance);
            Spawn(Slime, EntityKind.Monster, "monster.a", 2f);
            Controller = new MovementController(World.Grid);
            Pickup = new PickupState(World, Controller, this);
        }

        public ClientWorld World { get; }

        public MovementController Controller { get; }

        public PickupState Pickup { get; }

        public List<EntityId> Sent { get; } = new();

        public uint SendPickup(EntityId drop)
        {
            Sent.Add(drop);
            return (uint)Sent.Count;
        }

        public WorldDirection Tick()
        {
            Pickup.Tick(World.Predictor.Position);
            WorldDirection direction = Controller.Tick(World.Predictor.Position, World.Predictor.StepDistance);
            m_moveSequence++;
            World.Predictor.Apply(new MoveIntent(m_moveSequence, m_moveSequence, direction.X, direction.Z));
            return direction;
        }

        private void Spawn(EntityId entity, EntityKind kind, string definition, float dx)
        {
            World.OnSpawn(
                new EntitySpawn(
                    entity,
                    kind,
                    definition,
                    new WorldPosition(Start.X + dx, Start.Y, Start.Z),
                    new WorldDirection(0f, 1f),
                    EntityStateFlags.None,
                    kind == EntityKind.Monster ? (ushort)1000 : (ushort)0));
        }
    }

    [Test]
    public void Despawn_OfTheDrop_EndsThePickup()
    {
        var rig = new Rig(0.8f);
        rig.Pickup.Pickup(Gel);
        rig.Tick();
        var picked = new List<ItemPickedUp>();
        rig.World.ItemPickedUpReceived += picked.Add;

        rig.World.OnItemPickedUp(
            new ItemPickedUp(Gel, ClientWorldFixture.LocalEntity, new ItemDefinitionId("item.material.slime_gel"), 2));
        rig.World.OnDespawn(new EntityDespawn(Gel, DespawnReason.PickedUp));

        Assert.That(picked, Has.Count.EqualTo(1));
        Assert.That(rig.Pickup.IsActive, Is.False);
        Assert.That(rig.World.Remotes.ContainsKey(Gel), Is.False);
    }

    [Test]
    public void ManualMovement_BeforeReachingTheDrop_EndsThePickupWithoutAsking()
    {
        var rig = new Rig(4f);
        rig.Pickup.Pickup(Gel);
        rig.Tick();

        rig.Controller.SetManualDirection(0f, 1f);
        rig.Tick();

        Assert.That(rig.Pickup.IsActive, Is.False);
        Assert.That(rig.Sent, Is.Empty);
    }

    [Test]
    public void NearestDrop_FindsOnlyDropsWithinReach()
    {
        var near = new Rig(3f);
        var far = new Rig(PickupState.KeyReach + 0.5f);

        Assert.That(near.World.NearestDrop(Start, PickupState.KeyReach), Is.EqualTo(Gel));
        Assert.That(far.World.NearestDrop(Start, PickupState.KeyReach), Is.EqualTo(default(EntityId)));
    }

    [Test]
    public void Pickup_FromAfar_WalksWithinOneMetreAndThenAsks()
    {
        var rig = new Rig(4f);

        rig.Pickup.Pickup(Gel);
        int ticks = 0;
        while (rig.Sent.Count == 0 && ticks < 100)
        {
            rig.Tick();
            ticks++;
        }

        WorldPosition at = rig.World.Predictor.Position;
        Assert.That(rig.Sent, Is.EqualTo(new[] { Gel }));
        Assert.That(ticks, Is.GreaterThan(1), "it walked first");
        Assert.That(Start.X + 4f - at.X, Is.LessThanOrEqualTo(PickupState.StopDistance + 1e-3f));
        Assert.That(rig.Controller.IsChasing, Is.False);
    }

    [Test]
    public void Pickup_InReach_AsksOnceWithoutMoving()
    {
        var rig = new Rig(0.8f);

        rig.Pickup.Pickup(Gel);
        WorldDirection first = rig.Tick();
        rig.Tick();

        Assert.That(first, Is.EqualTo(default(WorldDirection)));
        Assert.That(rig.Sent, Is.EqualTo(new[] { Gel }));
        Assert.That(rig.Pickup.IsSent, Is.True);
    }

    [Test]
    public void Pickup_OfSomethingThatIsNotADrop_DoesNothing()
    {
        var rig = new Rig(0.8f);

        rig.Pickup.Pickup(Slime);
        rig.Tick();

        Assert.That(rig.Pickup.IsActive, Is.False);
        Assert.That(rig.Sent, Is.Empty);
    }

    [Test]
    public void Rejection_OfItsRequest_EndsThePickup()
    {
        var rig = new Rig(0.8f);
        rig.Pickup.Pickup(Gel);
        rig.Tick();

        rig.World.OnCommandRejected(new CommandRejected(99, CommandRejectionReason.Busy));
        bool isActiveAfterOther = rig.Pickup.IsActive;
        rig.World.OnCommandRejected(new CommandRejected(1, CommandRejectionReason.Busy));

        Assert.That(isActiveAfterOther, Is.True, "another command's rejection says nothing about this pickup");
        Assert.That(rig.Pickup.IsActive, Is.False);
    }
}
}
