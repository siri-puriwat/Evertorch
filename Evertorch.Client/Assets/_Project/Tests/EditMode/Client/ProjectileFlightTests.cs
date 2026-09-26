using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     A projectile lands at the server's impact or resolution and flies for at most 0.4 s before it (Gameplay Systems
///     §8).
/// </summary>
[TestFixture]
public sealed class ProjectileFlightTests
{
    // start, impact => launch: the last 0.4 s, or all of a shorter attack.
    [TestCase(10.0, 10.9, 10.5)]
    [TestCase(10.0, 11.5, 11.1)]
    [TestCase(10.0, 10.2, 10.0)]
    public void Launch_IsTheLastFourTenthsOfASecondBeforeTheImpact_OrTheStart(
        double start,
        double impact,
        double launch)
    {
        var flight = new ProjectileFlight(start, impact);

        Assert.That(flight.LaunchSeconds, Is.EqualTo(launch).Within(1e-9));
        Assert.That(flight.ImpactSeconds, Is.EqualTo(impact));
    }

    [Test]
    public void Flight_OfNoTime_NeverShows()
    {
        var flight = new ProjectileFlight(10.0, 10.0);

        Assert.That(flight.TryGetProgress(10.0, out float _), Is.False);
    }

    [Test]
    public void Progress_RunsFromTheLaunchToTheImpact_AndIsFalseOutsideIt()
    {
        var flight = new ProjectileFlight(10.0, 10.9);

        bool isBefore = flight.TryGetProgress(10.49, out float _);
        bool isAtLaunch = flight.TryGetProgress(10.5, out float atLaunch);
        bool isHalfway = flight.TryGetProgress(10.7, out float halfway);
        bool isAtImpact = flight.TryGetProgress(10.9, out float _);

        Assert.That(isBefore, Is.False);
        Assert.That((isAtLaunch, atLaunch), Is.EqualTo((true, 0f)));
        Assert.That(isHalfway, Is.True);
        Assert.That(halfway, Is.EqualTo(0.5f).Within(1e-4f));
        Assert.That(isAtImpact, Is.False, "it has landed");
    }
}
}
