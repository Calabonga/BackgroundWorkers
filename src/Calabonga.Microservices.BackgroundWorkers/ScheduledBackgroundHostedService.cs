using Calabonga.Microservices.BackgroundWorkers.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NCrontab;

namespace Calabonga.Microservices.BackgroundWorkers;

/// <summary>
/// Scheduled and Scoped Background Service with CronTab functionality.
/// Schedule is evaluated in UTC.
/// * * * * * *
/// | | | | | |
/// | | | | | +--- day of week (0 - 6) (Sunday=0)
/// | | | | +----- month (1 - 12)
/// | | | +------- day of month (1 - 31)
/// | | +--------- hour (0 - 23)
/// | +----------- min (0 - 59)
/// +------------- sec (0 - 59)
/// </summary>
public abstract class ScheduledHostedServiceBase : ScopedHostedServiceBase
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);
    private CrontabSchedule? _schedule;

    protected abstract string Schedule { get; }

    protected ScheduledHostedServiceBase(
        IServiceScopeFactory serviceScopeFactory,
        ILogger logger)
        : this(serviceScopeFactory, logger, TimeProvider.System)
    {
    }

    /// <summary>
    /// Creates service with custom <see cref="System.TimeProvider"/> (for example, a fake one in tests)
    /// </summary>
    protected ScheduledHostedServiceBase(
        IServiceScopeFactory serviceScopeFactory,
        ILogger logger,
        TimeProvider timeProvider)
        : base(serviceScopeFactory, logger, timeProvider)
    {
        GetSchedule();
    }

    #region Properties

    /// <summary>
    /// Indicates that hosted service should start process on server restart
    /// </summary>
    protected virtual bool IsExecuteOnServerRestart => false;

    /// <summary>
    /// Identify service by name
    /// </summary>
    protected abstract string DisplayName { get; }

    /// <summary>
    /// Next Run information (UTC) calculated by Cron schedule
    /// </summary>
    public DateTime NextRun { get; private set; }

    /// <summary>
    /// ParseOptions for Cron schedule
    /// </summary>
    protected virtual bool IncludingSeconds => false;

    /// <summary>
    /// Use 5 seconds delay before the first schedule check.
    /// The schedule is checked every 5 seconds regardless of this value.
    /// It can be helpful when you need start in DEBUG mode your application and want that scheduler starts too <see cref="IsExecuteOnServerRestart"/>
    /// </summary>
    protected virtual bool IsDelayBeforeStart { get;  } = true;
    #endregion


    private void GetSchedule()
    {
        if (string.IsNullOrEmpty(Schedule))
        {
            throw new WorkerArgumentNullException(nameof(Schedule));
        }

        _schedule = CrontabSchedule.Parse(Schedule, new CrontabSchedule.ParseOptions { IncludingSeconds = IncludingSeconds });
        var currentDateTime = TimeProvider.GetUtcNow().UtcDateTime;
        if (IsExecuteOnServerRestart)
        {
            NextRun = currentDateTime.AddSeconds(5);
            Logger.LogInformation($"{DisplayName} ({nameof(IsExecuteOnServerRestart)} = {IsExecuteOnServerRestart})");
        }
        else
        {
            NextRun = _schedule.GetNextOccurrence(currentDateTime);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        if (IsDelayBeforeStart)
        {
            await TimeProvider.Delay(CheckInterval, token).ConfigureAwait(false);
        }

        do
        {
            var now = TimeProvider.GetUtcNow().UtcDateTime;
            if (now > NextRun)
            {
                NextRun = _schedule!.GetNextOccurrence(now);
                await ProcessAsync(token);
            }

            await TimeProvider.Delay(CheckInterval, token).ConfigureAwait(false);
        }
        while (!token.IsCancellationRequested);
    }
}
