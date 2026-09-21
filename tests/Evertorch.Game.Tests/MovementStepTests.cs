using System;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
[TestFixture]
public sealed class MovementStepTests
{
    private const float Speed = 5f;
    private const float Delta = 0.05f;
    private const float Tolerance = 1e-5f;

    // North is up; cells are 1 m with the south-west corner at the origin. The plateau is reached only by its ramp.
    private static readonly string[] Arena =
    {
        "############",
        "#......^^^.#",
        "#......^^^.#",
        "#.....>^^^.#",
        "#..........#",
        "#....#.....#",
        "#..........#",
        "############",
    };

    private static readonly WorldDirection North = new WorldDirection(0f, 1f);
    private static readonly WorldDirection East = new WorldDirection(1f, 0f);

    [Test]
    public void Step_WithUnitDirection_MovesSpeedTimesDelta()
    {
        NavigationGrid grid = TestGrids.FromRows(Arena);

        MovementStep step = MovementModel.Step(grid, At(grid, 2f, 2.5f), North, East, Speed, Delta);

        Assert.That(step.Position.X, Is.EqualTo(2.25f).Within(Tolerance));
        Assert.That(step.Position.Z, Is.EqualTo(2.5f));
        Assert.That(step.VelocityX, Is.EqualTo(Speed).Within(1e-3f));
        Assert.That(step.VelocityZ, Is.EqualTo(0f));
        Assert.That(step.Facing, Is.EqualTo(East));
        Assert.That(step.IsMoving, Is.True);
    }

    [TestCase(1f, 1f)]
    [TestCase(-1f, 1f)]
    [TestCase(0.2f, -0.2f)]
    [TestCase(300f, 400f)]
    public void Step_Diagonal_DoesNotExceedStraightLineSpeed(float directionX, float directionZ)
    {
        NavigationGrid grid = TestGrids.FromRows(Arena);
        WorldPosition start = At(grid, 3f, 2.5f);

        MovementStep step =
            MovementModel.Step(grid, start, North, new WorldDirection(directionX, directionZ), Speed, Delta);

        Assert.That(Distance(start, step.Position), Is.EqualTo(Speed * Delta).Within(Tolerance));
    }

    [TestCase(0.3f)]
    [TestCase(1f)]
    [TestCase(250f)]
    public void Step_WhateverTheRequestedMagnitude_MovesTheSameDistance(float magnitude)
    {
        NavigationGrid grid = TestGrids.FromRows(Arena);

        MovementStep step = MovementModel.Step(
            grid,
            At(grid, 2f, 2.5f),
            North,
            new WorldDirection(magnitude, 0f),
            Speed,
            Delta);

        Assert.That(step.Position.X, Is.EqualTo(2.25f).Within(Tolerance));
    }

    [Test]
    public void Step_IntoWall_StopsAtWallAndSlidesAlongIt()
    {
        NavigationGrid grid = TestGrids.FromRows(Arena);
        WorldPosition position = At(grid, 10.2f, 1.5f);
        WorldDirection northEast = new WorldDirection(1f, 1f);

        for (int tick = 0; tick < 20; tick++)
        {
            position = MovementModel.Step(grid, position, North, northEast, Speed, Delta).Position;
        }

        Assert.That(position.X, Is.LessThanOrEqualTo(11f - TestGrids.AgentRadius));
        Assert.That(position.X, Is.GreaterThan(10.5f), "it should have closed in on the wall before sliding");
        Assert.That(position.Z, Is.GreaterThan(4f), "it should keep moving north along the wall");
        Assert.That(grid.CanOccupy(position.X, position.Z), Is.True);
    }

    [Test]
    public void Step_IntoCorner_ComesToRest()
    {
        NavigationGrid grid = TestGrids.FromRows(Arena);
        WorldPosition position = At(grid, 10f, 2f);
        WorldDirection southEast = new WorldDirection(1f, -1f);
        MovementStep step = default;

        for (int tick = 0; tick < 40; tick++)
        {
            step = MovementModel.Step(grid, position, North, southEast, Speed, Delta);
            position = step.Position;
        }

        Assert.That(step.IsMoving, Is.False);
        Assert.That(step.Facing.X, Is.GreaterThan(0f), "facing follows the request even when the body cannot move");
        Assert.That(grid.CanOccupy(position.X, position.Z), Is.True);
    }

    [Test]
    public void Step_UpRamp_FollowsSampledHeightOntoThePlateau()
    {
        NavigationGrid grid = TestGrids.FromRows(Arena);
        WorldPosition position = At(grid, 5.5f, 4.5f);
        float highestVerticalVelocity = 0f;

        for (int tick = 0; tick < 12; tick++)
        {
            MovementStep step = MovementModel.Step(grid, position, North, East, Speed, Delta);
            highestVerticalVelocity = Math.Max(highestVerticalVelocity, step.VelocityY);
            position = step.Position;
        }

        Assert.That(position.X, Is.GreaterThan(7f));
        Assert.That(position.Y, Is.EqualTo(1f).Within(Tolerance));
        Assert.That(highestVerticalVelocity, Is.EqualTo(Speed).Within(1e-2f), "the ramp rises 1 m per metre");
    }

    [Test]
    public void Step_AcrossPlateauEdge_IsBlockedFromBelowAndAbove()
    {
        NavigationGrid grid = TestGrids.FromRows(Arena);
        WorldPosition below = At(grid, 6.2f, 5.5f);
        WorldPosition above = At(grid, 7.8f, 5.5f);

        for (int tick = 0; tick < 20; tick++)
        {
            below = MovementModel.Step(grid, below, North, East, Speed, Delta).Position;
            above = MovementModel.Step(grid, above, North, new WorldDirection(-1f, 0f), Speed, Delta).Position;
        }

        Assert.That(below.X, Is.LessThan(7f));
        Assert.That(below.Y, Is.EqualTo(0f));
        Assert.That(above.X, Is.GreaterThan(7f));
        Assert.That(above.Y, Is.EqualTo(1f));
    }

    [TestCase(0.05f)]
    [TestCase(0.25f)]
    [TestCase(1f)]
    public void Step_AtMaximumSpeed_DoesNotTunnelThroughOneCellWall(float delta)
    {
        NavigationGrid grid = TestGrids.FromRows(Arena);
        WorldPosition position = At(grid, 1.5f, 2.5f);

        for (int tick = 0; tick < 50; tick++)
        {
            position = MovementModel.Step(grid, position, North, East, 20f, delta).Position;
        }

        Assert.That(position.X, Is.LessThan(5f), "the obstacle at column 5 of that row must stop it");
        Assert.That(grid.CanOccupy(position.X, position.Z), Is.True);
    }

    [Test]
    public void Step_WithZeroDirection_KeepsPositionFacingAndZeroVelocity()
    {
        NavigationGrid grid = TestGrids.FromRows(Arena);
        WorldPosition start = At(grid, 2f, 2.5f);

        MovementStep step = MovementModel.Step(grid, start, East, new WorldDirection(0f, 0f), Speed, Delta);

        Assert.That(step.Position, Is.EqualTo(start));
        Assert.That(step.Facing, Is.EqualTo(East));
        Assert.That(step.IsMoving, Is.False);
    }

    [TestCase(float.NaN, 0f, Speed, Delta)]
    [TestCase(float.PositiveInfinity, 0f, Speed, Delta)]
    [TestCase(1f, 0f, float.NaN, Delta)]
    [TestCase(1f, 0f, float.PositiveInfinity, Delta)]
    [TestCase(1f, 0f, 1e30f, Delta)]
    [TestCase(1f, 0f, -5f, Delta)]
    [TestCase(1f, 0f, Speed, 0f)]
    [TestCase(1f, 0f, Speed, -0.05f)]
    [TestCase(1f, 0f, Speed, float.NaN)]
    public void Step_WithInputThatIsNotMovement_DoesNotMove(
        float directionX,
        float directionZ,
        float speed,
        float delta)
    {
        NavigationGrid grid = TestGrids.FromRows(Arena);
        WorldPosition start = At(grid, 2f, 2.5f);

        MovementStep step =
            MovementModel.Step(grid, start, North, new WorldDirection(directionX, directionZ), speed, delta);

        Assert.That(step.Position, Is.EqualTo(start));
        Assert.That(step.IsMoving, Is.False);
    }

    [Test]
    public void Step_FromAPositionTheGridRejects_StaysThere()
    {
        NavigationGrid grid = TestGrids.FromRows(Arena);
        WorldPosition insideWall = new WorldPosition(0.5f, 0f, 0.5f);

        MovementStep step = MovementModel.Step(grid, insideWall, North, East, Speed, Delta);

        Assert.That(step.Position, Is.EqualTo(insideWall));
    }

    [Test]
    public void Step_ForAnySeededWalk_NeverLeavesWalkableSpaceOrExceedsSpeed()
    {
        NavigationGrid grid = TestGrids.FromRows(Arena);
        WorldPosition position = At(grid, 2f, 2.5f);
        ulong state = 0x9E3779B97F4A7C15UL;

        for (int tick = 0; tick < 5000; tick++)
        {
            state = (state * 6364136223846793005UL) + 1442695040888963407UL;
            float directionX = ((int)(state >> 40) % 2001 - 1000) / 1000f;
            float directionZ = ((int)(state >> 20) % 2001 - 1000) / 1000f;
            WorldPosition before = position;

            position = MovementModel
                .Step(grid, position, North, new WorldDirection(directionX, directionZ), Speed, Delta).Position;

            Assert.That(grid.CanOccupy(position.X, position.Z), Is.True, "tick " + tick + " at " + position);
            Assert.That(Distance(before, position), Is.LessThanOrEqualTo((Speed * Delta) + 1e-4f), "tick " + tick);
        }
    }

    [Test]
    public void Step_ForReferenceWalk_EndsAtTheGoldenPosition()
    {
        NavigationGrid grid = TestGrids.FromRows(Arena);
        WorldPosition position = At(grid, 2f, 2.5f);

        position = Walk(grid, position, new WorldDirection(1f, 1f), 30);
        position = Walk(grid, position, new WorldDirection(1f, 0f), 30);
        position = Walk(grid, position, new WorldDirection(0.2f, 1f), 30);

        Assert.That(position.X, Is.EqualTo(ReferenceWalk.ExpectedX).Within(Tolerance), position.ToString());
        Assert.That(position.Y, Is.EqualTo(ReferenceWalk.ExpectedY).Within(Tolerance), position.ToString());
        Assert.That(position.Z, Is.EqualTo(ReferenceWalk.ExpectedZ).Within(Tolerance), position.ToString());
    }

    private static WorldPosition Walk(NavigationGrid grid, WorldPosition position, WorldDirection direction, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            position = MovementModel.Step(grid, position, North, direction, Speed, Delta).Position;
        }

        return position;
    }

    private static WorldPosition At(NavigationGrid grid, float x, float z)
    {
        grid.TrySampleHeight(x, z, out float height);
        return new WorldPosition(x, height, z);
    }

    private static float Distance(WorldPosition from, WorldPosition to)
    {
        double deltaX = to.X - from.X;
        double deltaZ = to.Z - from.Z;
        return (float)Math.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));
    }

    // Read from the implementation once, checked against the map by hand, and mirrored in the Unity EditMode test.
    private static class ReferenceWalk
    {
        public const float ExpectedX = 6.694254f;
        public const float ExpectedY = 0f;
        public const float ExpectedZ = 6.5658665f;
    }
}
}
