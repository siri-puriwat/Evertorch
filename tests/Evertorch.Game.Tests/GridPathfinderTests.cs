using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class GridPathfinderTests
{
    private const int GenerousBudget = 10000;

    // A wall with one gap splits the map; the plateau in the north-east is reached only by its ramp.
    private static readonly string[] Yard =
    {
        "##########",
        "#......^^#",
        "#......^^#",
        "#......>^#",
        "#####.####",
        "#........#",
        "#...oo...#",
        "#........#",
        "##########",
    };

    [Test]
    public void TryFindPath_AcrossOpenFloor_ReturnsOnlyTheGoal()
    {
        NavigationGrid grid = TestGrids.FromRows(Yard);
        List<WorldPosition> waypoints = new List<WorldPosition>();
        WorldPosition goal = new WorldPosition(8.25f, 5f, 1.75f);

        bool found = new GridPathfinder(grid).TryFindPath(
            TestGrids.Center(grid, 1, 1),
            goal,
            GenerousBudget,
            waypoints);

        Assert.That(found, Is.True);
        Assert.That(waypoints, Is.EqualTo(new[] { new WorldPosition(8.25f, 0f, 1.75f) }));
    }

    [Test]
    public void TryFindPath_AroundWall_ReturnsWaypointsThatNeverCrossBlockedCells()
    {
        NavigationGrid grid = TestGrids.FromRows(Yard);
        List<WorldPosition> waypoints = new List<WorldPosition>();
        WorldPosition start = TestGrids.Center(grid, 1, 1);

        bool found = new GridPathfinder(grid).TryFindPath(
            start,
            TestGrids.Center(grid, 1, 7),
            GenerousBudget,
            waypoints);

        Assert.That(found, Is.True);
        Assert.That(waypoints.Count, Is.GreaterThan(1));
        Assert.That(waypoints[waypoints.Count - 1], Is.EqualTo(TestGrids.Center(grid, 1, 7)));
        AssertWalkable(grid, start, waypoints);
    }

    [Test]
    public void TryFindPath_OntoPlateau_GoesUpTheRamp()
    {
        NavigationGrid grid = TestGrids.FromRows(Yard);
        List<WorldPosition> waypoints = new List<WorldPosition>();
        WorldPosition start = TestGrids.Center(grid, 1, 1);
        WorldPosition goal = TestGrids.Center(grid, 8, 7);

        bool found = new GridPathfinder(grid).TryFindPath(start, goal, GenerousBudget, waypoints);

        Assert.That(found, Is.True);
        Assert.That(waypoints[waypoints.Count - 1].Y, Is.EqualTo(1f));
        AssertWalkable(grid, start, waypoints);
        Assert.That(CrossesCell(grid, start, waypoints, 7, 5), Is.True, "the route must use the ramp cell");
    }

    [Test]
    public void TryFindPath_OntoPlateauWithoutRamp_ReturnsFalse()
    {
        string[] rows = (string[])Yard.Clone();
        rows[3] = "#......^^#";
        NavigationGrid grid = TestGrids.FromRows(rows);
        List<WorldPosition> waypoints = new List<WorldPosition> { default };

        bool found = new GridPathfinder(grid).TryFindPath(
            TestGrids.Center(grid, 1, 1),
            TestGrids.Center(grid, 8, 7),
            GenerousBudget,
            waypoints);

        Assert.That(found, Is.False);
        Assert.That(waypoints, Is.Empty);
    }

    [Test]
    public void TryFindPath_ToEnclosedGoal_ReturnsFalse()
    {
        NavigationGrid grid = TestGrids.FromRows("#####", "#.#.#", "#####");
        List<WorldPosition> waypoints = new List<WorldPosition>();

        bool found = new GridPathfinder(grid).TryFindPath(
            TestGrids.Center(grid, 1, 1),
            TestGrids.Center(grid, 3, 1),
            GenerousBudget,
            waypoints);

        Assert.That(found, Is.False);
        Assert.That(waypoints, Is.Empty);
    }

    [TestCase(4.5f, 2.5f)]
    [TestCase(0.5f, 0.5f)]
    [TestCase(-3f, 2f)]
    [TestCase(1.1f, 1.5f)]
    [TestCase(float.NaN, 1.5f)]
    public void TryFindPath_ToPointThatCannotBeStoodOn_ReturnsFalse(float goalX, float goalZ)
    {
        NavigationGrid grid = TestGrids.FromRows(Yard);
        List<WorldPosition> waypoints = new List<WorldPosition>();

        bool found = new GridPathfinder(grid).TryFindPath(
            TestGrids.Center(grid, 1, 1),
            new WorldPosition(goalX, 0f, goalZ),
            GenerousBudget,
            waypoints);

        Assert.That(found, Is.False);
    }

    [Test]
    public void TryFindPath_FromPointThatCannotBeStoodOn_ReturnsFalse()
    {
        NavigationGrid grid = TestGrids.FromRows(Yard);
        List<WorldPosition> waypoints = new List<WorldPosition>();

        bool found = new GridPathfinder(grid).TryFindPath(
            new WorldPosition(4.5f, 0f, 2.5f),
            TestGrids.Center(grid, 1, 1),
            GenerousBudget,
            waypoints);

        Assert.That(found, Is.False);
    }

    [Test]
    public void TryFindPath_WhenNodeBudgetExceeded_ReturnsFalseAndStillWorksAfterwards()
    {
        NavigationGrid grid = TestGrids.FromRows(Yard);
        GridPathfinder pathfinder = new GridPathfinder(grid);
        List<WorldPosition> waypoints = new List<WorldPosition>();
        WorldPosition start = TestGrids.Center(grid, 1, 1);
        WorldPosition goal = TestGrids.Center(grid, 1, 7);

        bool foundWithinTinyBudget = pathfinder.TryFindPath(start, goal, 3, waypoints);
        bool foundAfterwards = pathfinder.TryFindPath(start, goal, GenerousBudget, waypoints);

        Assert.That(foundWithinTinyBudget, Is.False);
        Assert.That(foundAfterwards, Is.True);
        AssertWalkable(grid, start, waypoints);
    }

    [Test]
    public void TryFindPath_PastDiagonalObstacleCorner_DoesNotCutTheCorner()
    {
        NavigationGrid grid = TestGrids.FromRows("...", ".o.", "...");
        List<WorldPosition> waypoints = new List<WorldPosition>();
        WorldPosition start = TestGrids.Center(grid, 0, 0);

        bool found =
            new GridPathfinder(grid).TryFindPath(start, TestGrids.Center(grid, 2, 2), GenerousBudget, waypoints);

        Assert.That(found, Is.True);
        AssertWalkable(grid, start, waypoints);
    }

    [Test]
    public void TryFindPath_ForSameRequest_IsDeterministicAcrossInstancesAndReuse()
    {
        NavigationGrid grid = TestGrids.FromRows(Yard);
        GridPathfinder reused = new GridPathfinder(grid);
        WorldPosition start = TestGrids.Center(grid, 1, 1);
        WorldPosition goal = TestGrids.Center(grid, 8, 7);
        List<WorldPosition> first = new List<WorldPosition>();
        List<WorldPosition> second = new List<WorldPosition>();
        List<WorldPosition> fresh = new List<WorldPosition>();

        reused.TryFindPath(start, goal, GenerousBudget, first);
        reused.TryFindPath(goal, start, GenerousBudget, second);
        reused.TryFindPath(start, goal, GenerousBudget, second);
        new GridPathfinder(grid).TryFindPath(start, goal, GenerousBudget, fresh);

        Assert.That(second, Is.EqualTo(first));
        Assert.That(fresh, Is.EqualTo(first));
    }

    // The same request and expected waypoints are asserted from Unity, so the two runtimes are held to one answer.
    [Test]
    public void TryFindPath_ForReferenceRequest_MatchesGoldenWaypoints()
    {
        NavigationGrid grid = TestGrids.FromRows(Yard);
        List<WorldPosition> waypoints = new List<WorldPosition>();

        new GridPathfinder(grid).TryFindPath(
            TestGrids.Center(grid, 1, 1),
            TestGrids.Center(grid, 8, 7),
            GenerousBudget,
            waypoints);

        WorldPosition[] expected =
        {
            new WorldPosition(3.5f, 0f, 3.5f),
            new WorldPosition(5.5f, 0f, 3.5f),
            new WorldPosition(5.5f, 0f, 5.5f),
            new WorldPosition(7.5f, 0.5f, 5.5f),
            new WorldPosition(8.5f, 1f, 7.5f),
        };
        Assert.That(waypoints, Is.EqualTo(expected), string.Join(" ", waypoints));
    }

    [Test]
    public void TryFindPath_WithNullWaypointList_Throws()
    {
        NavigationGrid grid = TestGrids.FromRows(Yard);
        Action find = () => new GridPathfinder(grid).TryFindPath(default, default, GenerousBudget, null!);

        Assert.That(find, Throws.ArgumentNullException);
    }

    private static void AssertWalkable(NavigationGrid grid, WorldPosition start, List<WorldPosition> waypoints)
    {
        WorldPosition from = start;
        foreach (WorldPosition waypoint in waypoints)
        {
            Assert.That(grid.HasLineOfSight(from, waypoint), Is.True, from + " -> " + waypoint);
            from = waypoint;
        }
    }

    private static bool CrossesCell(
        NavigationGrid grid,
        WorldPosition start,
        List<WorldPosition> waypoints,
        int column,
        int row)
    {
        WorldPosition from = start;
        foreach (WorldPosition waypoint in waypoints)
        {
            for (int step = 0; step <= 100; step++)
            {
                float x = from.X + ((waypoint.X - from.X) * step / 100f);
                float z = from.Z + ((waypoint.Z - from.Z) * step / 100f);
                if (grid.TryGetCellIndex(x, z, out int atColumn, out int atRow) && atColumn == column && atRow == row)
                {
                    return true;
                }
            }

            from = waypoint;
        }

        return false;
    }
}
}
