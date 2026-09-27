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

        public int ProcessCount => _processCount;

        protected override Task ProcessAsync(CancellationToken token)
        {
            Interlocked.Increment(ref _processCount);
            return _process(token);
        }
    }
}
