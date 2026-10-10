using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The ranges of the market board's periods (System Architecture §12).
/// </summary>
[TestFixture]
public sealed class MarketOptionsValidatorTests
{
    [TestCase(999, 259_200_000, "Market:PricePeriodMs")]
    [TestCase(604_800_001, 259_200_000, "Market:PricePeriodMs")]
    [TestCase(86_400_000, 999, "Market:ListingDurationMs")]
    [TestCase(86_400_000, 1_209_600_001, "Market:ListingDurationMs")]
    public void Market_WithAPeriodOutOfRange_FailsNamingTheKey(int pricePeriod, int duration, string key)
    {
        var options = new MarketOptions { PricePeriodMs = pricePeriod, ListingDurationMs = duration };

        ValidateOptionsResult result = new MarketOptionsValidator().Validate(null, options);

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain(key));
    }

    [TestCase(1000, 1000)]
    [TestCase(86_400_000, 259_200_000)]
    [TestCase(604_800_000, 1_209_600_000)]
    public void Market_WithPeriodsInRange_Passes(int pricePeriod, int duration)
    {
        var options = new MarketOptions { PricePeriodMs = pricePeriod, ListingDurationMs = duration };

        Assert.That(new MarketOptionsValidator().Validate(null, options).Succeeded, Is.True);
    }

    [Test]
    public void Market_ByDefault_IsADayAndThreeDays()
    {
        var options = new MarketOptions();

        Assert.That((options.PricePeriodMs, options.ListingDurationMs), Is.EqualTo((86_400_000, 259_200_000)));
    }
}
}
