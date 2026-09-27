# BackgroundWorkers

Background Workers for Microservices on ASP.NET Core. Contains Scoped and Scheduled workers

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

**Registration.** Workers are ordinary hosted services, so register them with `AddHostedService<T>()`: the host starts them together with the application and cancels them on shutdown. The library targets `netstandard2.1` and depends only on `Microsoft.Extensions.*.Abstractions` and `ncrontab`, so it works with any .NET host, not only ASP.NET Core. For a custom loop you can inherit `HostedServiceBase` directly and override `ProcessAsync` (and `ExecuteAsync` to change the timing).

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHostedService<CleanupWorker>();
builder.Services.AddHostedService<DailyReportWorker>();

var app = builder.Build();
app.Run();
```
