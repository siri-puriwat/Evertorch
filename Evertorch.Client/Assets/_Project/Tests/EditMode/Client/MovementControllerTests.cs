using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class MovementControllerTests
{
    private const float Step = ClientTestGrids.Speed * ClientTestGrids.TickSeconds;

    [Test]
    public void Dead_Controller_RefusesWalksAndProducesNoMovementUntilAlive()
    {
        MovementController controller = new MovementController(ClientTestGrids.CreateYard());
        Assert.That(controller.TryMoveTo(ClientTestGrids.Center(2, 8), ClientTestGrids.Center(6, 8)), Is.True);

        controller.IsDead = true;
        controller.SetManualDirection(1f, 0f);
        WorldDirection whileDead = controller.Tick(ClientTestGrids.Center(2, 8), Step);
        bool isWalkAccepted = controller.TryMoveTo(ClientTestGrids.Center(2, 8), ClientTestGrids.Center(6, 8));
        controller.IsDead = false;

        Assert.That(whileDead, Is.EqualTo(default(WorldDirection)));
        Assert.That(isWalkAccepted, Is.False);
        Assert.That(controller.HasPath, Is.False, "the walk from before the death is dropped");
        Assert.That(controller.Tick(ClientTestGrids.Center(2, 8), Step), Is.EqualTo(new WorldDirection(1f, 0f)));
    }

    [Test]
    public void Tick_WithNoInput_IsZero()
    {
        MovementController controller = new MovementController(ClientTestGrids.CreateYard());

        Assert.That(controller.Tick(ClientTestGrids.Center(2, 5), Step), Is.EqualTo(default(WorldDirection)));
    }

    [TestCase(1f, 0f, 1f, 0f)]
    [TestCase(0.2f, 0f, 1f, 0f)]
    [TestCase(30f, 40f, 0.6f, 0.8f)]
    public void Tick_WithManualDirection_IsThatHeadingAtUnitLength(float x, float z, float expectedX, float expectedZ)
    {
        MovementController controller = new MovementController(ClientTestGrids.CreateYard());

        controller.SetManualDirection(x, z);
        WorldDirection direction = controller.Tick(ClientTestGrids.Center(2, 5), Step);

        Assert.That(direction.X, Is.EqualTo(expectedX).Within(1e-5f));
        Assert.That(direction.Z, Is.EqualTo(expectedZ).Within(1e-5f));
    }

    [Test]
    public void Tick_AfterManualDirectionIsReleased_IsZeroAgain()
    {
        MovementController controller = new MovementController(ClientTestGrids.CreateYard());
        controller.SetManualDirection(1f, 0f);
        controller.Tick(ClientTestGrids.Center(2, 5), Step);

        controller.SetManualDirection(0f, 0f);

        Assert.That(controller.Tick(ClientTestGrids.Center(2, 5), Step), Is.EqualTo(default(WorldDirection)));
    }

    [Test]
    public void TryMoveTo_ReachablePoint_SteersTowardTheFirstWaypoint()
    {
        MovementController controller = new MovementController(ClientTestGrids.CreateYard());

        bool accepted = controller.TryMoveTo(ClientTestGrids.Center(2, 8), ClientTestGrids.Center(9, 8));
        WorldDirection direction = controller.Tick(ClientTestGrids.Center(2, 8), Step);

        Assert.That(accepted, Is.True);
        Assert.That(controller.HasPath, Is.True);
        Assert.That(direction.X, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(direction.Z, Is.EqualTo(0f).Within(1e-5f));
    }

    [Test]
    public void TryMoveTo_BehindAWall_WalksAroundItAndArrives()
    {
        NavigationGrid grid = ClientTestGrids.CreateYard();
        MovementController controller = new MovementController(grid);
        WorldPosition position = ClientTestGrids.Center(2, 5);
        WorldPosition destination = ClientTestGrids.Center(7, 5);
        WorldDirection facing = new WorldDirection(0f, 1f);

        Assert.That(controller.TryMoveTo(position, destination), Is.True);
        Assert.That(controller.Path.Count, Is.GreaterThan(1), "a straight line would cross the wall");

        int ticks = 0;
        while (controller.HasPath && ticks < 400)
        {
            WorldDirection direction = controller.Tick(position, Step);
            MovementStep step = MovementModel.Step(
                grid,
                position,
                facing,
                direction,
                ClientTestGrids.Speed,
                ClientTestGrids.TickSeconds);
            position = step.Position;
            facing = step.Facing;
            ticks++;
        }

        Assert.That(controller.HasPath, Is.False);
        Assert.That(position.X, Is.EqualTo(destination.X).Within((Step * 0.5f) + 1e-4f));
        Assert.That(position.Z, Is.EqualTo(destination.Z).Within((Step * 0.5f) + 1e-4f));
        Assert.That(controller.Tick(position, Step), Is.EqualTo(default(WorldDirection)));
    }

    [TestCase(9, 3, "a sealed pocket")]
    [TestCase(5, 5, "inside a wall")]
    [TestCase(40, 40, "outside the map")]
    public void TryMoveTo_UnreachablePoint_IsRefusedAndProducesNoMovement(int column, int row, string why)
    {
        MovementController controller = new MovementController(ClientTestGrids.CreateYard());

        bool accepted = controller.TryMoveTo(ClientTestGrids.Center(2, 5), ClientTestGrids.Center(column, row));

        Assert.That(accepted, Is.False, why);
        Assert.That(controller.HasPath, Is.False);
        Assert.That(controller.RejectedMoveRequests, Is.EqualTo(1));
        Assert.That(controller.Tick(ClientTestGrids.Center(2, 5), Step), Is.EqualTo(default(WorldDirection)));
    }

    [Test]
    public void TryMoveTo_UnreachablePointDuringAWalk_LeavesTheWalkAlone()
    {
        MovementController controller = new MovementController(ClientTestGrids.CreateYard());
        controller.TryMoveTo(ClientTestGrids.Center(2, 8), ClientTestGrids.Center(9, 8));

        bool accepted = controller.TryMoveTo(ClientTestGrids.Center(2, 8), ClientTestGrids.Center(9, 3));

        Assert.That(accepted, Is.False);
        Assert.That(controller.HasPath, Is.True);
        Assert.That(controller.Path[controller.Path.Count - 1], Is.EqualTo(ClientTestGrids.Center(9, 8)));
    }

    [Test]
    public void Tick_ManualDirectionDuringAWalk_CancelsItInThatSameTick()
    {
        MovementController controller = new MovementController(ClientTestGrids.CreateYard());
        controller.TryMoveTo(ClientTestGrids.Center(2, 8), ClientTestGrids.Center(9, 8));
        controller.Tick(ClientTestGrids.Center(2, 8), Step);

        controller.SetManualDirection(0f, -1f);
        WorldDirection direction = controller.Tick(ClientTestGrids.Center(2, 8), Step);

        Assert.That(direction, Is.EqualTo(new WorldDirection(0f, -1f)), "the manual heading wins over the walk");
        Assert.That(controller.HasPath, Is.False);
        Assert.That(controller.CancelledPaths, Is.EqualTo(1));
    }

    [Test]
    public void Tick_AfterManualCancel_DoesNotResumeTheWalkWhenTheKeyIsReleased()
    {
        MovementController controller = new MovementController(ClientTestGrids.CreateYard());
        controller.TryMoveTo(ClientTestGrids.Center(2, 8), ClientTestGrids.Center(9, 8));
        controller.SetManualDirection(0f, -1f);
        controller.Tick(ClientTestGrids.Center(2, 8), Step);

        controller.SetManualDirection(0f, 0f);

        Assert.That(controller.Tick(ClientTestGrids.Center(2, 8), Step), Is.EqualTo(default(WorldDirection)));
    }

    [Test]
    public void Tick_WhenTheBodyStopsMakingProgress_GivesUpTheWalk()
    {
        MovementController controller = new MovementController(ClientTestGrids.CreateYard());
        controller.TryMoveTo(ClientTestGrids.Center(2, 8), ClientTestGrids.Center(9, 8));

        for (int tick = 0; tick <= PathFollower.StuckTickLimit; tick++)
        {
            controller.Tick(ClientTestGrids.Center(2, 8), Step);
        }

        Assert.That(controller.HasPath, Is.False);
    }

    [Test]
    public void MoveIntentProducer_NumbersIntentsFromOneAndNormalizesTheDirection()
    {
        MoveIntentProducer producer = new MoveIntentProducer();

        MoveIntent first = producer.Next(10, new WorldDirection(3f, 4f));
        MoveIntent second = producer.Next(11, default);

        Assert.That(first.Sequence, Is.EqualTo(1u));
        Assert.That(first.ClientTick, Is.EqualTo(10u));
        Assert.That(first.DirectionX, Is.EqualTo(0.6f).Within(1e-6f));
        Assert.That(first.DirectionZ, Is.EqualTo(0.8f).Within(1e-6f));
        Assert.That(second, Is.EqualTo(new MoveIntent(2, 11, 0f, 0f)));
        Assert.That(producer.LastSequence, Is.EqualTo(2u));
    }
}
}
