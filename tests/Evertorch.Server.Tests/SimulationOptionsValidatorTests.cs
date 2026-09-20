using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class SimulationOptionsValidatorTests
{
    [Test]
    public void Validate_WithDefaults_SucceedsAtTwentyHertz()
    {
        SimulationOptions options = new SimulationOptions();

        ValidateOptionsResult result = new SimulationOptionsValidator().Validate(null, options);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(options.TickRate, Is.EqualTo(20));
    }

    [TestCase(1)]
    [TestCase(120)]
    public void Validate_WithTickRateAtBoundary_Succeeds(int tickRate)
    {
        SimulationOptions options = new SimulationOptions { TickRate = tickRate };

        ValidateOptionsResult result = new SimulationOptionsValidator().Validate(null, options);

        Assert.That(result.Succeeded, Is.True);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(121)]
    public void Validate_WithTickRateOutOfRange_FailsNamingTheKey(int tickRate)
    {
        SimulationOptions options = new SimulationOptions { TickRate = tickRate };

        ValidateOptionsResult result = new SimulationOptionsValidator().Validate(null, options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("Simulation:TickRate"));
    }

    [TestCase(0)]
    [TestCase(101)]
    public void Validate_WithMaxCatchUpTicksOutOfRange_FailsNamingTheKey(int maxCatchUpTicks)
    {
        SimulationOptions options = new SimulationOptions { MaxCatchUpTicks = maxCatchUpTicks };

        ValidateOptionsResult result = new SimulationOptionsValidator().Validate(null, options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("Simulation:MaxCatchUpTicks"));
    }
}
}
