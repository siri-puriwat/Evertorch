using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class RenderSmootherTests
{
    private static readonly WorldPosition Origin = new WorldPosition(10f, 0f, 10f);

    [Test]
    public void Sample_BetweenTwoTicks_BlendsThem()
    {
        RenderSmoother smoother = new RenderSmoother(Origin);

        smoother.OnTick(Origin, new WorldPosition(10.25f, 0f, 10f));

        Assert.That(smoother.Sample(0f).X, Is.EqualTo(10f).Within(1e-6f));
        Assert.That(smoother.Sample(0.5f).X, Is.EqualTo(10.125f).Within(1e-6f));
        Assert.That(smoother.Sample(1f).X, Is.EqualTo(10.25f).Within(1e-6f));
        Assert.That(smoother.Sample(7f).X, Is.EqualTo(10.25f).Within(1e-6f), "never past the current tick");
    }

    [Test]
    public void OnCorrected_BelowTheTeleportThreshold_KeepsTheDrawnPointStillAndThenEasesOver()
    {
        RenderSmoother smoother = new RenderSmoother(Origin);
        WorldPosition corrected = new WorldPosition(10.5f, 0f, 9.7f);

        smoother.OnCorrected(Origin, corrected);
        WorldPosition immediately = smoother.Sample(1f);
        smoother.Advance(RenderSmoother.CorrectionHalfLifeSeconds);
        WorldPosition afterOneHalfLife = smoother.Sample(1f);
        smoother.Advance(2f);
        WorldPosition settled = smoother.Sample(1f);

        Assert.That(immediately.X, Is.EqualTo(Origin.X).Within(1e-5f));
        Assert.That(immediately.Z, Is.EqualTo(Origin.Z).Within(1e-5f));
        Assert.That(afterOneHalfLife.X, Is.EqualTo(10.25f).Within(1e-3f));
        Assert.That(afterOneHalfLife.Z, Is.EqualTo(9.85f).Within(1e-3f));
        Assert.That(settled, Is.EqualTo(corrected));
        Assert.That(smoother.Snaps, Is.EqualTo(0));
        Assert.That(smoother.LastCorrection, Is.EqualTo(0.5831f).Within(1e-3f));
    }

    [Test]
    public void OnCorrected_AboveTheTeleportThreshold_SnapsAndCountsIt()
    {
        RenderSmoother smoother = new RenderSmoother(Origin);
        smoother.OnTick(new WorldPosition(9.75f, 0f, 10f), Origin);
        WorldPosition corrected = new WorldPosition(10f + RenderSmoother.TeleportThreshold + 0.01f, 0f, 10f);

        smoother.OnCorrected(Origin, corrected);

        Assert.That(smoother.Sample(0f), Is.EqualTo(corrected), "no blending across a teleport");
        Assert.That(smoother.Sample(1f), Is.EqualTo(corrected));
        Assert.That(smoother.Snaps, Is.EqualTo(1));
        Assert.That(smoother.PendingOffset, Is.EqualTo(0f));
    }

    [Test]
    public void OnCorrected_ExactlyAtTheThreshold_StillSmooths()
    {
        RenderSmoother smoother = new RenderSmoother(Origin);

        smoother.OnCorrected(Origin, new WorldPosition(10f + RenderSmoother.TeleportThreshold, 0f, 10f));

        Assert.That(smoother.Snaps, Is.EqualTo(0));
        Assert.That(smoother.Sample(1f).X, Is.EqualTo(Origin.X).Within(1e-5f));
    }

    [Test]
    public void OnCorrected_WithNoError_ChangesNothing()
    {
        RenderSmoother smoother = new RenderSmoother(Origin);

        smoother.OnCorrected(Origin, Origin);

        Assert.That(smoother.PendingOffset, Is.EqualTo(0f));
        Assert.That(smoother.LargestCorrection, Is.EqualTo(0f));
    }

    [Test]
    public void OnCorrected_KeepsTheLargestCorrectionSeen()
    {
        RenderSmoother smoother = new RenderSmoother(Origin);

        smoother.OnCorrected(Origin, new WorldPosition(10.3f, 0f, 10f));
        smoother.OnCorrected(new WorldPosition(10.3f, 0f, 10f), new WorldPosition(10.4f, 0f, 10f));

        Assert.That(smoother.LastCorrection, Is.EqualTo(0.1f).Within(1e-5f));
        Assert.That(smoother.LargestCorrection, Is.EqualTo(0.3f).Within(1e-5f));
    }
}
}
