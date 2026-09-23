using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class MovementPredictorTests
{
    private const float Step = ClientTestGrids.Speed * ClientTestGrids.TickSeconds;

    private static readonly WorldPosition Start = ClientTestGrids.Center(2, 8);

    private static MovementPredictor CreatePredictor(NavigationGrid grid)
    {
        return new MovementPredictor(
            grid,
            ClientTestGrids.Speed,
            ClientTestGrids.TickSeconds,
            Start,
            new WorldDirection(0f, 1f));
    }

    [Test]
    public void Apply_BeyondThePendingLimit_DropsTheOldestAndCountsIt()
    {
        MovementPredictor predictor = CreatePredictor(ClientTestGrids.CreateYard());

        for (uint sequence = 1; sequence <= MovementPredictor.MaxPendingInputs + 3; sequence++)
        {
            predictor.Apply(new MoveIntent(sequence, sequence, 0f, 0f));
        }

        Assert.That(predictor.PendingCount, Is.EqualTo(MovementPredictor.MaxPendingInputs));
        Assert.That(predictor.DroppedPendingInputs, Is.EqualTo(3));
    }

    [Test]
    public void Apply_IntoAWall_StopsAtTheWallLikeTheServerWould()
    {
        NavigationGrid grid = ClientTestGrids.CreateYard();
        MovementPredictor predictor = CreatePredictor(grid);

        for (uint sequence = 1; sequence <= 40; sequence++)
        {
            predictor.Apply(new MoveIntent(sequence, sequence, -1f, 0f));
        }

        // The west wall ends at x = 1 and the body is 0.3 wide, so it can rest no further west than 1.3 and, moving
        // in pieces of at most a quarter metre, no further east than one piece short of that.
        Assert.That(predictor.Position.X, Is.InRange(1.3f - 1e-4f, 1.3f + NavigationGrid.MaxMoveStep));
        Assert.That(grid.CanOccupy(predictor.Position.X, predictor.Position.Z), Is.True);
        Assert.That(predictor.IsMoving, Is.False);
    }

    [Test]
    public void Apply_MovesExactlyAsTheSharedModelDoes()
    {
        NavigationGrid grid = ClientTestGrids.CreateYard();
        MovementPredictor predictor = CreatePredictor(grid);

        predictor.Apply(new MoveIntent(1, 1, 1f, 0f));

        MovementStep expected = MovementModel.Step(
            grid,
            Start,
            new WorldDirection(0f, 1f),
            new WorldDirection(1f, 0f),
            ClientTestGrids.Speed,
            ClientTestGrids.TickSeconds);
        Assert.That(predictor.Position, Is.EqualTo(expected.Position));
        Assert.That(predictor.Facing, Is.EqualTo(expected.Facing));
        Assert.That(predictor.IsMoving, Is.True);
        Assert.That(predictor.PendingCount, Is.EqualTo(1));
    }

    [Test]
    public void ForgetPendingStops_RemovesStopsAndKeepsMovesInOrder()
    {
        MovementPredictor predictor = CreatePredictor(ClientTestGrids.CreateYard());
        predictor.Apply(new MoveIntent(1, 1, 0f, 0f));
        predictor.Apply(new MoveIntent(2, 2, 1f, 0f));
        predictor.Apply(new MoveIntent(3, 3, 0f, 0f));
        predictor.Apply(new MoveIntent(4, 4, 0f, -1f));

        predictor.ForgetPendingStops();
        predictor.Reconcile(ClientWorldFixture.State(ClientWorldFixture.LocalEntity, Start), 0);

        Assert.That(predictor.PendingCount, Is.EqualTo(2));
        Assert.That(predictor.Position.X, Is.EqualTo(Start.X + Step).Within(1e-5f), "the move east was kept");
        Assert.That(predictor.Position.Z, Is.EqualTo(Start.Z - Step).Within(1e-5f), "then the move south");
    }

    [Test]
    public void Reconcile_AcrossSequenceWrapAround_StillKnowsWhichInputsAreNewer()
    {
        MovementPredictor predictor = CreatePredictor(ClientTestGrids.CreateYard());
        predictor.Apply(new MoveIntent(uint.MaxValue, 1, 1f, 0f));
        predictor.Apply(new MoveIntent(0, 2, 1f, 0f));
        predictor.Apply(new MoveIntent(1, 3, 1f, 0f));

        predictor.Reconcile(ClientWorldFixture.State(ClientWorldFixture.LocalEntity, Start), uint.MaxValue);

        Assert.That(predictor.PendingCount, Is.EqualTo(2));
        Assert.That(predictor.Position.X, Is.EqualTo(Start.X + 2f * Step).Within(1e-5f));
    }

    [Test]
    public void Reconcile_ReplaysOnlyTheInputsTheServerHasNotApplied()
    {
        MovementPredictor predictor = CreatePredictor(ClientTestGrids.CreateYard());
        for (uint sequence = 1; sequence <= 4; sequence++)
        {
            predictor.Apply(new MoveIntent(sequence, sequence, 1f, 0f));
        }

        var serverPosition = new WorldPosition(Start.X + 2f * Step, 0f, Start.Z);
        predictor.Reconcile(ClientWorldFixture.State(ClientWorldFixture.LocalEntity, serverPosition), 2);

        Assert.That(predictor.PendingCount, Is.EqualTo(2));
        Assert.That(predictor.Position.X, Is.EqualTo(Start.X + 4f * Step).Within(1e-5f));
        Assert.That(predictor.Position.Z, Is.EqualTo(Start.Z).Within(1e-5f));
    }

    [Test]
    public void Reconcile_ReportsTheServerMovingFlag()
    {
        MovementPredictor predictor = CreatePredictor(ClientTestGrids.CreateYard());
        var moving = new EntityState(
            ClientWorldFixture.LocalEntity,
            Start,
            new WorldDirection(1f, 0f),
            ClientTestGrids.Speed,
            0f,
            0f,
            EntityStateFlags.Moving);

        predictor.Reconcile(moving, 0);

        Assert.That(predictor.IsMoving, Is.True);
        Assert.That(predictor.Facing, Is.EqualTo(new WorldDirection(1f, 0f)));
    }

    [Test]
    public void Reconcile_WhenEverythingIsAcknowledged_AdoptsTheServerState()
    {
        MovementPredictor predictor = CreatePredictor(ClientTestGrids.CreateYard());
        predictor.Apply(new MoveIntent(1, 1, 1f, 0f));
        predictor.Apply(new MoveIntent(2, 2, 1f, 0f));
        var serverPosition = new WorldPosition(3f, 0f, 8.25f);

        predictor.Reconcile(ClientWorldFixture.State(ClientWorldFixture.LocalEntity, serverPosition), 2);

        Assert.That(predictor.Position, Is.EqualTo(serverPosition));
        Assert.That(predictor.PendingCount, Is.EqualTo(0));
        Assert.That(predictor.LastAcknowledgedSequence, Is.EqualTo(2u));
    }

    [Test]
    public void Reconcile_WhenTheServerDisagrees_ReplaysFromTheServerPosition()
    {
        MovementPredictor predictor = CreatePredictor(ClientTestGrids.CreateYard());
        for (uint sequence = 1; sequence <= 3; sequence++)
        {
            predictor.Apply(new MoveIntent(sequence, sequence, 1f, 0f));
        }

        var serverPosition = new WorldPosition(Start.X + 0.1f, 0f, Start.Z - 0.4f);
        predictor.Reconcile(ClientWorldFixture.State(ClientWorldFixture.LocalEntity, serverPosition), 1);

        Assert.That(predictor.Position.X, Is.EqualTo(serverPosition.X + 2f * Step).Within(1e-5f));
        Assert.That(predictor.Position.Z, Is.EqualTo(serverPosition.Z).Within(1e-5f));
    }
}
}
