using Calabonga.Microservices.BackgroundWorkers.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NCrontab;

namespace Calabonga.Microservices.BackgroundWorkers.Tests;

/// <summary>
/// Schedule settings are read in the base constructor, so every scenario is a separate worker
/// with constant overrides (values from the derived constructor are not available there yet).
/// Time is controlled by <see cref="SteppingTimeProvider"/>: each advance fires at most one pending
/// 5-second check, because the next check is scheduled only after the previous one completes.
/// </summary>
public sealed class ScheduledHostedServiceBaseTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 1, 15, 10, 0, 30, TimeSpan.Zero);

    private readonly Mock<IServiceScopeFactory> _scopeFactory = new Mock<IServiceScopeFactory>();
    private readonly Mock<ILogger> _logger = new Mock<ILogger>();
    private readonly SteppingTimeProvider _time = new SteppingTimeProvider(Start);

    public ScheduledHostedServiceBaseTests()
    {
        _scopeFactory.Setup(x => x.CreateScope()).Returns(Mock.Of<IServiceScope>());
    }

    [Fact]
    public void Constructor_Should_ThrowWorkerArgumentNullException_When_ScheduleEmpty()
    {
        Assert.Throws<WorkerArgumentNullException>(() => new EmptyScheduleWorker(_scopeFactory.Object, _logger.Object, _time));
    }

    [Fact]
    public void Constructor_Should_ThrowCrontabException_When_ScheduleInvalid()
    {
        Assert.Throws<CrontabException>(() => new InvalidScheduleWorker(_scopeFactory.Object, _logger.Object, _time));
    }

    [Fact]
    public void Constructor_Should_ThrowCrontabException_When_SecondsFieldWithoutIncludingSeconds()
    {
        Assert.Throws<CrontabException>(() => new SecondsWithoutFlagWorker(_scopeFactory.Object, _logger.Object, _time));
    }

    [Fact]
    public void Constructor_Should_ThrowArgumentNullException_When_TimeProviderNull()
    {
        // null! is intentional: the test checks the guard against null from callers without nullable annotations
        Assert.Throws<ArgumentNullException>(() => new DailyWorker(_scopeFactory.Object, _logger.Object, null!));
    }

    [Fact]
    public void Constructor_Should_SetNextRunToNextUtcOccurrence_When_ExecuteOnServerRestartDisabled()
    {
        // Act
        var worker = new DailyWorker(_scopeFactory.Object, _logger.Object, _time);

        // Assert
        Assert.Equal(new DateTime(2026, 1, 16, 3, 0, 0), worker.NextRun);
    }

    [Fact]
    public void Constructor_Should_SetNextRunInUtc_When_LocalTimeZoneIsNotUtc()
    {
        // Arrange
        var time = new FakeTimeProvider(Start);
        time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("UTC+09", TimeSpan.FromHours(9), "UTC+09", "UTC+09"));

        // Act
        var worker = new DailyWorker(_scopeFactory.Object, _logger.Object, time);

        // Assert
        Assert.Equal(new DateTime(2026, 1, 16, 3, 0, 0), worker.NextRun);
    }

    [Fact]
    public void Constructor_Should_SetNextRunToNextSecond_When_IncludingSecondsEnabled()
    {
        // Act
        var worker = new EverySecondWorker(_scopeFactory.Object, _logger.Object, _time);

        // Assert
        Assert.Equal(Start.UtcDateTime.AddSeconds(1), worker.NextRun);
    }

    [Fact]
    public void Constructor_Should_SetNextRunInFiveSeconds_When_ExecuteOnServerRestartEnabled()
    {
        // Act
        var worker = new RestartWorker(_scopeFactory.Object, _logger.Object, _time);

        // Assert
        Assert.Equal(Start.UtcDateTime.AddSeconds(5), worker.NextRun);
        _logger.VerifyLog(LogLevel.Information, Times.Once());
    }

    /// <summary>
    /// Regression: with IsDelayBeforeStart = false the loop had no await, burned CPU and never returned from StartAsync.
    /// If this test fails, the spinning loop keeps running in the test process until it exits.
    /// </summary>
    [Fact]
    public async Task StartAsync_Should_ReturnImmediately_When_DelayBeforeStartDisabled()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var worker = new EverySecondWorker(_scopeFactory.Object, _logger.Object, _time);

        // Act
        var startTask = Task.Run(() => worker.StartAsync(token), token);
        var completed = await Task.WhenAny(startTask, Task.Delay(TimeSpan.FromSeconds(2), token));
        await worker.StopAsync(token);

        // Assert
        Assert.Same(startTask, completed);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ProcessAndMoveNextRun_When_OccurrenceReached()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var worker = new EveryMinuteWorker(_scopeFactory.Object, _logger.Object, _time);
        await worker.StartAsync(token);

        // Act: 10:00:30 -> 10:01:05, first check after the initial delay
        var isIterated = await _time.AdvanceToNextIterationAsync(TimeSpan.FromSeconds(35), token);
        await worker.StopAsync(token);

        // Assert
        Assert.True(isIterated);
        Assert.Equal(1, worker.ProcessCount);
        Assert.Equal(new DateTime(2026, 1, 15, 10, 2, 0), worker.NextRun);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNot_Process_When_OccurrenceNotReached()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var worker = new EveryMinuteWorker(_scopeFactory.Object, _logger.Object, _time);
        await worker.StartAsync(token);

        // Act: 10:00:30 -> 10:00:35 -> 10:00:40 -> 10:00:45, NextRun is 10:01:00
        for (var i = 0; i < 3; i++)
        {
            Assert.True(await _time.AdvanceToNextIterationAsync(TimeSpan.FromSeconds(5), token));
        }

        await worker.StopAsync(token);

        // Assert
        Assert.Equal(0, worker.ProcessCount);
        Assert.Equal(new DateTime(2026, 1, 15, 10, 1, 0), worker.NextRun);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNot_Check_When_InitialDelayNotElapsed()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var worker = new EveryMinuteWorker(_scopeFactory.Object, _logger.Object, _time);
        await worker.StartAsync(token);

        // Act: 10:00:30 -> 10:00:34, the initial 5-second delay is still running
        _time.Advance(TimeSpan.FromSeconds(4));
        var timers = _time.TimerCount;
        await worker.StopAsync(token);

        // Assert
        Assert.Equal(1, timers);
        Assert.Equal(0, worker.ProcessCount);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNot_ReplayMissedOccurrences_When_TimeJumpedForward()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var worker = new EveryMinuteWorker(_scopeFactory.Object, _logger.Object, _time);
        await worker.StartAsync(token);
        Assert.True(await _time.AdvanceToNextIterationAsync(TimeSpan.FromSeconds(35), token));

        // Act: 10:01:05 -> 10:11:05, ten occurrences missed
        var isIterated = await _time.AdvanceToNextIterationAsync(TimeSpan.FromMinutes(10), token);
        await worker.StopAsync(token);

        // Assert
        Assert.True(isIterated);
        Assert.Equal(2, worker.ProcessCount);
        Assert.Equal(new DateTime(2026, 1, 15, 10, 12, 0), worker.NextRun);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ProcessAfterFiveSeconds_When_ExecuteOnServerRestartEnabled()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var worker = new RestartWorker(_scopeFactory.Object, _logger.Object, _time);
        await worker.StartAsync(token);

        // Act: first check at 10:00:35 (NextRun is exactly 10:00:35, not due yet), second at 10:00:40
        Assert.True(await _time.AdvanceToNextIterationAsync(TimeSpan.FromSeconds(5), token));
        var processedAtFirstCheck = worker.ProcessCount;
        Assert.True(await _time.AdvanceToNextIterationAsync(TimeSpan.FromSeconds(5), token));
        await worker.StopAsync(token);

        // Assert
        Assert.Equal(0, processedAtFirstCheck);
        Assert.Equal(1, worker.ProcessCount);
        Assert.Equal(new DateTime(2026, 1, 16, 3, 0, 0), worker.NextRun);
    }

    private abstract class TestScheduledWorkerBase : ScheduledHostedServiceBase
    {
        private int _processCount;

        protected TestScheduledWorkerBase(IServiceScopeFactory serviceScopeFactory, ILogger logger, TimeProvider timeProvider)
            : base(serviceScopeFactory, logger, timeProvider)
        {
        }

        public int ProcessCount => _processCount;

        protected override string DisplayName => GetType().Name;

        protected override Task ProcessInScopeAsync(IServiceProvider serviceProvider, CancellationToken token)
        {
            Interlocked.Increment(ref _processCount);
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyScheduleWorker : TestScheduledWorkerBase
    {
        public EmptyScheduleWorker(IServiceScopeFactory f, ILogger l, TimeProvider t) : base(f, l, t) { }

        protected override string Schedule => string.Empty;
    }

    private sealed class InvalidScheduleWorker : TestScheduledWorkerBase
    {
        public InvalidScheduleWorker(IServiceScopeFactory f, ILogger l, TimeProvider t) : base(f, l, t) { }

        protected override string Schedule => "not a cron";
    }

    private sealed class SecondsWithoutFlagWorker : TestScheduledWorkerBase
    {
        public SecondsWithoutFlagWorker(IServiceScopeFactory f, ILogger l, TimeProvider t) : base(f, l, t) { }

        protected override string Schedule => "* * * * * *";
    }

    private sealed class DailyWorker : TestScheduledWorkerBase
    {
        public DailyWorker(IServiceScopeFactory f, ILogger l, TimeProvider t) : base(f, l, t) { }

        protected override string Schedule => "0 3 * * *";
    }

    private sealed class EveryMinuteWorker : TestScheduledWorkerBase
    {
        public EveryMinuteWorker(IServiceScopeFactory f, ILogger l, TimeProvider t) : base(f, l, t) { }

        protected override string Schedule => "* * * * *";
    }

    private sealed class RestartWorker : TestScheduledWorkerBase
    {
        public RestartWorker(IServiceScopeFactory f, ILogger l, TimeProvider t) : base(f, l, t) { }

        protected override string Schedule => "0 3 * * *";

        protected override bool IsExecuteOnServerRestart => true;
    }

    private sealed class EverySecondWorker : TestScheduledWorkerBase
    {
        public EverySecondWorker(IServiceScopeFactory f, ILogger l, TimeProvider t) : base(f, l, t) { }

        protected override string Schedule => "* * * * * *";

        protected override bool IncludingSeconds => true;

        protected override bool IsDelayBeforeStart => false;
    }
}
