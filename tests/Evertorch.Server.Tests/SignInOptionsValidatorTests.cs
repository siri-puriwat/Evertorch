using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The ranges of the gateway, account, and token keys (System Architecture §12).
/// </summary>
[TestFixture]
public sealed class SignInOptionsValidatorTests
{
    [TestCase("not an address", 0, null, 4, "Gateway:BindAddress")]
    [TestCase("127.0.0.1", -1, null, 4, "Gateway:Port")]
    [TestCase("127.0.0.1", 65536, null, 4, "Gateway:Port")]
    [TestCase("127.0.0.1", 0, "bad host!", 4, "Gateway:PublicHost")]
    [TestCase("127.0.0.1", 0, null, 0, "Gateway:MaxConcurrentSignIns")]
    [TestCase("127.0.0.1", 0, null, 65, "Gateway:MaxConcurrentSignIns")]
    public void Gateway_WithAValueOutOfRange_FailsNamingTheKey(
        string address,
        int port,
        string? publicHost,
        int signIns,
        string key)
    {
        var options = new GatewayOptions
        {
            BindAddress = address,
            Port = port,
            PublicHost = publicHost,
            MaxConcurrentSignIns = signIns
        };

        ValidateOptionsResult result = new GatewayOptionsValidator().Validate(null, options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain(key));
    }

    [TestCase("0.0.0.0", 7443, "play.example.com", 1)]
    [TestCase("::1", 0, "192.168.1.20", 64)]
    [TestCase("127.0.0.1", 65535, null, 4)]
    public void Gateway_WithValuesInRange_Passes(string address, int port, string? publicHost, int signIns)
    {
        var options = new GatewayOptions
        {
            BindAddress = address,
            Port = port,
            PublicHost = publicHost,
            MaxConcurrentSignIns = signIns
        };

        Assert.That(new GatewayOptionsValidator().Validate(null, options).Succeeded, Is.True);
    }

    [TestCase(999, false)]
    [TestCase(1000, true)]
    [TestCase(10_000_000, true)]
    [TestCase(10_000_001, false)]
    public void PasswordIterations_AreCheckedAgainstTheirRange(int iterations, bool isValid)
    {
        ValidateOptionsResult result = new AccountOptionsValidator().Validate(
            null,
            new AccountOptions { PasswordIterations = iterations });

        Assert.That(result.Succeeded, Is.EqualTo(isValid));
        Assert.That(result.FailureMessage ?? "Accounts:PasswordIterations",
            Does.Contain("Accounts:PasswordIterations"));
    }

    [TestCase(59_999, false)]
    [TestCase(60_000, true)]
    [TestCase(86_400_000, true)]
    [TestCase(86_400_001, false)]
    public void TokenLifetime_IsCheckedAgainstItsRange(int lifetime, bool isValid)
    {
        ValidateOptionsResult result = new SessionOptionsValidator().Validate(
            null,
            new SessionOptions { TokenLifetimeMs = lifetime });

        Assert.That(result.Succeeded, Is.EqualTo(isValid));
    }

    [TestCase("localhost")]
    [TestCase("http://localhost/game")]
    [TestCase("ftp://localhost")]
    [TestCase("http://localhost:*/")]
    [TestCase("*")]
    public void Gateway_WithAnAllowedOriginThatIsNoOrigin_Fails(string origin)
    {
        var options = new GatewayOptions { AllowedOrigins = new[] { origin } };

        ValidateOptionsResult result = new GatewayOptionsValidator().Validate(null, options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("Gateway:AllowedOrigins"));
    }

    [TestCase("http://localhost:*")]
    [TestCase("https://play.example.com")]
    [TestCase("http://127.0.0.1:8000")]
    public void Gateway_WithAnOriginOrAnyPortOfAHost_Passes(string origin)
    {
        var options = new GatewayOptions { AllowedOrigins = new[] { origin } };

        Assert.That(new GatewayOptionsValidator().Validate(null, options).Succeeded, Is.True);
    }

    [Test]
    public void Defaults_AreTheSpecifiedOnes()
    {
        Assert.That(new AccountOptions().PasswordIterations, Is.EqualTo(600_000));
        Assert.That(new SessionOptions().TokenLifetimeMs, Is.EqualTo(900_000));
        Assert.That(new GatewayOptions().Enabled, Is.False, "off in code, on in appsettings.json");
        Assert.That(new GatewayOptions().Port, Is.Zero);
        Assert.That(new GatewayOptions().MaxConcurrentSignIns, Is.EqualTo(4));
        var abuse = new AbuseOptions();
        Assert.That(
            new[]
            {
                abuse.SignInsPerSecond, abuse.SignInBurst, abuse.SignInFailuresPerMinute, abuse.SignInFailureBurst,
                abuse.MaxTrackedLogins
            },
            Is.EqualTo(new[] { 2, 20, 2, 5, 10000 }));
    }

    [Test]
    public void Gateway_WithAPasswordAndEveryOtherKeyWrong_NeverRepeatsThePassword()
    {
        var options = new GatewayOptions
        {
            BindAddress = "nope",
            Port = -1,
            PublicHost = "bad host!",
            MaxConcurrentSignIns = 0,
            CertificatePath = "missing.pfx",
            CertificatePassword = "hunter2-pfx"
        };

        ValidateOptionsResult result = new GatewayOptionsValidator().Validate(null, options);

        Assert.That(result.FailureMessage, Does.Not.Contain("hunter2"));
    }

    // Network Protocol §4: the owner's server takes passwords only, and a shipped server serves the gateway.
    [Test]
    public void ShippedConfiguration_ServesTheGatewayAndNeverDevelopmentSignIn()
    {
        string server = Path.Combine(Path.GetDirectoryName(PackageFixture.RepositoryContentDirectory)!,
            "Evertorch.Server");

        using var shipped = JsonDocument.Parse(File.ReadAllText(Path.Combine(server, "appsettings.json")));
        using var development =
            JsonDocument.Parse(File.ReadAllText(Path.Combine(server, "appsettings.Development.json")));

        JsonElement gateway = shipped.RootElement.GetProperty("Gateway");
        Assert.That(gateway.GetProperty("Enabled").GetBoolean(), Is.True);
        Assert.That(gateway.GetProperty("Port").GetInt32(), Is.EqualTo(7443));
        Assert.That(shipped.RootElement.GetProperty("DevelopmentAuthentication").GetProperty("Enabled").GetBoolean(),
            Is.False);
        Assert.That(
            development.RootElement.GetProperty("DevelopmentAuthentication").GetProperty("Enabled").GetBoolean(),
            Is.False);
    }
}
}
