using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class PersistenceOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(PersistenceOptions options)
    {
        return new PersistenceOptionsValidator().Validate(null, options);
    }

    [TestCase("QueueCapacity", 0)]
    [TestCase("QueueCapacity", 100001)]
    [TestCase("CommandTimeoutMs", 99)]
    [TestCase("CommandTimeoutMs", 60001)]
    [TestCase("MaxRetries", -1)]
    [TestCase("MaxRetries", 11)]
    [TestCase("RetryBaseDelayMs", 0)]
    [TestCase("RetryBaseDelayMs", 10001)]
    [TestCase("CheckpointIntervalMs", 999)]
    [TestCase("CheckpointIntervalMs", 3600001)]
    public void Validate_WithAValueOutOfRange_FailsNamingTheKey(string key, int value)
    {
        var options = new PersistenceOptions();
        typeof(PersistenceOptions).GetProperty(key)!.SetValue(options, value);

        ValidateOptionsResult result = Validate(options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain($"Persistence:{key}"));
    }

    [TestCase("QueueCapacity", 1)]
    [TestCase("QueueCapacity", 100000)]
    [TestCase("CommandTimeoutMs", 100)]
    [TestCase("CommandTimeoutMs", 60000)]
    [TestCase("MaxRetries", 0)]
    [TestCase("MaxRetries", 10)]
    [TestCase("RetryBaseDelayMs", 1)]
    [TestCase("RetryBaseDelayMs", 10000)]
    [TestCase("CheckpointIntervalMs", 1000)]
    [TestCase("CheckpointIntervalMs", 3600000)]
    public void Validate_WithAValueAtItsBound_Succeeds(string key, int value)
    {
        var options = new PersistenceOptions();
        typeof(PersistenceOptions).GetProperty(key)!.SetValue(options, value);

        Assert.That(Validate(options).Succeeded, Is.True);
    }

    [Test]
    public void Validate_WithDefaults_MatchesTheSpecification()
    {
        var options = new PersistenceOptions();

        Assert.That(Validate(options).Succeeded, Is.True);
        Assert.That(
            new[]
            {
                options.QueueCapacity,
                options.CommandTimeoutMs,
                options.MaxRetries,
                options.RetryBaseDelayMs,
                options.CheckpointIntervalMs
            },
            Is.EqualTo(new[] { 256, 5000, 3, 100, 60000 }));
    }
}
}
