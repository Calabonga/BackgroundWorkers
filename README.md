# BackgroundWorkers

Background Workers for Microservices on ASP.NET Core. Contains Scoped, Scheduled and Periodic workers

# Installation and sample

You can install from nuget-package
[Calabonga.Microservices.BackgroundWorkers](https://www.nuget.org/packages/Calabonga.Microservices.BackgroundWorkers/). Also you can see the using sample on the [github](https://github.com/Calabonga/BackgroundWorker)

```bash
dotnet add package Calabonga.Microservices.BackgroundWorkers
```

# How it works

**Scoped worker.** `ScopedHostedServiceBase` is an `IHostedService` that runs your code in an endless loop with a 5-second pause between iterations until the application stops. Every iteration gets its own DI scope, so you can safely resolve scoped services such as `DbContext` from the `serviceProvider` argument. An exception thrown inside an iteration is logged with the `ILogger` you passed to the constructor, and the worker keeps running; only cancellation on application shutdown stops the loop.

```csharp
public sealed class CleanupWorker : ScopedHostedServiceBase
{
    public CleanupWorker(IServiceScopeFactory serviceScopeFactory, ILogger<CleanupWorker> logger)
        : base(serviceScopeFactory, logger) { }

    protected override async Task ProcessInScopeAsync(IServiceProvider serviceProvider, CancellationToken token)
    {
        var repository = serviceProvider.GetRequiredService<IOrderRepository>();
        await repository.DeleteExpiredAsync(token);
    }
}
```

**Scheduled worker.** `ScheduledHostedServiceBase` adds a CronTab schedule on top of the scoped worker: it checks the schedule every 5 seconds and calls `ProcessInScopeAsync` when the next occurrence has come. Provide the cron expression in `Schedule` and a name in `DisplayName`. Set `IncludingSeconds` to `true` to use the 6-field format with seconds, and `IsExecuteOnServerRestart` to `true` to run once 5 seconds after the application starts. Since version 3.0.0 the schedule and the public `NextRun` property are in **UTC**, not in the server's local time. Missed occurrences are not replayed, and the schedule accuracy is about 5 seconds. The schedule is parsed in the base constructor, so overridden properties must return constant values and must not depend on fields of your class.

```csharp
public sealed class DailyReportWorker : ScheduledHostedServiceBase
{
    public DailyReportWorker(IServiceScopeFactory serviceScopeFactory, ILogger<DailyReportWorker> logger)
        : base(serviceScopeFactory, logger) { }

    // every day at 03:00 UTC
    protected override string Schedule => "0 3 * * *";

    protected override string DisplayName => "Daily report";

    protected override bool IsExecuteOnServerRestart => true;

    protected override async Task ProcessInScopeAsync(IServiceProvider serviceProvider, CancellationToken token)
    {
        var reports = serviceProvider.GetRequiredService<IReportService>();
        await reports.BuildDailyReportAsync(token);
    }
}
```

**Days of the week.** The fifth cron field is the day of the week: `0` = Sunday (`7` is not supported, use `0` or `SUN`), `1` = Monday, `2` = Tuesday, `3` = Wednesday, `4` = Thursday, `5` = Friday, `6` = Saturday; names (`MON`, `THU`) and lists/ranges (`1,4`, `1-5`) also work. **The schedule is evaluated in UTC**, so the day and hour are UTC too. `1 0 * * 1,4` runs on Monday and Thursday at 00:01 *UTC*; on a server whose local time is behind UTC (for example, UTC-5) that moment is still Sunday 19:01 / Wednesday 19:01 local time, which looks like "the worker ran on the wrong day". To run at a local time, convert the whole moment (hour **and** day of the week) to UTC. For example, Monday and Thursday at 00:01 in Moscow (UTC+3) are Sunday and Wednesday at 21:01 UTC:

```csharp
public sealed class TwiceAWeekWorker : ScheduledHostedServiceBase
{
    public TwiceAWeekWorker(IServiceScopeFactory serviceScopeFactory, ILogger<TwiceAWeekWorker> logger)
        : base(serviceScopeFactory, logger) { }

    // Monday and Thursday at 00:01 UTC
    protected override string Schedule => "1 0 * * 1,4";

    // the same with day names
    // protected override string Schedule => "1 0 * * MON,THU";

    // Monday and Thursday at 00:01 in UTC+3 (= Sunday and Wednesday at 21:01 UTC)
    // protected override string Schedule => "1 21 * * 0,3";

    protected override string DisplayName => "Twice a week";

    protected override async Task ProcessInScopeAsync(IServiceProvider serviceProvider, CancellationToken token)
    {
        // ...
    }
}
```

**Periodic worker.** `PeriodicHostedServiceBase` (since version 3.2.0) runs your code repeatedly with a random interval instead of a fixed schedule. Set the unit in `PeriodType` (`Minutes` or `Hours`) and the bounds in `MinValue` and `MaxValue`: after each run the next interval is chosen randomly between them, inclusive, with whole-minute precision, and is counted from the start of the run. For example, 3–4 hours gives intervals like 3:00, 3:07, 3:59 or 4:00, and 10–15 minutes gives 10, 11, …, 15 minutes. Unlike the scheduled worker, the settings are read when the worker starts, not in the constructor, so they can come from options injected into your class; invalid values (`MinValue` less than 1, `MaxValue` less than `MinValue`) throw `WorkerArgumentOutOfRangeException` and stop the host from starting. `IsExecuteOnServerRestart`, `IsDelayBeforeStart` and `NextRun` work the same way as in the scheduled worker.

```json
// appsettings.json
{
  "DatabaseReview": {
    "MinHours": 3,
    "MaxHours": 4
  }
}
```

```csharp
public sealed class ReviewOptions
{
    public int MinHours { get; set; } = 3;

    public int MaxHours { get; set; } = 4;
}

public sealed class DatabaseReviewWorker : PeriodicHostedServiceBase
{
    private readonly ReviewOptions _options;

    public DatabaseReviewWorker(IServiceScopeFactory serviceScopeFactory, ILogger<DatabaseReviewWorker> logger, IOptions<ReviewOptions> options)
        : base(serviceScopeFactory, logger)
    {
        _options = options.Value;
    }

    // every 3-4 hours, values from the "DatabaseReview" section of appsettings.json
    protected override PeriodType PeriodType => PeriodType.Hours;

    protected override int MinValue => _options.MinHours;

    protected override int MaxValue => _options.MaxHours;

    protected override string DisplayName => "Database review";

    protected override bool IsExecuteOnServerRestart => true;

    protected override async Task ProcessInScopeAsync(IServiceProvider serviceProvider, CancellationToken token)
    {
        var viewer = serviceProvider.GetRequiredService<IDatabaseViewer>();
        await viewer.CheckDatabaseAsync(token);
    }
}
```

**Registration.** Workers are ordinary hosted services, so register them with `AddHostedService<T>()`: the host starts them together with the application and cancels them on shutdown. The library targets `netstandard2.1` and depends only on `Microsoft.Extensions.*.Abstractions`, `Microsoft.Bcl.TimeProvider` and `ncrontab`, so it works with any .NET host, not only ASP.NET Core. For a custom loop you can inherit `HostedServiceBase` directly and override `ProcessAsync` (and `ExecuteAsync` to change the timing).

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHostedService<CleanupWorker>();
builder.Services.AddHostedService<DailyReportWorker>();
builder.Services.Configure<ReviewOptions>(builder.Configuration.GetSection("DatabaseReview"));
builder.Services.AddHostedService<DatabaseReviewWorker>();

var app = builder.Build();
app.Run();
```

**Testing.** Since version 3.1.0 every base class has a constructor overload that accepts `TimeProvider`: the workers read the current time and wait between checks only through it (the old constructors use `TimeProvider.System`). Pass a `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` in tests to check your schedule instantly, without real 5-second waits. Each `Advance` that passes a 5-second check triggers one iteration of the loop. `PeriodicHostedServiceBase` also has an overload with `Random`: pass a seeded or custom `Random` to make the chosen intervals predictable.

```csharp
public sealed class DailyReportWorker : ScheduledHostedServiceBase
{
    public DailyReportWorker(IServiceScopeFactory serviceScopeFactory, ILogger<DailyReportWorker> logger, TimeProvider timeProvider)
        : base(serviceScopeFactory, logger, timeProvider) { }

    // ...
}

// registration: TimeProvider must be available in DI
builder.Services.TryAddSingleton(TimeProvider.System);

// test
var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));
var worker = new DailyReportWorker(scopeFactory, logger, time);
Assert.Equal(new DateTime(2026, 1, 16, 3, 0, 0), worker.NextRun);
```
