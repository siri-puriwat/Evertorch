using System;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class MovementModelTests
{
    [TestCase(1f, 0f, 1f, 0f)]
    [TestCase(0f, -1f, 0f, -1f)]
    [TestCase(0.3f, 0f, 1f, 0f)]
    [TestCase(0f, 250f, 0f, 1f)]
    [TestCase(-3f, 4f, -0.6f, 0.8f)]
    public void NormalizeOrZero_ForFiniteDirection_ReturnsUnitVectorWhateverTheRequestedLength(
        float x,
        float z,
        float expectedX,
        float expectedZ)
    {
        WorldDirection direction = MovementModel.NormalizeOrZero(x, z);

        Assert.That(direction.X, Is.EqualTo(expectedX).Within(1e-6f));
        Assert.That(direction.Z, Is.EqualTo(expectedZ).Within(1e-6f));
    }

    [TestCase(1f, 1f)]
    [TestCase(-1f, 1f)]
    [TestCase(0.1f, -0.1f)]
    [TestCase(1e30f, 1e30f)]
    [TestCase(3.4e38f, -3.4e38f)]
    public void NormalizeOrZero_ForDiagonal_HasUnitLength(float x, float z)
    {
        WorldDirection direction = MovementModel.NormalizeOrZero(x, z);

        double length = Math.Sqrt((double)direction.X * direction.X + (double)direction.Z * direction.Z);
        Assert.That(length, Is.EqualTo(1d).Within(1e-6));
    }

    [TestCase(0f, 0f)]
    [TestCase(0.0005f, 0.0005f)]
    [TestCase(-0.0009f, 0f)]
    public void NormalizeOrZero_BelowMinimumMagnitude_ReturnsZero(float x, float z)
    {
        Assert.That(MovementModel.NormalizeOrZero(x, z), Is.EqualTo(new WorldDirection(0f, 0f)));
    }

    [TestCase(float.NaN, 1f)]
    [TestCase(1f, float.NaN)]
    [TestCase(float.PositiveInfinity, 0f)]
    [TestCase(0f, float.NegativeInfinity)]
    [TestCase(float.PositiveInfinity, float.NegativeInfinity)]
    public void NormalizeOrZero_ForNonFiniteInput_ReturnsZero(float x, float z)
    {
        Assert.That(MovementModel.NormalizeOrZero(x, z), Is.EqualTo(new WorldDirection(0f, 0f)));
    }

    [TestCase(0.05f)]
    [TestCase(0.08f)]
    [TestCase(0.3f)]
    public void Step_BetweenTwoWallsThatTouchAtACorner_NeverSqueezesThrough(float agentRadius)
    {
        var floor = NavigationCell.Level(NavigationSurface.Floor, 0f);
        var wall = NavigationCell.Level(NavigationSurface.Wall, 0f);

        // Row 0 is the south: wall at (1, 0) and at (0, 1), floor at (0, 0) and (1, 1).
        NavigationCell[] cells = { floor, wall, wall, floor };
        var grid = new NavigationGrid(2, 2, 1f, 0f, 0f, agentRadius, 0.4f, cells);

        // From here quarter-metre pieces along the diagonal would land at 0.93 and then at 1.11, clear of both
        // walls on the far side, which is how a narrow body once slipped through.
        var position = new WorldPosition(0.4f, 0f, 0.4f);
        var facing = new WorldDirection(0f, 1f);

        for (int tick = 0; tick < 60; tick++)
        {
            MovementStep step = MovementModel.Step(grid, position, facing, new WorldDirection(1f, 1f), 5f, 0.05f);
            position = step.Position;
        }

        Assert.That(position.X < 1f && position.Z < 1f, Is.True, "still in the south-west cell at " + position);
        Assert.That(
            grid.HasLineOfSight(new WorldPosition(0.5f, 0f, 0.5f), new WorldPosition(1.5f, 0f, 1.5f)),
            Is.False,
            "and the pathfinder must not think the corner can be crossed either");
    }

    [Test]
    public void Step_WithABodyNarrowerThanTheUsualPiece_CannotJumpAThinWall()
    {
        var floor = NavigationCell.Level(NavigationSurface.Floor, 0f);
        var wall = NavigationCell.Level(NavigationSurface.Wall, 0f);
        var cells = new NavigationCell[30];
        for (int index = 0; index < cells.Length; index++)
        {
            cells[index] = index == 10 ? wall : floor;
        }

        // Cells of 0.1 m and a body of 0.05 m: the wall spans x 1.0 to 1.1, thinner than a quarter-metre piece.
        var grid = new NavigationGrid(30, 1, 0.1f, 0f, 0f, 0.05f, 0.4f, cells);
        var position = new WorldPosition(0.92f, 0f, 0.05f);
        var facing = new WorldDirection(1f, 0f);

        for (int tick = 0; tick < 40; tick++)
        {
            MovementStep step = MovementModel.Step(grid, position, facing, new WorldDirection(1f, 0f), 5f, 0.05f);
            position = step.Position;
        }

        Assert.That(position.X, Is.LessThanOrEqualTo(0.95f + 1e-4f), "held on the near side of the wall");
        Assert.That(grid.MoveStepLength, Is.EqualTo(0.05f));
    }
}
}
