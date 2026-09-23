using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class TargetingTests
{
    private static readonly WorldPosition Origin = new(0f, 0f, 0f);

    private static PickCandidate Candidate(long entity, float x, float z)
    {
        return new PickCandidate(new EntityId(entity), new WorldPosition(x, 0f, z));
    }

    private static ClientWorld CreateWorldWithMonster(EntityId monster)
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), ClientTestGrids.Center(2, 8));
        world.OnSpawn(
            new EntitySpawn(
                monster,
                EntityKind.Monster,
                "monster.a",
                ClientTestGrids.Center(4, 8),
                new WorldDirection(0f, 1f),
                EntityStateFlags.None,
                1000));
        return world;
    }

    [TestCase(0.69f, true)]
    [TestCase(0.71f, false)]
    public void TryPick_ForARayPassingBesideAMonster_HitsOnlyWithinThePickRadius(float offset, bool expected)
    {
        var candidates = new List<PickCandidate> { Candidate(1, offset, 5f) };
        var eye = new WorldPosition(0f, EntityPicker.PickHeight, 0f);

        Assert.That(EntityPicker.TryPick(eye, 0f, 0f, 1f, candidates, out EntityId _), Is.EqualTo(expected));
    }

    [Test]
    public void Choose_ForEquallyNearCandidates_OrdersByEntityId()
    {
        var candidates = new List<PickCandidate> { Candidate(9, 0f, 2f), Candidate(4, 2f, 0f) };

        Assert.That(new TargetCycler().Choose(candidates, Origin, default, true), Is.EqualTo(new EntityId(4)));
    }

    [Test]
    public void Choose_ForwardFromTheCurrentTarget_TakesTheNextNearestAndWraps()
    {
        var candidates = new List<PickCandidate>
        {
            Candidate(3, 5f, 0f), Candidate(1, 1f, 0f), Candidate(2, 3f, 0f)
        };
        var cycler = new TargetCycler();

        EntityId first = cycler.Choose(candidates, Origin, default, true);
        EntityId second = cycler.Choose(candidates, Origin, first, true);
        EntityId third = cycler.Choose(candidates, Origin, second, true);
        EntityId wrapped = cycler.Choose(candidates, Origin, third, true);
        EntityId back = cycler.Choose(candidates, Origin, first, false);

        Assert.That(first, Is.EqualTo(new EntityId(1)));
        Assert.That(second, Is.EqualTo(new EntityId(2)));
        Assert.That(third, Is.EqualTo(new EntityId(3)));
        Assert.That(wrapped, Is.EqualTo(new EntityId(1)));
        Assert.That(back, Is.EqualTo(new EntityId(3)));
    }

    [Test]
    public void Choose_WithNoCandidates_ChoosesNothing()
    {
        var cycler = new TargetCycler();

        Assert.That(cycler.Choose(new List<PickCandidate>(), Origin, default, true), Is.EqualTo(default(EntityId)));
    }

    [Test]
    public void CollectTargetCandidates_ListsMonstersButNotPlayers()
    {
        var monster = new EntityId(300);
        ClientWorld world = CreateWorldWithMonster(monster);
        world.OnSpawn(
            new EntitySpawn(
                new EntityId(301),
                EntityKind.Player,
                "job.adventurer",
                ClientTestGrids.Center(5, 8),
                new WorldDirection(0f, 1f),
                EntityStateFlags.None,
                0));
        var candidates = new List<PickCandidate>();

        world.CollectTargetCandidates(candidates);

        Assert.That(candidates.Count, Is.EqualTo(1));
        Assert.That(candidates[0].Entity, Is.EqualTo(monster));
    }

    [Test]
    public void OnTargetChanged_ForAKnownMonster_SetsTheTargetAndZeroClearsIt()
    {
        var monster = new EntityId(300);
        ClientWorld world = CreateWorldWithMonster(monster);
        int changes = 0;
        world.TargetChanged += () => changes++;

        world.OnTargetChanged(new TargetChanged(ClientWorldFixture.LocalEntity, monster));
        EntityId selected = world.Target;
        world.OnTargetChanged(new TargetChanged(ClientWorldFixture.LocalEntity, default));

        Assert.That(selected, Is.EqualTo(monster));
        Assert.That(world.Target, Is.EqualTo(default(EntityId)));
        Assert.That(changes, Is.EqualTo(2));
    }

    [Test]
    public void OnTargetChanged_ForAnotherActorOrAnUnknownTarget_IsCountedAndIgnored()
    {
        var monster = new EntityId(300);
        ClientWorld world = CreateWorldWithMonster(monster);

        world.OnTargetChanged(new TargetChanged(new EntityId(999), monster));
        world.OnTargetChanged(new TargetChanged(ClientWorldFixture.LocalEntity, new EntityId(301)));

        Assert.That(world.Target, Is.EqualTo(default(EntityId)));
        Assert.That(world.UnknownEntityEvents, Is.EqualTo(2));
    }

    [Test]
    public void TryPick_AlongARayThroughTwoMonsters_TakesTheNearer()
    {
        var candidates = new List<PickCandidate> { Candidate(2, 0f, 10f), Candidate(1, 0f, 5f) };
        var eye = new WorldPosition(0f, EntityPicker.PickHeight, 0f);

        bool isPicked = EntityPicker.TryPick(eye, 0f, 0f, 1f, candidates, out EntityId picked);

        Assert.That(isPicked, Is.True);
        Assert.That(picked, Is.EqualTo(new EntityId(1)));
    }

    [Test]
    public void TryPick_ForAMonsterBehindTheRayOrAZeroDirection_PicksNothing()
    {
        var candidates = new List<PickCandidate> { Candidate(1, 0f, -5f) };
        var eye = new WorldPosition(0f, EntityPicker.PickHeight, 0f);

        Assert.That(EntityPicker.TryPick(eye, 0f, 0f, 1f, candidates, out EntityId _), Is.False);
        Assert.That(EntityPicker.TryPick(eye, 0f, 0f, 0f, candidates, out EntityId _), Is.False);
    }
}
}
