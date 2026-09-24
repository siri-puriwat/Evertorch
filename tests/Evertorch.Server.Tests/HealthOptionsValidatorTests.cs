using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class HealthOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(HealthOptions options)
    {
        return new HealthOptionsValidator().Validate(null, options);
    }

    [TestCase("not-an-address", 0, 5000, "BindAddress")]
    [TestCase("127.0.0.1", -1, 5000, "Port")]
    [TestCase("127.0.0.1", 65536, 5000, "Port")]
    [TestCase("127.0.0.1", 0, 1999, "LivenessStallMs")]
    [TestCase("127.0.0.1", 0, 600001, "LivenessStallMs")]
    public void Validate_WithAValueOutOfRange_FailsNamingTheKey(
        string bindAddress,
        int port,
        int stallMs,
        string key)
    {
        var options = new HealthOptions { BindAddress = bindAddress, Port = port, LivenessStallMs = stallMs };

        ValidateOptionsResult result = Validate(options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain($"Health:{key}"));
    }

    [TestCase("127.0.0.1", 0, 2000)]
    [TestCase("0.0.0.0", 65535, 600000)]
    [TestCase("::1", 7778, 5000)]
    public void Validate_WithValuesInRange_Succeeds(string bindAddress, int port, int stallMs)
    {
        var options = new HealthOptions { BindAddress = bindAddress, Port = port, LivenessStallMs = stallMs };

        Assert.That(Validate(options).Succeeded, Is.True);
    }

    [Test]
    public void Validate_TheDefaults_Succeeds()
    {
        Assert.That(Validate(new HealthOptions()).Succeeded, Is.True);
    }
}
}
