using System;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The camera's orbit (Prototype Content §3): today's view by default, and every change kept within the configured
///     limits.
/// </summary>
public sealed class OrbitCameraStateTests
{
    private const float Tolerance = 1e-4f;

    private static OrbitCameraState CreateDefault()
    {
        return new OrbitCameraState(90f, 30f, 60f, 6f, 20f);
    }

    [TestCase(-1f, 30f, 60f, 6f, 20f)]
    [TestCase(181f, 30f, 60f, 6f, 20f)]
    [TestCase(90f, 0f, 60f, 6f, 20f)]
    [TestCase(90f, 61f, 60f, 6f, 20f)]
    [TestCase(90f, 30f, 90f, 6f, 20f)]
    [TestCase(90f, 30f, 60f, 0f, 20f)]
    [TestCase(90f, 30f, 60f, 21f, 20f)]
    [TestCase(90f, 30f, 60f, 6f, float.PositiveInfinity)]
    public void Constructor_WithLimitsThatCannotHold_Throws(
        float maxYaw,
        float minPitch,
        float maxPitch,
        float minDistance,
        float maxDistance)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OrbitCameraState(maxYaw, minPitch, maxPitch, minDistance, maxDistance));
    }

    [Test]
    public void Default_IsTheFixedViewTheMapsWereFirstPlayedWith()
    {
        OrbitCameraState state = CreateDefault();

        state.GetOffset(out float x, out float y, out float z);

        Assert.That(state.YawDegrees, Is.EqualTo(0f));
        Assert.That(x, Is.EqualTo(0f).Within(Tolerance));
        Assert.That(y, Is.EqualTo(15f).Within(Tolerance));
        Assert.That(z, Is.EqualTo(-11f).Within(Tolerance));
    }

    [Test]
    public void Offset_AtYawNinety_PutsTheCameraWestOfThePlayer()
    {
        OrbitCameraState state = CreateDefault();

        state.Orbit(90f, 0f);
        state.GetOffset(out float x, out float y, out float z);

        Assert.That(x, Is.EqualTo(-11f).Within(Tolerance));
        Assert.That(y, Is.EqualTo(15f).Within(Tolerance));
        Assert.That(z, Is.EqualTo(0f).Within(Tolerance));
    }

    [Test]
    public void Orbit_StaysWithinTheLimits()
    {
        OrbitCameraState state = CreateDefault();

        state.Orbit(500f, 500f);
        Assert.That(state.YawDegrees, Is.EqualTo(90f));
        Assert.That(state.PitchDegrees, Is.EqualTo(60f));

        state.Orbit(-1000f, -1000f);
        Assert.That(state.YawDegrees, Is.EqualTo(-90f));
        Assert.That(state.PitchDegrees, Is.EqualTo(30f));
    }

    [Test]
    public void Orbit_WithOtherLimits_ClampsToThoseLimits()
    {
        var state = new OrbitCameraState(45f, 40f, 50f, 8f, 10f);

        state.Orbit(100f, 100f);

        Assert.That(state.YawDegrees, Is.EqualTo(45f));
        Assert.That(state.PitchDegrees, Is.EqualTo(50f));
        Assert.That(state.Distance, Is.EqualTo(10f), "the default distance is brought within the limits");
    }

    [Test]
    public void Orbit_WithNaN_ChangesNothing()
    {
        OrbitCameraState state = CreateDefault();

        state.Orbit(float.NaN, float.NaN);

        Assert.That(state.YawDegrees, Is.EqualTo(0f));
        Assert.That(state.PitchDegrees, Is.EqualTo(OrbitCameraState.DefaultPitchDegrees));
    }

    [Test]
    public void ZoomAndPinch_StayWithinTheDistanceLimits()
    {
        OrbitCameraState state = CreateDefault();

        state.Zoom(-3f);
        Assert.That(state.Distance, Is.EqualTo(OrbitCameraState.DefaultDistance - 3f).Within(Tolerance));
        state.Zoom(-100f);
        Assert.That(state.Distance, Is.EqualTo(6f));
        state.ScaleDistance(10f);
        Assert.That(state.Distance, Is.EqualTo(20f));
        state.ScaleDistance(0.5f);
        Assert.That(state.Distance, Is.EqualTo(10f));
        state.ScaleDistance(0f);
        Assert.That(state.Distance, Is.EqualTo(10f), "a pinch of nothing changes nothing");
    }

    [Test]
    public void StepZoom_GoesInThroughThreeDistancesAndBackOut()
    {
        OrbitCameraState state = CreateDefault();

        state.StepZoom();
        Assert.That(state.Distance, Is.EqualTo(12f));
        state.StepZoom();
        Assert.That(state.Distance, Is.EqualTo(6f));
        state.StepZoom();
        Assert.That(state.Distance, Is.EqualTo(OrbitCameraState.DefaultDistance));

        state.Zoom(-4f);
        state.StepZoom();
        Assert.That(state.Distance, Is.EqualTo(12f), "from between two steps, the next nearer one");
    }
}
}
