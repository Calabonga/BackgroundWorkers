using Calabonga.Microservices.BackgroundWorkers.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NCrontab;

namespace Calabonga.Microservices.BackgroundWorkers.Tests;

/// <summary>
/// Schedule settings are read in the base constructor, so every scenario is a separate worker
/// with constant overrides (values from the derived constructor are not available there yet).
/// </summary>
public sealed class ScheduledHostedServiceBaseTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new Mock<IServiceScopeFactory>();
    private readonly Mock<ILogger> _logger = new Mock<ILogger>();

    public ScheduledHostedServiceBaseTests()
    {
        _scopeFactory.Setup(x => x.CreateScope()).Returns(Mock.Of<IServiceScope>());
    }

    [Fact]
    public void Constructor_Should_ThrowWorkerArgumentNullException_When_ScheduleEmpty()
    {
        Assert.Throws<WorkerArgumentNullException>(() => new EmptyScheduleWorker(_scopeFactory.Object, _logger.Object));
    }

    [Fact]
    public void Constructor_Should_ThrowCrontabException_When_ScheduleInvalid()
    {
        Assert.Throws<CrontabException>(() => new InvalidScheduleWorker(_scopeFactory.Object, _logger.Object));
    }

    [Fact]
    public void Constructor_Should_ThrowCrontabException_When_SecondsFieldWithoutIncludingSeconds()
    {
        Assert.Throws<CrontabException>(() => new SecondsWithoutFlagWorker(_scopeFactory.Object, _logger.Object));
    }

    [Fact]
    public void Constructor_Should_SetNextRunToNextUtcOccurrence_When_ExecuteOnServerRestartDisabled()
    {
        // Arrange
        var schedule = CrontabSchedule.Parse(DailyWorker.Cron);
        var before = DateTime.UtcNow;

        // Act
        var worker = new DailyWorker(_scopeFactory.Object, _logger.Object);
        var after = DateTime.UtcNow;

        // Assert
        Assert.InRange(worker.NextRun, schedule.GetNextOccurrence(before), schedule.GetNextOccurrence(after));
    }

    [Fact]
    public void Constructor_Should_SetNextRunInFiveSeconds_When_ExecuteOnServerRestartEnabled()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var worker = new RestartWorker(_scopeFactory.Object, _logger.Object);
        var after = DateTime.UtcNow;

        // Assert
        Assert.InRange(worker.NextRun, before.AddSeconds(5), after.AddSeconds(5));
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
        var worker = new EverySecondWorker(_scopeFactory.Object, _logger.Object);

        // Act
        var startTask = Task.Run(() => worker.StartAsync(token), token);
        var completed = await Task.WhenAny(startTask, Task.Delay(TimeSpan.FromSeconds(2), token));
        await worker.StopAsync(token);

        // Assert
        Assert.Same(startTask, completed);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ExecuteAsync_Should_ProcessAndMoveNextRun_When_OccurrenceReached()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var worker = new EverySecondWorker(_scopeFactory.Object, _logger.Object);
        var initialNextRun = worker.NextRun;

        // Act
        await worker.StartAsync(token);
        var completed = await Task.WhenAny(worker.FirstProcessed, Task.Delay(TimeSpan.FromSeconds(12), token));
        await worker.StopAsync(token);

        // Assert
        Assert.Same(worker.FirstProcessed, completed);
        Assert.True(worker.NextRun > initialNextRun);
    }

    private abstract class TestScheduledWorkerBase : ScheduledHostedServiceBase
    {
        private readonly TaskCompletionSource _firstProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _processCount;

        protected TestScheduledWorkerBase(IServiceScopeFactory serviceScopeFactory, ILogger logger)
            : base(serviceScopeFactory, logger)
        {
        }

        public int ProcessCount => _processCount;

        public Task FirstProcessed => _firstProcessed.Task;

        protected override string DisplayName => GetType().Name;

        protected override Task ProcessInScopeAsync(IServiceProvider serviceProvider, CancellationToken token)
        {
            Interlocked.Increment(ref _processCount);
            _firstProcessed.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyScheduleWorker : TestScheduledWorkerBase
    {
        public EmptyScheduleWorker(IServiceScopeFactory f, ILogger l) : base(f, l) { }

        protected override string Schedule => string.Empty;
    }

    private sealed class InvalidScheduleWorker : TestScheduledWorkerBase
    {
        public InvalidScheduleWorker(IServiceScopeFactory f, ILogger l) : base(f, l) { }

        protected override string Schedule => "not a cron";
    }

    private sealed class SecondsWithoutFlagWorker : TestScheduledWorkerBase
    {
        public SecondsWithoutFlagWorker(IServiceScopeFactory f, ILogger l) : base(f, l) { }

        protected override string Schedule => "* * * * * *";
    }

    private sealed class DailyWorker : TestScheduledWorkerBase
    {
        public const string Cron = "0 3 * * *";

        public DailyWorker(IServiceScopeFactory f, ILogger l) : base(f, l) { }

        protected override string Schedule => Cron;
    }

    private sealed class RestartWorker : TestScheduledWorkerBase
    {
        public RestartWorker(IServiceScopeFactory f, ILogger l) : base(f, l) { }

        protected override string Schedule => "0 3 * * *";

        protected override bool IsExecuteOnServerRestart => true;
    }

    private sealed class EverySecondWorker : TestScheduledWorkerBase
    {
        public EverySecondWorker(IServiceScopeFactory f, ILogger l) : base(f, l) { }

        protected override string Schedule => "* * * * * *";

        protected override bool IncludingSeconds => true;

        protected override bool IsDelayBeforeStart => false;
    }
}
