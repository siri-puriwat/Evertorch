using System;
using System.Collections.Generic;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class InterestGridTests
{
    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void Constructor_WithCellSizeThatIsNotPositiveAndFinite_Throws(float cellSize)
    {
        Action create = () => _ = new InterestGrid(cellSize, 1);

        Assert.That(create, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [TestCase(15.9f, 0f, true)]
    [TestCase(31.9f, 0f, true)]
    [TestCase(32f, 0f, false)]
    [TestCase(-0.1f, 0f, true)]
    [TestCase(-16f, 0f, true)]
    [TestCase(-16.1f, 0f, false)]
    [TestCase(31.9f, 31.9f, true)]
    [TestCase(0f, 32f, false)]
    public void CollectVisible_WithRadiusOne_SeesExactlyTheSurroundingBlockOfCells(float x, float z, bool expected)
    {
        var grid = new InterestGrid(16f, 1);
        PlayerEntity observer = Player(1, 1f, 1f);
        PlayerEntity other = Player(2, x, z);
        grid.Update(observer);
        grid.Update(other);

        Assert.That(Visible(grid, observer).Contains(other), Is.EqualTo(expected));
    }

    private static List<WorldEntity> Visible(InterestGrid grid, PlayerEntity observer)
    {
        var visible = new List<WorldEntity>();
        grid.CollectVisible(observer, visible);
        return visible;
    }

    private static PlayerEntity Player(long id, float x, float z)
    {
        return new PlayerEntity(
            new EntityId(id),
            new CharacterId(id),
            $"Player{id}",
            new ConnectionId(id),
            new JobDefinitionId("job.adventurer"),
            new WorldPosition(x, 0f, z),
            new WorldDirection(0f, 1f),
            5f,
            1,
            new PrimaryStats(5, 5, 5, 5, 5, 5),
            TestStats.Adventurer,
            default,
            1.5f);
    }

    [Test]
    public void CollectVisible_NeverIncludesTheObserver()
    {
        var grid = new InterestGrid(16f, 1);
        PlayerEntity observer = Player(1, 1f, 1f);
        grid.Update(observer);

        Assert.That(Visible(grid, observer), Is.Empty);
    }

    [Test]
    public void CollectVisible_WithRadiusZero_SeesOnlyItsOwnCell()
    {
        var grid = new InterestGrid(16f, 0);
        PlayerEntity observer = Player(1, 1f, 1f);
        PlayerEntity sameCell = Player(2, 15f, 15f);
        PlayerEntity nextCell = Player(3, 17f, 1f);
        grid.Update(observer);
        grid.Update(sameCell);
        grid.Update(nextCell);

        Assert.That(Visible(grid, observer), Is.EquivalentTo(new[] { sameCell }));
    }

    [Test]
    public void Constructor_WithNegativeRadius_Throws()
    {
        Action create = () => _ = new InterestGrid(16f, -1);

        Assert.That(create, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Remove_TakesTheEntityOutAndToleratesRepeats()
    {
        var grid = new InterestGrid(16f, 1);
        PlayerEntity observer = Player(1, 1f, 1f);
        PlayerEntity other = Player(2, 2f, 2f);
        grid.Update(observer);
        grid.Update(other);

        grid.Remove(other);
        grid.Remove(other);

        Assert.That(Visible(grid, observer), Is.Empty);
    }

    [Test]
    public void Update_AfterMoving_ListsTheEntityOnlyInItsNewCell()
    {
        var grid = new InterestGrid(16f, 0);
        PlayerEntity stayer = Player(1, 1f, 1f);
        PlayerEntity distantStayer = Player(2, 100f, 100f);
        PlayerEntity mover = Player(3, 2f, 2f);
        grid.Update(stayer);
        grid.Update(distantStayer);
        grid.Update(mover);

        mover.Position = new WorldPosition(101f, 0f, 101f);
        grid.Update(mover);
        grid.Update(mover);

        Assert.That(Visible(grid, stayer), Is.Empty);
        Assert.That(Visible(grid, distantStayer), Is.EquivalentTo(new[] { mover }));
    }
}
}
