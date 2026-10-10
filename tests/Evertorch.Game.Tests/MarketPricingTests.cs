using System;
using NUnit.Framework;

namespace Evertorch.Game.Tests
{
/// <summary>
///     The board's price rules against the vectors of the market board research note (Gameplay Systems §11.5): Gloom
///     Dust sells for 14, so its base is 28, its floor 14, and its ceiling 280; a period is a day.
/// </summary>
[TestFixture]
public sealed class MarketPricingTests
{
    private const int Floor = 14;
    private const int Ceiling = 280;
    private const long Period = 86_400_000;

    private static readonly DateTime Start = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static DateTime After(double periods)
    {
        return Start.AddMilliseconds(periods * Period);
    }

    // Stock listed from the start until far beyond the test's times.
    private static MarketPriceState Listed(int price, long listedMs = 0, DateTime? roseAt = null)
    {
        return new MarketPriceState(price, listedMs, Start, After(1000), roseAt);
    }

    [TestCase(28, 2)]
    [TestCase(9, 1)]
    [TestCase(1, 1)]
    [TestCase(120, 12)]
    [TestCase(1200, 120)]
    public void Step_IsATenthRoundedDown_AndAtLeastACoin(int price, int step)
    {
        Assert.That(MarketPricing.Step(price), Is.EqualTo(step));
    }

    [TestCase(10L, 30, 3L)]
    [TestCase(1L, 28, 1L)]
    [TestCase(999L, 280, 2797L)]
    [TestCase(999L, 1_001_001, 9_999_999L)]
    public void Fee_IsAPercentOfTheValueRoundedDown_AndAtLeastACoin(long units, int price, long fee)
    {
        Assert.That(MarketPricing.Fee(units, price), Is.EqualTo(fee));
    }

    [TestCase(120L, 10L)]
    [TestCase(9L, 0L)]
    [TestCase(1_000_000_000L, 90_000_000L)]
    public void Tax_IsNinePercentRoundedDownOnce(long gross, long tax)
    {
        Assert.That(MarketPricing.Tax(gross), Is.EqualTo(tax));
    }

    [TestCase(14, 14)]
    [TestCase(0, 1)]
    public void DefaultFloor_IsTheSellPrice_AndAtLeastACoin(int sellPrice, int floor)
    {
        Assert.That(MarketPricing.DefaultFloor(sellPrice), Is.EqualTo(floor));
    }

    [TestCase(28, 999, 280)]
    [TestCase(120, 1, 1200)]
    [TestCase(200_000, 999, 1_001_001)]
    public void DefaultCeiling_IsTenTimesTheBase_WithinWhatAStackMayCost(int basePrice, int stackLimit, int ceiling)
    {
        Assert.That(MarketPricing.DefaultCeiling(basePrice, stackLimit), Is.EqualTo(ceiling));
    }

    [Test]
    public void Rise_LessThanAPeriodAfterTheLast_DoesNothing()
    {
        MarketPriceState risen = MarketPricing.Rise(Listed(26, 1000, After(-0.5)), Start, Ceiling, Period);

        Assert.That((risen.Price, risen.ListedMs), Is.EqualTo((26, 1000L)));
    }

    [Test]
    public void Rise_StepsUp_AndClearsTheListedTime()
    {
        MarketPriceState risen = MarketPricing.Rise(Listed(24, 1000, After(-2)), Start, Ceiling, Period);

        Assert.That((risen.Price, risen.ListedMs, risen.RoseAt), Is.EqualTo((26, 0L, (DateTime?)Start)));
    }

    [Test]
    public void Rise_StopsAtTheCeiling()
    {
        Assert.That(MarketPricing.Rise(Listed(275), Start, Ceiling, Period).Price, Is.EqualTo(Ceiling));
        Assert.That(MarketPricing.Rise(Listed(Ceiling), Start, Ceiling, Period).Price, Is.EqualTo(Ceiling));
    }

    [Test]
    public void Settle_AfterTheStockRanOut_Pauses()
    {
        var state = new MarketPriceState(28, 0, Start, After(0.5), null);

        MarketPriceState settled = MarketPricing.Settle(state, After(5), Floor, Period);

        Assert.That(settled.Price, Is.EqualTo(28));
        Assert.That(settled.ListedMs, Is.EqualTo(Period / 2));
    }

    [Test]
    public void Settle_AtAnEarlierTime_NeverMovesBack()
    {
        MarketPriceState settled = MarketPricing.Settle(Listed(28), Start.AddHours(-1), Floor, Period);

        Assert.That(settled.SettledAt, Is.EqualTo(Start));
        Assert.That((settled.Price, settled.ListedMs), Is.EqualTo((28, 0L)));
    }

    [Test]
    public void Settle_FallsOnceAWholePeriod_AndCarriesTheRest()
    {
        MarketPriceState settled = MarketPricing.Settle(Listed(28), After(2.5), Floor, Period);

        Assert.That(settled.Price, Is.EqualTo(24));
        Assert.That(settled.ListedMs, Is.EqualTo(Period / 2));
        Assert.That(settled.SettledAt, Is.EqualTo(After(2.5)));
    }

    [Test]
    public void Settle_FallsToTheFloor_AndHoldsThere()
    {
        MarketPriceState settled = MarketPricing.Settle(Listed(24, Period / 2), After(10), Floor, Period);

        Assert.That(settled.Price, Is.EqualTo(Floor));
        Assert.That(settled.ListedMs, Is.EqualTo(Period / 2));
    }

    [Test]
    public void Settle_InSteps_EndsWhereOneSettleDoes()
    {
        MarketPriceState stepped = Listed(28);
        foreach (int hours in new[] { 7, 26, 46, 60, 114, 173 })
        {
            stepped = MarketPricing.Settle(stepped, Start.AddHours(hours), Floor, Period);
        }

        MarketPriceState once = MarketPricing.Settle(Listed(28), Start.AddHours(173), Floor, Period);

        Assert.That((stepped.Price, stepped.ListedMs), Is.EqualTo((once.Price, once.ListedMs)));
    }

    [Test]
    public void Settle_LongAfterTheLastTime_EndsAtTheFloor()
    {
        MarketPriceState settled = MarketPricing.Settle(Listed(1_000_000), After(999), 60, Period);

        Assert.That(settled.Price, Is.EqualTo(60));
    }

    [Test]
    public void Settle_WithNoStock_CountsNothing()
    {
        var state = new MarketPriceState(28, Period / 2, Start, null, null);

        MarketPriceState settled = MarketPricing.Settle(state, After(30), Floor, Period);

        Assert.That((settled.Price, settled.ListedMs), Is.EqualTo((28, Period / 2)));
    }
}
}
