namespace PortfolioApp.Services;

/// <summary>
/// Deterministic technical-indicator math over a price history.
/// </summary>
/// <remarks>
/// Pure functions with no storage or clock dependency, so rule evaluation stays reproducible:
/// the same inputs must always yield the same signal. Every method returns <c>null</c> rather
/// than guessing when there is not enough history, which lets the rule engine skip a rule
/// instead of emitting a recommendation built on incomplete data.
/// <para>
/// All methods take prices ordered oldest to newest.
/// </para>
/// </remarks>
public static class IndicatorCalculator
{
    /// <summary>Simple moving average of the most recent <paramref name="period"/> prices.</summary>
    public static decimal? SimpleMovingAverage(IReadOnlyList<decimal> prices, int period)
    {
        ArgumentNullException.ThrowIfNull(prices);

        if (period < 1 || prices.Count < period)
        {
            return null;
        }

        decimal sum = 0m;
        for (var i = prices.Count - period; i < prices.Count; i++)
        {
            sum += prices[i];
        }

        return sum / period;
    }

    /// <summary>
    /// Wilder's Relative Strength Index. Needs <paramref name="period"/> + 1 prices because the
    /// first value is a price *change*, not a price.
    /// </summary>
    public static decimal? RelativeStrengthIndex(IReadOnlyList<decimal> prices, int period)
    {
        ArgumentNullException.ThrowIfNull(prices);

        if (period < 2 || prices.Count < period + 1)
        {
            return null;
        }

        decimal gainSum = 0m;
        decimal lossSum = 0m;

        for (var i = 1; i <= period; i++)
        {
            var change = prices[i] - prices[i - 1];
            if (change > 0m)
            {
                gainSum += change;
            }
            else
            {
                lossSum -= change;
            }
        }

        var averageGain = gainSum / period;
        var averageLoss = lossSum / period;

        // Wilder smoothing across the remainder of the series.
        for (var i = period + 1; i < prices.Count; i++)
        {
            var change = prices[i] - prices[i - 1];
            var gain = change > 0m ? change : 0m;
            var loss = change < 0m ? -change : 0m;

            averageGain = ((averageGain * (period - 1)) + gain) / period;
            averageLoss = ((averageLoss * (period - 1)) + loss) / period;
        }

        if (averageLoss == 0m)
        {
            // A perfectly flat series has no strength in either direction, so report neutral
            // rather than the maximum an unguarded division would produce.
            return averageGain == 0m ? 50m : 100m;
        }

        var relativeStrength = averageGain / averageLoss;
        return 100m - (100m / (1m + relativeStrength));
    }

    /// <summary>Percentage change from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static decimal? PercentChange(decimal? from, decimal? to)
    {
        if (from is null or 0m || to is null)
        {
            return null;
        }

        return (to.Value - from.Value) / Math.Abs(from.Value) * 100m;
    }

    /// <summary>A position's share of total portfolio value, as a percentage.</summary>
    public static decimal? AllocationPercent(decimal? positionValue, decimal totalPortfolioValue)
    {
        if (positionValue is null || totalPortfolioValue <= 0m)
        {
            return null;
        }

        return positionValue.Value / totalPortfolioValue * 100m;
    }
}
