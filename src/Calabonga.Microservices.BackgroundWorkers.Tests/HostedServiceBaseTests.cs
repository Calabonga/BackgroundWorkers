using Calabonga.Microservices.BackgroundWorkers.Base;

namespace Calabonga.Microservices.BackgroundWorkers.Tests;

public sealed class HostedServiceBaseTests
{
    [Fact]
    public async Task StartAsync_Should_CallProcessImmediately_When_Started()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var service = new TestHostedService(_ => Task.CompletedTask);

        // Act
        await service.StartAsync(token);

        // Assert
        Assert.Equal(1, service.ProcessCount);
        await service.StopAsync(token);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ProcessAgain_When_IntervalElapsed()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var time = new SteppingTimeProvider(DateTimeOffset.UnixEpoch);
        var service = new TestHostedService(_ => Task.CompletedTask, time);
        await service.StartAsync(token);

        // Act
        var isIterated = await time.AdvanceToNextIterationAsync(TimeSpan.FromSeconds(5), token);
        await service.StopAsync(token);

        // Assert
        Assert.True(isIterated);
        Assert.Equal(2, service.ProcessCount);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNot_ProcessAgain_When_IntervalNotElapsed()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var time = new SteppingTimeProvider(DateTimeOffset.UnixEpoch);
        var service = new TestHostedService(_ => Task.CompletedTask, time);
        await service.StartAsync(token);

        // Act
        time.Advance(TimeSpan.FromSeconds(4.9));
        await service.StopAsync(token);

        // Assert
        Assert.Equal(1, service.ProcessCount);
    }

    [Fact]
    public void Constructor_Should_ThrowArgumentNullException_When_TimeProviderNull()
    {
        // null! is intentional: the test checks the guard against null from callers without nullable annotations
        Assert.Throws<ArgumentNullException>(() => new TestHostedService(_ => Task.CompletedTask, null!));
    }

    [Fact]
    public async Task StartAsync_Should_ReturnFaultedTask_When_ProcessFailsSynchronously()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var service = new TestHostedService(_ => Task.FromException(new InvalidOperationException()));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(token));
    }

    [Fact]
    public async Task StopAsync_Should_CancelProcessToken_When_Called()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var processToken = CancellationToken.None;
        var service = new TestHostedService(t =>
        {
            processToken = t;
            return Task.CompletedTask;
        });
        await service.StartAsync(token);

        // Act
        await service.StopAsync(token);

        // Assert
        Assert.True(processToken.IsCancellationRequested);
    }

    [Fact]
    public async Task StopAsync_ShouldNot_Throw_When_NotStarted()
    {
        // Arrange
        var service = new TestHostedService(_ => Task.CompletedTask);

        // Act
        var exception = await Record.ExceptionAsync(() => service.StopAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task StopAsync_Should_Return_When_StopTokenCancelledAndProcessIgnoresCancellation()
    {
        // Arrange
        var never = new TaskCompletionSource();
        var service = new TestHostedService(_ => never.Task);
        await service.StartAsync(TestContext.Current.CancellationToken);
        using var stopTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        var stopTask = service.StopAsync(stopTokenSource.Token);
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(stopTask, completed);
    }

    private sealed class TestHostedService : HostedServiceBase
    {
        private readonly Func<CancellationToken, Task> _process;
        private int _processCount;

        public TestHostedService(Func<CancellationToken, Task> process)
        {
            _process = process;
        }

        public TestHostedService(Func<CancellationToken, Task> process, TimeProvider timeProvider)
            : base(timeProvider)
        {
            _process = process;
        }

        public int ProcessCount => _processCount;

        protected override Task ProcessAsync(CancellationToken token)
        {
            Interlocked.Increment(ref _processCount);
            return _process(token);
        }
    }
}
