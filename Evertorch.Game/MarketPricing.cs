using System;

namespace Evertorch.Game
{
/// <summary>
///     The market board's price rules (Gameplay Systems §11.5), shared by both stores and the client's fee preview: a
///     price falls one step for each whole period its item had stock listed, never below the floor, and rises one step,
///     never past the ceiling, when a buyer takes the last unit it could, at most once a period. A step is a tenth of the
///     price, at least 1 coin.
/// </summary>
public static class MarketPricing
{
    public static int Step(int price)
    {
        return Math.Max(1, price / MarketTerms.StepDivisor);
    }

    /// <summary>
    ///     The state at <paramref name="at" />: the time since the last settle that the item had stock listed is added,
    ///     each whole period of it becomes a fall that stops at <paramref name="floor" />, and the remainder carries over.
    ///     A time before the last settle adds nothing and never moves it back.
    /// </summary>
    public static MarketPriceState Settle(MarketPriceState state, DateTime at, int floor, long periodMs)
    {
        if (periodMs < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(periodMs), "A period is at least 1 ms.");
        }

        long listed = state.ListedMs;
        if (state.StockUntil.HasValue)
        {
            DateTime end = at < state.StockUntil.Value ? at : state.StockUntil.Value;
            if (end > state.SettledAt)
            {
                listed += (end - state.SettledAt).Ticks / TimeSpan.TicksPerMillisecond;
            }
        }

        long falls = listed / periodMs;
        int price = state.Price;

        // Each fall is at least a coin, so the loop ends at the floor however long the item sat.
        for (long fall = 0; fall < falls && price > floor; fall++)
        {
            price = Math.Max(floor, price - Step(price));
        }

        return new MarketPriceState(
            price,
            listed - falls * periodMs,
            at > state.SettledAt ? at : state.SettledAt,
            state.StockUntil,
            state.RoseAt);
    }

    /// <summary>
    ///     The state after a purchase that took the last unit its buyer could take: one step up, capped at
    ///     <paramref name="ceiling" />, with the listed time cleared, unless the last rise is less than a period old.
    /// </summary>
    public static MarketPriceState Rise(MarketPriceState state, DateTime at, int ceiling, long periodMs)
    {
        if (state.RoseAt.HasValue && (at - state.RoseAt.Value).Ticks < periodMs * TimeSpan.TicksPerMillisecond)
        {
            return state;
        }

        return new MarketPriceState(
            Math.Min(ceiling, state.Price + Step(state.Price)),
            0,
            state.SettledAt,
            state.StockUntil,
            at);
    }

    /// <summary>
    ///     The fee to list <paramref name="units" /> at <paramref name="price" />: 1 % of their value, rounded down, at
    ///     least 1 coin.
    /// </summary>
    public static long Fee(long units, int price)
    {
        return Math.Max(MarketTerms.MinimumFee, units * price * MarketTerms.FeePercent / 100);
    }

    /// <summary>
    ///     What the board keeps of a listing's <paramref name="gross" /> sales: 9 %, rounded down once.
    /// </summary>
    public static long Tax(long gross)
    {
        return gross * MarketTerms.TaxPercent / 100;
    }

    /// <summary>
    ///     The floor an item's content leaves out: its sell price, and at least 1 coin, so nothing bought on the board
    ///     sells to a shop for more.
    /// </summary>
    public static int DefaultFloor(int sellPrice)
    {
        return Math.Max(1, sellPrice);
    }

    /// <summary>
    ///     The ceiling an item's content leaves out: ten times its base, but never so high that a whole stack would cost
    ///     more than the coin cap.
    /// </summary>
    public static int DefaultCeiling(int basePrice, int stackLimit)
    {
        return (int)Math.Min((long)MarketTerms.CeilingFactor * basePrice, ContentLimits.MaxCurrency / stackLimit);
    }
}
}
