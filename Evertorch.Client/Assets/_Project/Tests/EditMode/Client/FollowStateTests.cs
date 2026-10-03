using System;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class FollowStateTests
{
    private static readonly EntityId Bobby = new(7);
    private static readonly EntityId Slime = new(300);
    private static readonly WorldPosition Start = ClientTestGrids.Center(1, 8);

    private sealed class Rig
    {
        private uint m_moveSequence;

        public Rig(float bobbyDistance, bool isInParty = true)
        {
            World = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), Start);
            Spawn(Bobby, EntityKind.Player, "job.adventurer", bobbyDistance, "Bobby");
            Spawn(Slime, EntityKind.Monster, "monster.a", 2f, string.Empty);
            Party = new ClientParty();
            if (isInParty)
            {
                Party.Apply(Roster("Ann0", "Bobby"));
            }

            Controller = new MovementController(World.Grid);
            Follow = new FollowState(World, Controller, Party);
        }

        public ClientWorld World { get; }

        public MovementController Controller { get; }

        public ClientParty Party { get; }

        public FollowState Follow { get; }

        public static PartyRoster Roster(params string[] names)
        {
            var map = new MapDefinitionId("map.training_ground");
            var job = new JobDefinitionId("job.adventurer");
            var members = new PartyRosterEntry[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                members[i] = new PartyRosterEntry(names[i], job, 3, map);
            }

            return new PartyRoster(0, members);
        }

        public WorldDirection Tick()
        {
            Follow.Tick(World.Predictor.Position);
            WorldDirection direction = Controller.Tick(World.Predictor.Position, World.Predictor.StepDistance);
            m_moveSequence++;
            World.Predictor.Apply(new MoveIntent(m_moveSequence, m_moveSequence, direction.X, direction.Z));
            return direction;
        }

        public void TickUntilStill(int limit = 200)
        {
            for (int i = 0; i < limit; i++)
            {
                if (Tick() == default)
                {
                    return;
                }
            }
        }

        public void MoveBobby(WorldPosition position, uint tick)
        {
            World.OnSnapshot(
                ClientWorldFixture.Snapshot(
                    tick,
                    0,
                    ClientWorldFixture.State(ClientWorldFixture.LocalEntity, World.Predictor.Position),
                    ClientWorldFixture.State(Bobby, position)));
            World.Advance(1f);
        }

        public float DistanceToBobby()
        {
            RemoteEntityBuffer buffer = World.Remotes[Bobby].Buffer;
            buffer.TrySample(World.RemoteRenderTime, out WorldPosition bobby, out WorldDirection _);
            WorldPosition at = World.Predictor.Position;
            float dx = at.X - bobby.X;
            float dz = at.Z - bobby.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private void Spawn(EntityId entity, EntityKind kind, string definition, float dx, string name)
        {
            World.OnSpawn(
                new EntitySpawn(
                    entity,
                    kind,
                    definition,
                    new WorldPosition(Start.X + dx, Start.Y, Start.Z),
                    new WorldDirection(0f, 1f),
                    EntityStateFlags.None,
                    kind == EntityKind.Monster ? (ushort)1000 : (ushort)0,
                    string.Empty,
                    name));
        }
    }

    [Test]
    public void AMemberWithinTheMargin_DoesNotMoveIt_AndOneBeyondItDoes()
    {
        var rig = new Rig(FollowState.FollowDistance + FollowState.Margin - 0.1f);
        rig.Follow.Follow(Bobby);

        WorldDirection near = rig.Tick();
        rig.MoveBobby(new WorldPosition(Start.X + 6f, Start.Y, Start.Z), 1);
        WorldDirection far = rig.Tick();

        Assert.That(near, Is.EqualTo(default(WorldDirection)));
        Assert.That(far, Is.Not.EqualTo(default(WorldDirection)));
        Assert.That(rig.Controller.IsChasing, Is.True);
    }

    [Test]
    public void Cancel_EndsItsOwnChase_ButNotAWalkThePlayerAskedFor()
    {
        var chasing = new Rig(6f);
        chasing.Follow.Follow(Bobby);
        chasing.Tick();
        var walking = new Rig(6f);
        walking.Follow.Follow(Bobby);
        walking.Tick();
        walking.Controller.TryMoveTo(walking.World.Predictor.Position, ClientTestGrids.Center(9, 1));

        chasing.Follow.Cancel();
        walking.Follow.Cancel();

        Assert.That(chasing.Controller.HasPath, Is.False);
        Assert.That(walking.Controller.HasPath, Is.True);
        Assert.That(chasing.Follow.IsFollowing || walking.Follow.IsFollowing, Is.False);
    }

    [Test]
    public void Despawn_OfTheMember_EndsTheFollow()
    {
        var rig = new Rig(6f);
        rig.Follow.Follow(Bobby);
        rig.Tick();

        rig.World.OnDespawn(new EntityDespawn(Bobby, DespawnReason.OutOfRange));

        Assert.That(rig.Follow.IsFollowing, Is.False);
        Assert.That(rig.Controller.IsChasing, Is.False);
    }

    [Test]
    public void Follow_OfADistantMember_WalksUntilWithinTheFollowDistance_AndKeepsFollowing()
    {
        var rig = new Rig(6f);

        Assert.That(rig.Follow.Follow(Bobby), Is.True);
        WorldDirection first = rig.Tick();
        rig.TickUntilStill();

        Assert.That(first, Is.Not.EqualTo(default(WorldDirection)));
        Assert.That(rig.DistanceToBobby(), Is.LessThanOrEqualTo(FollowState.FollowDistance + 1e-3f));
        Assert.That(rig.Controller.IsChasing, Is.False);
        Assert.That(rig.Follow.IsFollowing, Is.True, "arriving does not end it");
        Assert.That(rig.Follow.Name, Is.EqualTo("Bobby"));
    }

    [Test]
    public void Follow_OfAMemberInsideTheDistance_StandsStill()
    {
        var rig = new Rig(2f);

        rig.Follow.Follow(Bobby);
        WorldDirection direction = rig.Tick();

        Assert.That(direction, Is.EqualTo(default(WorldDirection)));
        Assert.That(rig.Follow.IsFollowing, Is.True);
    }

    [Test]
    public void Follow_OfAMemberWhoWalksOn_KeepsUp()
    {
        var rig = new Rig(2f);
        rig.Follow.Follow(Bobby);
        rig.Tick();

        rig.MoveBobby(new WorldPosition(Start.X + 8f, Start.Y, Start.Z), 1);
        rig.TickUntilStill();

        Assert.That(rig.DistanceToBobby(), Is.LessThanOrEqualTo(FollowState.FollowDistance + 1e-3f));
        Assert.That(rig.Follow.IsFollowing, Is.True);
    }

    [Test]
    public void Follow_OfAStranger_OrAMonster_IsRefused()
    {
        var stranger = new Rig(6f, false);
        var member = new Rig(6f);

        Assert.That(stranger.Follow.Follow(Bobby), Is.False);
        Assert.That(member.Follow.Follow(Slime), Is.False);
        Assert.That(member.Follow.Follow(new EntityId(999)), Is.False);
        Assert.That(stranger.Follow.IsFollowing || member.Follow.IsFollowing, Is.False);
    }

    [Test]
    public void LeavingTheParty_EndsTheFollow()
    {
        var rig = new Rig(6f);
        rig.Follow.Follow(Bobby);
        rig.Tick();

        rig.Party.Apply(Rig.Roster("Ann0"));
        rig.Tick();

        Assert.That(rig.Follow.IsFollowing, Is.False);
        Assert.That(rig.Controller.IsChasing, Is.False);
    }

    [Test]
    public void ManualMovement_EndsTheFollow()
    {
        var rig = new Rig(6f);
        rig.Follow.Follow(Bobby);
        rig.Tick();

        rig.Controller.SetManualDirection(0f, 1f);
        rig.Tick();

        Assert.That(rig.Follow.IsFollowing, Is.False);
        Assert.That(rig.Controller.IsChasing, Is.False);
    }
}
}
