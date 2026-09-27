using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Calabonga.Microservices.BackgroundWorkers;

/// <summary>
/// Scoped Background Service that runs periodically with a random interval.
/// After each run the next interval is chosen randomly in [<see cref="MinValue"/>; <see cref="MaxValue"/>]
/// (inclusive, whole minutes) of <see cref="PeriodType"/> units and counted from the start of the run.
/// Settings are read when the worker starts (not in the constructor), so they can come from options
/// injected into the derived class. Time is in UTC.
/// </summary>
public abstract class PeriodicHostedServiceBase : ScopedHostedServiceBase
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);
    private readonly Random _random;

    protected PeriodicHostedServiceBase(
        IServiceScopeFactory serviceScopeFactory,
        ILogger logger)
        : this(serviceScopeFactory, logger, TimeProvider.System)
    {
    }

    /// <summary>
    /// Creates service with custom <see cref="System.TimeProvider"/> (for example, a fake one in tests)
    /// </summary>
    protected PeriodicHostedServiceBase(
        IServiceScopeFactory serviceScopeFactory,
        ILogger logger,
        TimeProvider timeProvider)
        : this(serviceScopeFactory, logger, timeProvider, new Random())
    {
    }

    /// <summary>
    /// Creates service with custom <see cref="System.TimeProvider"/> and <see cref="System.Random"/>
    /// (for example, predictable ones in tests)
    /// </summary>
    protected PeriodicHostedServiceBase(
        IServiceScopeFactory serviceScopeFactory,
        ILogger logger,
        TimeProvider timeProvider,
        Random random)
        : base(serviceScopeFactory, logger, timeProvider)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    #region Properties

    /// <summary>
    /// Unit of <see cref="MinValue"/> and <see cref="MaxValue"/>
    /// </summary>
    protected abstract PeriodType PeriodType { get; }

    /// <summary>
    /// Minimal interval between runs (inclusive), must be greater than 0
    /// </summary>
    protected abstract int MinValue { get; }

    /// <summary>
    /// Maximal interval between runs (inclusive), must be greater than or equal to <see cref="MinValue"/>
    /// </summary>
    protected abstract int MaxValue { get; }

    /// <summary>
    /// Identify service by name
    /// </summary>
    protected abstract string DisplayName { get; }

    /// <summary>
    /// Indicates that hosted service should start process 5 seconds after start instead of waiting for the first interval
    /// </summary>
    protected virtual bool IsExecuteOnServerRestart => false;

    /// <summary>
    /// Use 5 seconds delay before the first check.
    /// The next run is checked every 5 seconds regardless of this value.
    /// </summary>
    protected virtual bool IsDelayBeforeStart => true;

    /// <summary>
    /// Next Run information (UTC). Calculated when the worker starts and after each run
    /// </summary>
    public DateTime NextRun { get; private set; }

    #endregion

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var periodType = PeriodType;
        var minValue = MinValue;
        var maxValue = MaxValue;
        PeriodicInterval.Validate(periodType, minValue, maxValue);

        var startedAt = TimeProvider.GetUtcNow().UtcDateTime;
        NextRun = IsExecuteOnServerRestart
            ? startedAt.Add(CheckInterval)
            : startedAt.Add(PeriodicInterval.Next(_random, periodType, minValue, maxValue));

        Logger.LogInformation(
            "{DisplayName} started: runs every {MinValue}-{MaxValue} {PeriodType}, next run at {NextRun:O}",
            DisplayName, minValue, maxValue, periodType, NextRun);

        if (IsDelayBeforeStart)
        {
            await TimeProvider.Delay(CheckInterval, token).ConfigureAwait(false);
        }

        do
        {
            var now = TimeProvider.GetUtcNow().UtcDateTime;
            if (now > NextRun)
            {
                NextRun = now.Add(PeriodicInterval.Next(_random, periodType, minValue, maxValue));
                await ProcessAsync(token).ConfigureAwait(false);
            }

            await TimeProvider.Delay(CheckInterval, token).ConfigureAwait(false);
        }
        while (!token.IsCancellationRequested);
    }
}
