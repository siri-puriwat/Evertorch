using System.Collections.Generic;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
// Mirrors the .NET navigation tests on the same map, so Unity's compiler and runtime are held to the same answers.
[TestFixture]
public sealed class SharedNavigationTests
{
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
        "##########"
    };

    private static readonly string[] Arena =
    {
        "############",
        "#......^^^.#",
        "#......^^^.#",
        "#.....>^^^.#",
        "#..........#",
        "#....#.....#",
        "#..........#",
        "############"
    };

    [TestCase(2.5f, 1.5f, true)]
    [TestCase(1.1f, 1.5f, false)]
    [TestCase(6.8f, 6.5f, false)]
    [TestCase(7.5f, 6.5f, true)]
    [TestCase(float.NaN, 1.5f, false)]
    public void CanOccupy_ForPoint_MatchesDotNet(float x, float z, bool expected)
    {
        Assert.That(CreateYard().CanOccupy(x, z), Is.EqualTo(expected));
    }

    private static NavigationGrid CreateYard()
    {
        return Create(Yard);
    }

    private static NavigationGrid Create(string[] northFirstRows)
    {
        int rows = northFirstRows.Length;
        int columns = northFirstRows[0].Length;
        var cells = new NavigationCell[rows * columns];
        for (int row = 0; row < rows; row++)
        {
            string text = northFirstRows[rows - 1 - row];
            for (int column = 0; column < columns; column++)
            {
                cells[row * columns + column] = ToCell(text[column]);
            }
        }

        return new NavigationGrid(columns, rows, 1f, 0f, 0f, 0.3f, 0.4f, cells);
    }

    private static NavigationCell ToCell(char symbol)
    {
        switch (symbol)
        {
            case '.':
                return NavigationCell.Level(NavigationSurface.Floor, 0f);
            case '^':
                return NavigationCell.Level(NavigationSurface.Floor, 1f);
            case 'o':
                return NavigationCell.Level(NavigationSurface.Obstacle, 0f);
            case '>':
                return NavigationCell.Ramp(RampAxis.X, 0f, 1f);
            default:
                return NavigationCell.Level(NavigationSurface.Wall, 0f);
        }
    }

    [Test]
    public void NormalizeOrZero_ForDiagonalAndInvalidInput_MatchesDotNet()
    {
        WorldDirection diagonal = MovementModel.NormalizeOrZero(-3f, 4f);

        Assert.That(diagonal.X, Is.EqualTo(-0.6f).Within(1e-6f));
        Assert.That(diagonal.Z, Is.EqualTo(0.8f).Within(1e-6f));
        Assert.That(MovementModel.NormalizeOrZero(float.NaN, 1f), Is.EqualTo(new WorldDirection(0f, 0f)));
        Assert.That(MovementModel.NormalizeOrZero(0.0005f, 0f), Is.EqualTo(new WorldDirection(0f, 0f)));
    }

    [Test]
    public void Step_ForReferenceWalk_EndsWhereDotNetEnds()
    {
        NavigationGrid grid = Create(Arena);
        var north = new WorldDirection(0f, 1f);
        var position = new WorldPosition(2f, 0f, 2.5f);
        WorldDirection[] legs = { new(1f, 1f), new(1f, 0f), new(0.2f, 1f) };

        foreach (WorldDirection leg in legs)
        {
            for (int tick = 0; tick < 30; tick++)
            {
                position = MovementModel.Step(grid, position, north, leg, 5f, 0.05f).Position;
            }
        }

        Assert.That(position.X, Is.EqualTo(6.694254f).Within(1e-5f));
        Assert.That(position.Y, Is.EqualTo(0f).Within(1e-5f));
        Assert.That(position.Z, Is.EqualTo(6.5658665f).Within(1e-5f));
    }

    [Test]
    public void Step_UpTheRamp_ReachesThePlateauHeight()
    {
        NavigationGrid grid = Create(Arena);
        var position = new WorldPosition(5.5f, 0f, 4.5f);

        for (int tick = 0; tick < 12; tick++)
        {
            position = MovementModel.Step(grid, position, default, new WorldDirection(1f, 0f), 5f, 0.05f).Position;
        }

        Assert.That(position.X, Is.GreaterThan(7f));
        Assert.That(position.Y, Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public void TryFindPath_ForReferenceRequest_MatchesGoldenWaypoints()
    {
        NavigationGrid grid = CreateYard();
        var waypoints = new List<WorldPosition>();

        bool found = new GridPathfinder(grid).TryFindPath(
            grid.GetCellCenter(1, 1),
            grid.GetCellCenter(8, 7),
            10000,
            waypoints);

        WorldPosition[] expected =
        {
            new(3.5f, 0f, 3.5f),
            new(5.5f, 0f, 3.5f),
            new(5.5f, 0f, 5.5f),
            new(7.5f, 0.5f, 5.5f),
            new(8.5f, 1f, 7.5f)
        };
        Assert.That(found, Is.True);
        Assert.That(waypoints, Is.EqualTo(expected));
    }

    [Test]
    public void TryFindPath_ToBlockedGoal_ReturnsFalse()
    {
        NavigationGrid grid = CreateYard();
        var waypoints = new List<WorldPosition>();

        bool found = new GridPathfinder(grid).TryFindPath(
            grid.GetCellCenter(1, 1),
            new WorldPosition(4.5f, 0f, 2.5f),
            10000,
            waypoints);

        Assert.That(found, Is.False);
        Assert.That(waypoints, Is.Empty);
    }

    [Test]
    public void TrySampleHeight_OnRamp_Interpolates()
    {
        bool found = CreateYard().TrySampleHeight(7.25f, 5.5f, out float height);

        Assert.That(found, Is.True);
        Assert.That(height, Is.EqualTo(0.25f).Within(1e-5f));
    }
}
}
