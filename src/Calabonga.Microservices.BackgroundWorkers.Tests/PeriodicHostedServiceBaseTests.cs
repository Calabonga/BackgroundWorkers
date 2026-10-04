using Calabonga.Microservices.BackgroundWorkers.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Calabonga.Microservices.BackgroundWorkers.Tests;

/// <summary>
/// Settings of <see cref="PeriodicHostedServiceBase"/> are read when the worker starts, so a single test worker
/// takes them through its constructor (the way a real worker would take them from options).
/// Time is controlled by <see cref="SteppingTimeProvider"/>, random intervals by <see cref="SequenceRandom"/>.
/// </summary>
public sealed class PeriodicHostedServiceBaseTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 15, 10, 0, 30, TimeSpan.Zero);

    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly Mock<ILogger> _logger = new();
    private readonly SteppingTimeProvider _time = new(Start);

    public PeriodicHostedServiceBaseTests()
    {
        _scopeFactory.Setup(x => x.CreateScope()).Returns(Mock.Of<IServiceScope>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task StartAsync_Should_ThrowWorkerArgumentOutOfRangeException_When_MinValueLessThanOne(int minValue)
    {
        // Arrange
        var worker = CreateWorker(new WorkerSettings(PeriodType.Minutes, minValue, 10), new SequenceRandom());

        // Act & Assert
        await Assert.ThrowsAsync<WorkerArgumentOutOfRangeException>(() => worker.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StartAsync_Should_ThrowWorkerArgumentOutOfRangeException_When_MaxValueLessThanMinValue()
    {
        // Arrange
        var worker = CreateWorker(new WorkerSettings(PeriodType.Minutes, 15, 10), new SequenceRandom());

        // Act & Assert
        await Assert.ThrowsAsync<WorkerArgumentOutOfRangeException>(() => worker.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StartAsync_Should_ThrowWorkerArgumentOutOfRangeException_When_PeriodTypeUnknown()
    {
        // Arrange
        var worker = CreateWorker(new WorkerSettings((PeriodType)42, 1, 2), new SequenceRandom());

        // Act & Assert
        await Assert.ThrowsAsync<WorkerArgumentOutOfRangeException>(() => worker.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StartAsync_Should_SetNextRunFromHoursRange_When_SettingsFromDerivedConstructor()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var random = new SequenceRandom(187);
        var worker = CreateWorker(new WorkerSettings(PeriodType.Hours, 3, 4), random);

        // Act
        await worker.StartAsync(token);
        await worker.StopAsync(token);

        // Assert
        Assert.Equal((180, 241), Assert.Single(random.Calls));
        Assert.Equal(Start.UtcDateTime.AddMinutes(187), worker.NextRun);
    }

    [Fact]
    public async Task StartAsync_Should_SetNextRunFromMinutesRange_When_PeriodTypeMinutes()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var random = new SequenceRandom(13);
        var worker = CreateWorker(new WorkerSettings(PeriodType.Minutes, 10, 15), random);

        // Act
        await worker.StartAsync(token);
        await worker.StopAsync(token);

        // Assert
        Assert.Equal((10, 16), Assert.Single(random.Calls));
        Assert.Equal(Start.UtcDateTime.AddMinutes(13), worker.NextRun);
    }

    [Fact]
    public async Task StartAsync_Should_SetNextRunInFiveSecondsWithoutRandom_When_ExecuteOnServerRestartEnabled()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var random = new SequenceRandom();
        var worker = CreateWorker(new WorkerSettings(PeriodType.Minutes, 10, 15, IsExecuteOnServerRestart: true), random);

        // Act
        await worker.StartAsync(token);
        await worker.StopAsync(token);

        // Assert
        Assert.Empty(random.Calls);
        Assert.Equal(Start.UtcDateTime.AddSeconds(5), worker.NextRun);
    }

    [Fact]
    public async Task StartAsync_Should_LogInformationOnce_When_Started()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var worker = CreateWorker(new WorkerSettings(PeriodType.Minutes, 10, 15), new SequenceRandom(10));

        // Act
        await worker.StartAsync(token);
        await worker.StopAsync(token);

        // Assert
        _logger.VerifyLog(LogLevel.Information, Times.Once());
    }

    /// <summary>
    /// Regression: the loop must await a delay on every iteration, otherwise StartAsync never returns.
    /// </summary>
    [Fact]
    public async Task StartAsync_Should_ReturnImmediately_When_DelayBeforeStartDisabled()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var worker = CreateWorker(new WorkerSettings(PeriodType.Minutes, 10, 15, IsDelayBeforeStart: false), new SequenceRandom(10));

        // Act
        var startTask = Task.Run(() => worker.StartAsync(token), token);
        var completed = await Task.WhenAny(startTask, Task.Delay(TimeSpan.FromSeconds(2), token));
        await worker.StopAsync(token);

        // Assert
        Assert.Same(startTask, completed);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNot_Process_When_NextRunNotReached()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var worker = CreateWorker(new WorkerSettings(PeriodType.Minutes, 10, 15), new SequenceRandom(10));
        await worker.StartAsync(token);

        // Act: 10:00:30 -> 10:00:35 -> 10:00:40 -> 10:00:45, NextRun is 10:10:30
        for (var i = 0; i < 3; i++)
        {
            Assert.True(await _time.AdvanceToNextIterationAsync(TimeSpan.FromSeconds(5), token));
        }

        await worker.StopAsync(token);

        // Assert
        Assert.Equal(0, worker.ProcessCount);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ProcessAndChooseNewInterval_When_NextRunReached()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var random = new SequenceRandom(11, 15, 12);
        var worker = CreateWorker(new WorkerSettings(PeriodType.Minutes, 10, 15), random);
        await worker.StartAsync(token);

        // Act: NextRun 10:11:30; first check at 10:11:35 -> run, next interval 15 min
        Assert.True(await _time.AdvanceToNextIterationAsync(TimeSpan.FromMinutes(11) + TimeSpan.FromSeconds(5), token));
        var processedFirst = worker.ProcessCount;
        var nextRunAfterFirst = worker.NextRun;

        // Act: NextRun 10:26:35; next check at 10:26:40 -> run, next interval 12 min
        Assert.True(await _time.AdvanceToNextIterationAsync(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(5), token));
        await worker.StopAsync(token);

        // Assert
        Assert.Equal(1, processedFirst);
        Assert.Equal(new DateTime(2026, 1, 15, 10, 26, 35), nextRunAfterFirst);
        Assert.Equal(2, worker.ProcessCount);
        Assert.Equal(new DateTime(2026, 1, 15, 10, 38, 40), worker.NextRun);
        Assert.Equal(3, random.Calls.Count);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ProcessAfterFiveSeconds_When_ExecuteOnServerRestartEnabled()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var random = new SequenceRandom(14);
        var worker = CreateWorker(new WorkerSettings(PeriodType.Minutes, 10, 15, IsExecuteOnServerRestart: true), random);
        await worker.StartAsync(token);

        // Act: first check at 10:00:35 (NextRun is exactly 10:00:35, not due yet), second at 10:00:40
        Assert.True(await _time.AdvanceToNextIterationAsync(TimeSpan.FromSeconds(5), token));
        var processedAtFirstCheck = worker.ProcessCount;
        Assert.True(await _time.AdvanceToNextIterationAsync(TimeSpan.FromSeconds(5), token));
        await worker.StopAsync(token);

        // Assert
        Assert.Equal(0, processedAtFirstCheck);
        Assert.Equal(1, worker.ProcessCount);
        Assert.Equal(new DateTime(2026, 1, 15, 10, 14, 40), worker.NextRun);
    }

    [Fact]
    public void Constructor_Should_ThrowArgumentNullException_When_RandomNull()
    {
        // null! is intentional: the test checks the guard against null from callers without nullable annotations
        Assert.Throws<ArgumentNullException>(() => CreateWorker(new WorkerSettings(PeriodType.Minutes, 10, 15), null!));
    }

    [Fact]
    public async Task StartAsync_Should_SetNextRunWithinRange_When_DefaultConstructorUsed()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var worker = new DefaultWorker(_scopeFactory.Object, _logger.Object);
        var before = DateTime.UtcNow;

        // Act
        await worker.StartAsync(token);
        var after = DateTime.UtcNow;
        await worker.StopAsync(token);

        // Assert
        Assert.InRange(worker.NextRun, before.AddMinutes(10), after.AddMinutes(15));
    }

    private TestPeriodicWorker CreateWorker(WorkerSettings settings, Random random)
        => new(_scopeFactory.Object, _logger.Object, _time, random, settings);

    private sealed record WorkerSettings(
        PeriodType PeriodType,
        int MinValue,
        int MaxValue,
        bool IsExecuteOnServerRestart = false,
        bool IsDelayBeforeStart = true);

    private sealed class TestPeriodicWorker : PeriodicHostedServiceBase
    {
        private readonly WorkerSettings _settings;
        private int _processCount;

        public TestPeriodicWorker(
            IServiceScopeFactory serviceScopeFactory,
            ILogger logger,
            TimeProvider timeProvider,
            Random random,
            WorkerSettings settings)
            : base(serviceScopeFactory, logger, timeProvider, random)
        {
            _settings = settings;
        }

        public int ProcessCount => _processCount;

        protected override PeriodType PeriodType => _settings.PeriodType;

        protected override int MinValue => _settings.MinValue;

        protected override int MaxValue => _settings.MaxValue;

        protected override string DisplayName => nameof(TestPeriodicWorker);

        protected override bool IsExecuteOnServerRestart => _settings.IsExecuteOnServerRestart;

        protected override bool IsDelayBeforeStart => _settings.IsDelayBeforeStart;

        protected override Task ProcessInScopeAsync(IServiceProvider serviceProvider, CancellationToken token)
        {
            Interlocked.Increment(ref _processCount);
            return Task.CompletedTask;
        }
    }

    private sealed class DefaultWorker : PeriodicHostedServiceBase
    {
        public DefaultWorker(IServiceScopeFactory serviceScopeFactory, ILogger logger)
            : base(serviceScopeFactory, logger)
        {
        }

        protected override PeriodType PeriodType => PeriodType.Minutes;

        protected override int MinValue => 10;

        protected override int MaxValue => 15;

        protected override string DisplayName => nameof(DefaultWorker);

        protected override Task ProcessInScopeAsync(IServiceProvider serviceProvider, CancellationToken token) => Task.CompletedTask;
    }

    /// <summary>
    /// Returns predefined values from <see cref="Next(int, int)"/> and records requested ranges
    /// </summary>
    private sealed class SequenceRandom : Random
    {
        private readonly Queue<int> _values;

        public SequenceRandom(params int[] values)
        {
            _values = new Queue<int>(values);
        }

        public List<(int MinValue, int MaxValue)> Calls { get; } = new List<(int MinValue, int MaxValue)>();

        public override int Next(int minValue, int maxValue)
        {
            Calls.Add((minValue, maxValue));
            return _values.Dequeue();
        }
    }
}
