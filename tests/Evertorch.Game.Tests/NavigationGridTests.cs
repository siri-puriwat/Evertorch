using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class NavigationGridTests
{
    // North is up. Column 0 is west, row 0 is south, cells are 1 m.
    private static readonly string[] Room =
    {
        "#####",
        "#..^#",
        "#..>#",
        "#o..#",
        "#####"
    };

    [TestCase(0, 1, 1f, 0.3f, 0.3f)]
    [TestCase(1, 0, 1f, 0.3f, 0.3f)]
    [TestCase(513, 1, 1f, 0.3f, 0.3f)]
    [TestCase(1, 1, 0f, 0.3f, 0.3f)]
    [TestCase(1, 1, float.NaN, 0.3f, 0.3f)]
    [TestCase(1, 1, 1f, 0f, 0.3f)]
    [TestCase(1, 1, 1f, float.PositiveInfinity, 0.3f)]
    [TestCase(1, 1, 1f, 0.51f, 0.3f)]
    [TestCase(1, 1, 0.5f, 0.3f, 0.3f)]
    [TestCase(1, 1, 1f, 0.3f, -0.1f)]
    [TestCase(1, 1, 1f, 0.3f, float.NaN)]
    public void Constructor_WithInvalidShape_Throws(
        int columns,
        int rows,
        float cellSize,
        float agentRadius,
        float maxStepHeight)
    {
        var cells = new NavigationCell[Math.Max(0, columns * rows)];
        Action create = () =>
            _ = new NavigationGrid(columns, rows, cellSize, 0f, 0f, agentRadius, maxStepHeight, cells);

        Assert.That(create, Throws.ArgumentException);
    }

    // A marker cell in a small yard, an obstacle south of it.
    private static readonly string[] Yard =
    {
        "#####",
        "#...#",
        "#.N.#",
        "#.o.#",
        "#####"
    };

    [TestCase(1.5f, 2.5f, true)]
    [TestCase(1.5f, 1.5f, false)]
    [TestCase(0.5f, 0.5f, false)]
    [TestCase(-0.1f, 2.5f, false)]
    [TestCase(2.5f, 5.0f, false)]
    [TestCase(1e30f, 2.5f, false)]
    [TestCase(float.NaN, 2.5f, false)]
    [TestCase(2.5f, float.NegativeInfinity, false)]
    public void IsWalkable_ForPoint_ReflectsCellAndBounds(float x, float z, bool expected)
    {
        NavigationGrid grid = TestGrids.FromRows(Room);

        Assert.That(grid.IsWalkable(x, z), Is.EqualTo(expected));
    }

    [TestCase(3.0f, 0f)]
    [TestCase(3.25f, 0.25f)]
    [TestCase(3.5f, 0.5f)]
    [TestCase(3.75f, 0.75f)]
    public void TrySampleHeight_OnRampAlongX_InterpolatesAlongAxis(float x, float expectedHeight)
    {
        NavigationGrid grid = TestGrids.FromRows(Room);

        bool found = grid.TrySampleHeight(x, 2.2f, out float height);

        Assert.That(found, Is.True);
        Assert.That(height, Is.EqualTo(expectedHeight).Within(1e-5f));
    }

    [TestCase(2.5f, 1.5f, true)]
    [TestCase(2.29f, 1.5f, false)]
    [TestCase(2.31f, 1.5f, true)]
    [TestCase(3.71f, 1.5f, false)]
    [TestCase(3.69f, 1.5f, true)]
    [TestCase(2.25f, 2.25f, true)]
    [TestCase(2.2f, 2.2f, false)]
    public void CanOccupy_NearBlockedCells_RespectsBodyRadius(float x, float z, bool expected)
    {
        NavigationGrid grid = TestGrids.FromRows(Room);

        Assert.That(grid.CanOccupy(x, z), Is.EqualTo(expected));
    }

    [TestCase(float.NaN, 2.5f)]
    [TestCase(2.5f, float.PositiveInfinity)]
    public void CanOccupy_ForNonFinitePoint_ReturnsFalse(float x, float z)
    {
        NavigationGrid grid = TestGrids.FromRows(Room);

        Assert.That(grid.CanOccupy(x, z), Is.False);
    }

    [TestCase(2, 1, 3, 1, true)]
    [TestCase(2, 1, 1, 1, false)]
    [TestCase(2, 1, 2, 0, false)]
    [TestCase(2, 2, 3, 2, true)]
    [TestCase(3, 2, 3, 3, false)]
    [TestCase(2, 3, 3, 3, false)]
    [TestCase(2, 1, 2, 1, false)]
    [TestCase(1, 2, 3, 2, false)]
    [TestCase(2, 1, 1, 2, false)]
    [TestCase(2, 1, 3, 2, true)]
    [TestCase(1, 2, 2, 3, true)]
    public void CanTraverse_BetweenCells_FollowsWallsEdgesAndCorners(
        int fromColumn,
        int fromRow,
        int toColumn,
        int toRow,
        bool expected)
    {
        NavigationGrid grid = TestGrids.FromRows(Room);

        Assert.That(grid.CanTraverse(fromColumn, fromRow, toColumn, toRow), Is.EqualTo(expected));
    }

    [TestCase(2.5f, 2.5f, 0.9f)]
    [TestCase(float.NaN, 2.5f, 3f)]
    [TestCase(40f, 40f, 3f)]
    public void CollectStandingPlaces_WithNoCentreInReach_ListsNothing(float x, float z, float radius)
    {
        NavigationGrid grid = TestGrids.FromRows(Yard);
        var places = new List<WorldPosition>();

        grid.CollectStandingPlaces(new WorldPosition(x, 0f, z), radius, places);

        Assert.That(places, Is.Empty);
    }

    [Test]
    public void CanOccupy_AtGridEdgeWithoutWall_TreatsOutsideAsBlocked()
    {
        NavigationGrid grid = TestGrids.FromRows("..", "..");

        Assert.That(grid.CanOccupy(1f, 1f), Is.True);
        Assert.That(grid.CanOccupy(0.2f, 1f), Is.False);
        Assert.That(grid.CanOccupy(1f, 1.9f), Is.False);
    }

    [Test]
    public void CanOccupy_BesidePlateauEdge_IsBlockedFromBelowAndAbove()
    {
        NavigationGrid grid = TestGrids.FromRows(Room);

        Assert.That(grid.CanOccupy(2.5f, 3.5f), Is.True);
        Assert.That(grid.CanOccupy(2.8f, 3.5f), Is.False);
        Assert.That(grid.CanOccupy(3.2f, 3.5f), Is.False);
        Assert.That(grid.CanOccupy(3.5f, 3.5f), Is.True);
    }

    [Test]
    public void CanOccupy_HighOnRampBesideLowerFloor_IsBlocked()
    {
        NavigationGrid grid = TestGrids.FromRows(Room);

        Assert.That(grid.CanOccupy(3.6f, 2.5f), Is.True);
        Assert.That(grid.CanOccupy(3.6f, 2.2f), Is.False);
    }

    [Test]
    public void CanOccupy_OnRamp_IsAllowedAlongItsWholeLength()
    {
        NavigationGrid grid = TestGrids.FromRows(Room);

        for (float x = 2.5f; x <= 3.5f; x += 0.05f)
        {
            Assert.That(grid.CanOccupy(x, 2.5f), Is.True, $"x = {x}");
        }
    }

    [Test]
    public void CanTraverse_FromRampTopOntoPlateau_ReturnsTrue()
    {
        NavigationGrid grid = TestGrids.FromRows("#####", ".>^^.", "#####");

        Assert.That(grid.CanTraverse(1, 1, 2, 1), Is.True);
        Assert.That(grid.CanTraverse(2, 1, 1, 1), Is.True);
    }

    [Test]
    public void CollectStandingPlaces_AroundAMarker_ListsTheStandableCentresNearestFirstThenByRow()
    {
        NavigationGrid grid = TestGrids.FromRows(Yard);
        var places = new List<WorldPosition> { new(9f, 0f, 9f) };

        grid.CollectStandingPlaces(new WorldPosition(2.5f, 0f, 2.5f), 1.5f, places);

        Assert.That(
            places,
            Is.EqualTo(
                new[]
                {
                    new WorldPosition(1.5f, 0f, 2.5f), new WorldPosition(3.5f, 0f, 2.5f),
                    new WorldPosition(2.5f, 0f, 3.5f), new WorldPosition(1.5f, 0f, 1.5f),
                    new WorldPosition(3.5f, 0f, 1.5f), new WorldPosition(1.5f, 0f, 3.5f),
                    new WorldPosition(3.5f, 0f, 3.5f)
                }),
            "the obstacle and the marker itself are no place to stand; the list started empty");
    }

    [Test]
    public void Constructor_WithAgentRadiusOfExactlyHalfACell_IsAccepted()
    {
        NavigationCell[] cells = { NavigationCell.Level(NavigationSurface.Floor, 0f) };

        var grid = new NavigationGrid(1, 1, 1f, 0f, 0f, 0.5f, 0.3f, cells);

        Assert.That(grid.AgentRadius, Is.EqualTo(0.5f));
    }

    [Test]
    public void Constructor_WithCells_CopiesThem()
    {
        NavigationCell[] cells = { NavigationCell.Level(NavigationSurface.Floor, 0f) };
        var grid = new NavigationGrid(1, 1, 1f, 0f, 0f, 0.3f, 0.3f, cells);

        cells[0] = NavigationCell.Level(NavigationSurface.Wall, 0f);

        Assert.That(grid.GetCell(0, 0).IsWalkable, Is.True);
    }

    [Test]
    public void Constructor_WithMismatchedCellCount_Throws()
    {
        var cells = new NavigationCell[5];
        Action create = () => _ = new NavigationGrid(2, 3, 1f, 0f, 0f, 0.3f, 0.3f, cells);

        Assert.That(create, Throws.ArgumentException);
    }

    [Test]
    public void GetCell_ForTextRows_PutsTheLastRowAtTheSouth()
    {
        NavigationGrid grid = TestGrids.FromRows(Room);

        Assert.That(grid.GetCell(1, 1).Surface, Is.EqualTo(NavigationSurface.Obstacle));
        Assert.That(grid.GetCell(3, 3).HeightAtMin, Is.EqualTo(1f));
    }

    [Test]
    public void GetCell_OutsideGrid_Throws()
    {
        NavigationGrid grid = TestGrids.FromRows(Room);
        Action read = () => grid.GetCell(5, 0);

        Assert.That(read, Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void HasLineOfSight_AcrossOpenFloor_ReturnsTrue()
    {
        NavigationGrid grid = TestGrids.FromRows(Room);

        Assert.That(grid.HasLineOfSight(TestGrids.Center(grid, 2, 1), TestGrids.Center(grid, 2, 3)), Is.True);
    }

    [Test]
    public void HasLineOfSight_AcrossPlateauEdge_ReturnsFalseBothWays()
    {
        NavigationGrid grid = TestGrids.FromRows("#####", ".>^^.", "#####");
        WorldPosition plateau = TestGrids.Center(grid, 3, 1);
        WorldPosition lowerFloor = TestGrids.Center(grid, 4, 1);

        Assert.That(grid.HasLineOfSight(plateau, lowerFloor), Is.False);
        Assert.That(grid.HasLineOfSight(lowerFloor, plateau), Is.False);
    }

    [Test]
    public void HasLineOfSight_ThroughObstacle_ReturnsFalse()
    {
        NavigationGrid grid = TestGrids.FromRows("...", ".o.", "...");

        Assert.That(grid.HasLineOfSight(TestGrids.Center(grid, 0, 0), TestGrids.Center(grid, 2, 2)), Is.False);
    }

    [Test]
    public void HasLineOfSight_ToNonFinitePoint_ReturnsFalse()
    {
        NavigationGrid grid = TestGrids.FromRows(Room);
        WorldPosition from = TestGrids.Center(grid, 2, 1);

        Assert.That(grid.HasLineOfSight(from, new WorldPosition(float.NaN, 0f, 1f)), Is.False);
        Assert.That(grid.HasLineOfSight(from, new WorldPosition(float.PositiveInfinity, 0f, 1f)), Is.False);
    }

    [Test]
    public void HasLineOfSight_UpRampOntoPlateau_ReturnsTrue()
    {
        NavigationGrid grid = TestGrids.FromRows("#####", ".>^^.", "#####");

        Assert.That(grid.HasLineOfSight(TestGrids.Center(grid, 0, 1), TestGrids.Center(grid, 3, 1)), Is.True);
    }

    [Test]
    public void TrySampleHeight_OnBlockedCell_ReturnsFalse()
    {
        NavigationGrid grid = TestGrids.FromRows(Room);

        Assert.That(grid.TrySampleHeight(1.5f, 1.5f, out float _), Is.False);
    }

    [Test]
    public void TrySampleHeight_OnRampAlongZ_IgnoresX()
    {
        NavigationGrid grid = TestGrids.FromRows("s", "n");

        grid.TrySampleHeight(0.1f, 0.25f, out float rising);
        grid.TrySampleHeight(0.9f, 1.25f, out float falling);

        Assert.That(rising, Is.EqualTo(0.25f).Within(1e-5f));
        Assert.That(falling, Is.EqualTo(0.75f).Within(1e-5f));
    }
}
}
