using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The walk up to an NPC (Gameplay Systems §6.1): to a place to stand beside it, the window asked for on arrival or
///     within 2.5 m, and nothing sent.
/// </summary>
[TestFixture]
public sealed class TalkStateTests
{
    private static readonly EntityId Warden = new(500);
    private static readonly EntityId Slime = new(300);

    // A wall cell beside the yard's sealed pocket: the NPC stands where nobody can walk, as on its marker.
    private static readonly WorldPosition NpcSpot = ClientTestGrids.Center(8, 3);

    private sealed class Rig
    {
        private uint m_moveSequence;

        public Rig(WorldPosition start)
        {
            World = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), start);
            World.OnSpawn(Spawn(Warden, EntityKind.Npc, "npc.gate_warden", NpcSpot));
            World.OnSpawn(Spawn(Slime, EntityKind.Monster, "monster.a", ClientTestGrids.Center(2, 8)));
            Controller = new MovementController(World.Grid);
            Talk = new TalkState(World, Controller);
            Talk.Arrived += Opened.Add;
        }

        public ClientWorld World { get; }

        public MovementController Controller { get; }

        public TalkState Talk { get; }

        public List<EntityId> Opened { get; } = new();

        public float DistanceToNpc
        {
            get
            {
                float dx = World.Predictor.Position.X - NpcSpot.X;
                float dz = World.Predictor.Position.Z - NpcSpot.Z;
                return (float)Math.Sqrt(dx * dx + dz * dz);
            }
        }

        public void Tick()
        {
            Talk.Tick(World.Predictor.Position);
            WorldDirection direction = Controller.Tick(World.Predictor.Position, World.Predictor.StepDistance);
            m_moveSequence++;
            World.Predictor.Apply(new MoveIntent(m_moveSequence, m_moveSequence, direction.X, direction.Z));
        }

        public void TickUntilIdle(int limit)
        {
            for (int tick = 0; tick < limit && Talk.IsActive; tick++)
            {
                Tick();
            }
        }

        private static EntitySpawn Spawn(EntityId entity, EntityKind kind, string definition, WorldPosition at)
        {
            return new EntitySpawn(
                entity,
                kind,
                definition,
                at,
                new WorldDirection(0f, 1f),
                EntityStateFlags.None,
                kind == EntityKind.Monster ? (ushort)1000 : (ushort)0);
        }
    }

    [Test]
    public void Cancel_AfterAWalkOfThePlayersOwn_LeavesThatWalk_ButEndsItsOwn()
    {
        var clicked = new Rig(ClientTestGrids.Center(1, 1));
        clicked.Talk.Talk(Warden);
        clicked.Tick();
        WorldPosition elsewhere = ClientTestGrids.Center(1, 6);
        Assert.That(clicked.Controller.TryMoveTo(clicked.World.Predictor.Position, elsewhere), Is.True);
        var cancelled = new Rig(ClientTestGrids.Center(1, 1));
        cancelled.Talk.Talk(Warden);
        cancelled.Tick();

        clicked.Talk.Cancel();
        cancelled.Talk.Cancel();

        Assert.That(clicked.Controller.HasPath, Is.True, "a ground click installs its walk before the talk ends");
        WorldPosition goal = clicked.Controller.Path[clicked.Controller.Path.Count - 1];
        Assert.That((goal.X, goal.Z), Is.EqualTo((elsewhere.X, elsewhere.Z)));
        Assert.That(cancelled.Controller.HasPath, Is.False, "the walk up itself ends with the talk");
    }

    [Test]
    public void Death_EndsTheWalkUp()
    {
        var rig = new Rig(ClientTestGrids.Center(1, 1));
        rig.Talk.Talk(Warden);
        rig.Tick();

        rig.World.OnEntityDied(new EntityDied(ClientWorldFixture.LocalEntity, Slime, 2));
        rig.Tick();

        Assert.That(rig.Talk.IsActive, Is.False);
        Assert.That(rig.Opened, Is.Empty);
    }

    [Test]
    public void Despawn_OfTheNpc_EndsTheWalkUp()
    {
        var rig = new Rig(ClientTestGrids.Center(1, 1));
        rig.Talk.Talk(Warden);
        rig.Tick();

        rig.World.OnDespawn(new EntityDespawn(Warden, DespawnReason.OutOfRange));

        Assert.That(rig.Talk.IsActive, Is.False);
        Assert.That(rig.Controller.HasPath, Is.False, "the walk to it stops too");
        Assert.That(rig.Opened, Is.Empty);
    }

    [Test]
    public void ManualDirection_EndsTheWalkUp()
    {
        var rig = new Rig(ClientTestGrids.Center(1, 1));
        rig.Talk.Talk(Warden);
        rig.Tick();

        rig.Controller.SetManualDirection(1f, 0f);
        rig.Tick();

        Assert.That(rig.Talk.IsActive, Is.False);
        Assert.That(rig.Opened, Is.Empty);
    }

    [Test]
    public void Talk_FromAfar_WalksUpAndAsksForTheWindowWithinTwoAndAHalfMetres()
    {
        var rig = new Rig(ClientTestGrids.Center(1, 1));

        rig.Talk.Talk(Warden);
        rig.TickUntilIdle(400);

        Assert.That(rig.Opened, Is.EqualTo(new[] { Warden }));
        Assert.That(rig.DistanceToNpc, Is.LessThanOrEqualTo(TalkState.OpenDistance));
        Assert.That(rig.DistanceToNpc, Is.GreaterThan(TalkState.OpenDistance - 0.5f), "it stopped once near enough");
        Assert.That(rig.Controller.HasPath, Is.False);
        Assert.That(rig.Talk.IsActive, Is.False);
    }

    [Test]
    public void Talk_NearTheNpc_AsksForTheWindowWithoutWalking()
    {
        var rig = new Rig(ClientTestGrids.Center(7, 3));

        rig.Talk.Talk(Warden);
        rig.Tick();

        Assert.That(rig.Opened, Is.EqualTo(new[] { Warden }));
        Assert.That(rig.Controller.RejectedMoveRequests + rig.Controller.CancelledPaths, Is.Zero);
    }

    [Test]
    public void Talk_ToAMonsterOrAnUnknownEntity_DoesNothing()
    {
        var rig = new Rig(ClientTestGrids.Center(1, 1));

        rig.Talk.Talk(Slime);
        bool isTalkingToMonster = rig.Talk.IsActive;
        rig.Talk.Talk(new EntityId(999));

        Assert.That(isTalkingToMonster, Is.False);
        Assert.That(rig.Talk.IsActive, Is.False);
    }

    // The two places nearest the NPC are one metre from it: the sealed pocket, which is nearer this player, and the
    // cell west of it. The pocket has no path, so the walk heads west of the NPC.
    [Test]
    public void Talk_ToAnNpcWhoseNearestPlaceCannotBeReached_WalksToTheNextOne()
    {
        var rig = new Rig(ClientTestGrids.Center(10, 1));

        rig.Talk.Talk(Warden);
        rig.Tick();

        Assert.That(rig.Controller.HasPath, Is.True);
        Assert.That(rig.Controller.Path[rig.Controller.Path.Count - 1], Is.EqualTo(ClientTestGrids.Center(7, 3)));
        Assert.That(rig.Controller.RejectedMoveRequests, Is.EqualTo(1), "the pocket was tried first");
    }

    [Test]
    public void World_KeepsAnNpcsServicesUntilItDespawns_AndRefusesThemForAnotherKind()
    {
        var rig = new Rig(ClientTestGrids.Center(1, 1));
        var services = new NpcServices(Warden, new NpcServiceEntry[0], new NpcQuestOffer[0]);
        var received = new List<NpcServices>();
        rig.World.NpcServicesReceived += received.Add;

        rig.World.OnNpcServices(services);
        bool isKept = rig.World.TryGetNpcServices(Warden, out NpcServices? kept);
        rig.World.OnNpcServices(new NpcServices(Slime, new NpcServiceEntry[0], new NpcQuestOffer[0]));
        rig.World.OnDespawn(new EntityDespawn(Warden, DespawnReason.OutOfRange));

        Assert.That(isKept, Is.True);
        Assert.That(kept, Is.SameAs(services));
        Assert.That(received, Is.EqualTo(new[] { services }));
        Assert.That(rig.World.UnknownEntityEvents, Is.EqualTo(1), "a monster has no services");
        Assert.That(rig.World.TryGetNpcServices(Warden, out NpcServices? _), Is.False);
    }

    [Test]
    public void World_OffersNpcsToAClickAndTheTalkKey_ButNeverToTargeting()
    {
        var rig = new Rig(ClientTestGrids.Center(1, 1));
        var targets = new List<PickCandidate>();
        var npcs = new List<PickCandidate>();

        rig.World.CollectTargetCandidates(targets);
        rig.World.CollectNpcCandidates(npcs);

        Assert.That(targets.ConvertAll(candidate => candidate.Entity), Is.EqualTo(new[] { Slime }));
        Assert.That(npcs.ConvertAll(candidate => candidate.Entity), Is.EqualTo(new[] { Warden }));
        Assert.That(
            rig.World.NearestNpc(ClientTestGrids.Center(2, 7)),
            Is.EqualTo(Warden),
            "the slime a metre away is no NPC");
    }
}
}
