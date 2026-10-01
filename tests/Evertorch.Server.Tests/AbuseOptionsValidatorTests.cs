using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The ranges of the abuse keys (System Architecture §12).
/// </summary>
[TestFixture]
public sealed class AbuseOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(AbuseOptions options)
    {
        return new AbuseOptionsValidator().Validate(null, options);
    }

    [TestCase("PeerMessagesPerTick", 2)]
    [TestCase("PeerMessagesPerTick", 1001)]
    [TestCase("PeerMessageBurst", 0)]
    [TestCase("PeerMessageBurst", 100001)]
    [TestCase("ConnectionRequestsPerSecond", 0)]
    [TestCase("ConnectionRequestsPerSecond", 10001)]
    [TestCase("MaxConnectionsPerAddress", 0)]
    [TestCase("MaxConnectionsPerAddress", 10001)]
    [TestCase("MaxTrackedAddresses", 15)]
    [TestCase("MaxTrackedAddresses", 1000001)]
    [TestCase("CombatCommandsPerSecond", 0)]
    [TestCase("CombatCommandBurst", 10001)]
    [TestCase("PickupCommandsPerSecond", 1001)]
    [TestCase("PickupCommandBurst", 0)]
    [TestCase("ItemCommandsPerSecond", 0)]
    [TestCase("ItemCommandsPerSecond", 1001)]
    [TestCase("ItemCommandBurst", 0)]
    [TestCase("ItemCommandBurst", 10001)]
    [TestCase("SessionCommandsPerSecond", 0)]
    [TestCase("SessionCommandBurst", 10001)]
    [TestCase("ResyncRequestsPerSecond", 1001)]
    [TestCase("ResyncRequestBurst", 0)]
    [TestCase("ChatCommandsPerSecond", 0)]
    [TestCase("ChatCommandsPerSecond", 1001)]
    [TestCase("ChatCommandBurst", 0)]
    [TestCase("ChatCommandBurst", 10001)]
    [TestCase("ViolationThreshold", 0)]
    [TestCase("ViolationThreshold", 1000001)]
    [TestCase("ViolationDecayPerSecond", -1)]
    [TestCase("ViolationDecayPerSecond", 1000001)]
    [TestCase("KickCooldownMs", -1)]
    [TestCase("KickCooldownMs", 86400001)]
    [TestCase("SignInsPerSecond", 0)]
    [TestCase("SignInsPerSecond", 1001)]
    [TestCase("SignInBurst", 0)]
    [TestCase("SignInBurst", 10001)]
    [TestCase("SignInFailuresPerMinute", 0)]
    [TestCase("SignInFailuresPerMinute", 1001)]
    [TestCase("SignInFailureBurst", 0)]
    [TestCase("SignInFailureBurst", 1001)]
    [TestCase("MaxTrackedLogins", 15)]
    [TestCase("MaxTrackedLogins", 1000001)]
    public void Validate_WithAValueOutOfRange_FailsNamingTheKey(string key, int value)
    {
        var options = new AbuseOptions();
        typeof(AbuseOptions).GetProperty(key)!.SetValue(options, value);

        ValidateOptionsResult result = Validate(options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain($"Abuse:{key}"));
    }

    [TestCase("PeerMessagesPerTick", 3)]
    [TestCase("MaxTrackedAddresses", 16)]
    [TestCase("ViolationThreshold", 1)]
    [TestCase("ViolationThreshold", 1000000)]
    [TestCase("ViolationDecayPerSecond", 0)]
    [TestCase("ViolationDecayPerSecond", 1000000)]
    [TestCase("KickCooldownMs", 0)]
    [TestCase("KickCooldownMs", 86400000)]
    public void Validate_WithAValueAtItsBound_Succeeds(string key, int value)
    {
        var options = new AbuseOptions();
        typeof(AbuseOptions).GetProperty(key)!.SetValue(options, value);

        Assert.That(Validate(options).Succeeded, Is.True);
    }

    [Test]
    public void Validate_TheDefaults_Succeeds()
    {
        Assert.That(Validate(new AbuseOptions()).Succeeded, Is.True);
    }
}
}
