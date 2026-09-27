using Calabonga.Microservices.BackgroundWorkers.Exceptions;

namespace Calabonga.Microservices.BackgroundWorkers;

/// <summary>
/// Validation and random interval calculation for <see cref="PeriodicHostedServiceBase"/>
/// </summary>
internal static class PeriodicInterval
{
    /// <summary>
    /// Throws <see cref="WorkerArgumentOutOfRangeException"/> when settings are invalid
    /// </summary>
    public static void Validate(PeriodType periodType, int minValue, int maxValue)
    {
        var unitMinutes = GetUnitMinutes(periodType);

        if (minValue < 1)
        {
            throw new WorkerArgumentOutOfRangeException($"MinValue must be greater than 0, but was {minValue}");
        }

        if (maxValue < minValue)
        {
            throw new WorkerArgumentOutOfRangeException($"MaxValue ({maxValue}) must be greater than or equal to MinValue ({minValue})");
        }

        if (maxValue > (int.MaxValue - 1) / unitMinutes)
        {
            throw new WorkerArgumentOutOfRangeException($"MaxValue ({maxValue} {periodType}) is too large");
        }
    }

    /// <summary>
    /// Returns random interval in [minValue; maxValue] (inclusive) with whole-minute precision.
    /// Settings must be validated by <see cref="Validate"/>
    /// </summary>
    public static TimeSpan Next(Random random, PeriodType periodType, int minValue, int maxValue)
    {
        var unitMinutes = GetUnitMinutes(periodType);
        var minutes = random.Next(minValue * unitMinutes, maxValue * unitMinutes + 1);
        return TimeSpan.FromMinutes(minutes);
    }

    private static int GetUnitMinutes(PeriodType periodType) => periodType switch
    {
        PeriodType.Minutes => 1,
        PeriodType.Hours => 60,
        _ => throw new WorkerArgumentOutOfRangeException($"Unknown PeriodType: {periodType}")
    };
}
