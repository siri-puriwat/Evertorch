using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class ClientWorldTests
{
    private static readonly WorldPosition Start = ClientTestGrids.Center(2, 8);
    private static readonly EntityId Other = new(200);

    private static EntitySpawn Spawn(EntityId entity, WorldPosition position)
    {
        return new EntitySpawn(
            entity,
            EntityKind.Player,
            "job.adventurer",
            position,
            new WorldDirection(0f, 1f),
            EntityStateFlags.None,
            0);
    }

    [Test]
    public void Constructor_TakesTheLocalPlayerFromWorldEntered()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), Start, 500);

        Assert.That(world.LocalEntity, Is.EqualTo(ClientWorldFixture.LocalEntity));
        Assert.That(world.LocalJob, Is.EqualTo(ClientWorldFixture.LocalJob));
        Assert.That(world.Map.Value, Is.EqualTo("map.training_ground"));
        Assert.That(world.Predictor.Position, Is.EqualTo(Start));
        Assert.That(world.Predictor.StepDistance, Is.EqualTo(0.25f).Within(1e-6f));
        Assert.That(world.LatestServerTick, Is.EqualTo(500u));
        Assert.That(world.ServerTime.Now, Is.EqualTo(25.0).Within(1e-9));
    }

    [Test]
    public void Constructor_WithNoHealthLeft_EntersTheLocalPlayerDead()
    {
        var world = new ClientWorld(
            ClientTestGrids.CreateYard(),
            ClientWorldFixture.Entered(Start, 500, 0),
            ClientWorldFixture.TickRate);
        var alive = new ClientWorld(
            ClientTestGrids.CreateYard(),
            ClientWorldFixture.Entered(Start, 500),
            ClientWorldFixture.TickRate);

        Assert.That(world.IsLocalDead, Is.True);
        Assert.That(alive.IsLocalDead, Is.False);
    }

    // The sheet names the job, so the owner learns of a change from it (Network Protocol §9); the same job again changes
    // nothing.
    [Test]
    public void OnCharacterSheet_NamingAnotherJob_ChangesTheLocalJobOnce()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), Start);
        int changes = 0;
        world.LocalJobChanged += () => changes++;
        CharacterSheetStat[] stats =
        {
            new(5, 2), new(5, 2), new(5, 2), new(5, 2), new(5, 2), new(5, 2)
        };

        world.OnCharacterSheet(new CharacterSheet(10, ClientWorldFixture.LocalJob, 0, 0, 0, 5, stats, 1, 1, 1, 1, 1, 1,
            1, 1));
        int afterSameJob = changes;
        world.OnCharacterSheet(new CharacterSheet(1, new JobDefinitionId("job.vanguard"), 0, 250, 0, 5, stats, 1, 1, 1,
            1, 1, 1, 1, 1));
        world.OnCharacterSheet(new CharacterSheet(1, new JobDefinitionId("job.vanguard"), 5, 250, 0, 5, stats, 1, 1, 1,
            1, 1, 1, 1, 1));

        Assert.That(afterSameJob, Is.Zero);
        Assert.That(world.LocalJob.Value, Is.EqualTo("job.vanguard"));
        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void OnDespawn_RemovesTheEntityAndAnnouncesIt()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), Start);
        var despawned = new List<EntityId>();
        world.RemoteDespawned += remote => despawned.Add(remote.Entity);
        world.OnSpawn(Spawn(Other, ClientTestGrids.Center(3, 8)));

        world.OnDespawn(new EntityDespawn(Other, DespawnReason.OutOfRange));
        world.OnDespawn(new EntityDespawn(Other, DespawnReason.OutOfRange));

        Assert.That(world.Remotes.Count, Is.EqualTo(0));
        Assert.That(despawned, Is.EqualTo(new[] { Other }));
    }

    [Test]
    public void OnSnapshot_AcrossTickWrapAround_TreatsTheWrappedTickAsNewer()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), Start, uint.MaxValue - 1);

        world.OnSnapshot(
            ClientWorldFixture.Snapshot(2, 0, ClientWorldFixture.State(ClientWorldFixture.LocalEntity, Start)));

        Assert.That(world.StaleSnapshots, Is.EqualTo(0));
        Assert.That(world.LatestServerTick, Is.EqualTo(2u));
    }

    [Test]
    public void OnSnapshot_FeedsKnownRemotesAndIgnoresUnknownOnes()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), Start);
        world.OnSpawn(Spawn(Other, ClientTestGrids.Center(3, 8)));

        world.OnSnapshot(
            ClientWorldFixture.Snapshot(
                3,
                0,
                ClientWorldFixture.State(ClientWorldFixture.LocalEntity, Start),
                ClientWorldFixture.State(Other, ClientTestGrids.Center(4, 8)),
                ClientWorldFixture.State(new EntityId(999), ClientTestGrids.Center(5, 8))));

        Assert.That(world.Remotes[Other].Buffer.Count, Is.EqualTo(2));
        Assert.That(world.Remotes.Count, Is.EqualTo(1), "a state is never a spawn");
        Assert.That(world.UnknownEntityStates, Is.EqualTo(1));
        Assert.That(world.SnapshotsApplied, Is.EqualTo(1));
    }

    [Test]
    public void OnSnapshot_OlderThanTheNewestSeen_IsIgnoredEntirely()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), Start);
        WorldPosition newer = ClientTestGrids.Center(3, 8);
        world.OnSnapshot(
            ClientWorldFixture.Snapshot(10, 0, ClientWorldFixture.State(ClientWorldFixture.LocalEntity, newer)));

        world.OnSnapshot(
            ClientWorldFixture.Snapshot(9, 0, ClientWorldFixture.State(ClientWorldFixture.LocalEntity, Start)));

        Assert.That(world.Predictor.Position, Is.EqualTo(newer));
        Assert.That(world.StaleSnapshots, Is.EqualTo(1));
        Assert.That(world.LatestServerTick, Is.EqualTo(10u));
    }

    [Test]
    public void OnSnapshot_ThatMovesTheLocalPlayerFar_SnapsTheDrawnPosition()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), Start);
        WorldPosition far = ClientTestGrids.Center(9, 8);

        world.OnSnapshot(
            ClientWorldFixture.Snapshot(1, 0, ClientWorldFixture.State(ClientWorldFixture.LocalEntity, far)));

        Assert.That(world.Smoother.Snaps, Is.EqualTo(1));
        Assert.That(world.Smoother.Sample(1f), Is.EqualTo(far));
    }

    [Test]
    public void OnSnapshot_ThatNudgesTheLocalPlayer_IsSmoothed()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), Start);
        var near = new WorldPosition(Start.X + 0.3f, 0f, Start.Z);

        world.OnSnapshot(
            ClientWorldFixture.Snapshot(1, 0, ClientWorldFixture.State(ClientWorldFixture.LocalEntity, near)));

        Assert.That(world.Smoother.Snaps, Is.EqualTo(0));
        Assert.That(world.Predictor.Position, Is.EqualTo(near));
        Assert.That(world.Smoother.Sample(1f).X, Is.EqualTo(Start.X).Within(1e-5f));
    }

    [Test]
    public void OnSnapshot_WithTheSameTick_IsStillApplied()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), Start);
        world.OnSpawn(Spawn(Other, ClientTestGrids.Center(3, 8)));
        world.OnSnapshot(
            ClientWorldFixture.Snapshot(10, 0, ClientWorldFixture.State(ClientWorldFixture.LocalEntity, Start)));

        world.OnSnapshot(
            ClientWorldFixture.Snapshot(10, 0, ClientWorldFixture.State(Other, ClientTestGrids.Center(4, 8))));

        Assert.That(world.StaleSnapshots, Is.EqualTo(0));
        Assert.That(world.Remotes[Other].Buffer.Count, Is.EqualTo(2), "the second part of a split snapshot");
    }

    [Test]
    public void OnSpawn_AddsARemoteEntityAndAnnouncesIt()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), Start);
        var spawned = new List<RemoteEntity>();
        world.RemoteSpawned += spawned.Add;

        world.OnSpawn(Spawn(Other, ClientTestGrids.Center(3, 8)));

        Assert.That(spawned.Count, Is.EqualTo(1));
        Assert.That(world.Remotes[Other].DefinitionId, Is.EqualTo("job.adventurer"));
        Assert.That(world.Remotes[Other].Kind, Is.EqualTo(EntityKind.Player));
        Assert.That(world.Remotes[Other].Buffer.Count, Is.EqualTo(1), "drawable before the first snapshot");
    }

    [Test]
    public void OnSpawn_ForTheLocalEntity_IsIgnored()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), Start);

        world.OnSpawn(Spawn(ClientWorldFixture.LocalEntity, Start));

        Assert.That(world.Remotes.Count, Is.EqualTo(0));
    }
}
}
