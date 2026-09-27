using AutoFixture;
using AutoFixture.AutoMoq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Calabonga.Microservices.BackgroundWorkers.Tests;

public sealed class ScopedHostedServiceBaseTests
{
    private readonly IFixture _fixture = new Fixture().Customize(new AutoMoqCustomization());
    private readonly Mock<IServiceScopeFactory> _scopeFactory;
    private readonly Mock<IServiceScope> _scope;
    private readonly Mock<IServiceProvider> _serviceProvider;
    private readonly Mock<ILogger> _logger;

    public ScopedHostedServiceBaseTests()
    {
        _serviceProvider = _fixture.Freeze<Mock<IServiceProvider>>();
        _scope = _fixture.Freeze<Mock<IServiceScope>>();
        _scope.Setup(x => x.ServiceProvider).Returns(_serviceProvider.Object);
        _scopeFactory = _fixture.Freeze<Mock<IServiceScopeFactory>>();
        _scopeFactory.Setup(x => x.CreateScope()).Returns(_scope.Object);
        _logger = _fixture.Freeze<Mock<ILogger>>();
    }

    [Fact]
    public async Task ProcessAsync_Should_PassScopeServiceProvider_When_Called()
    {
        // Arrange
        IServiceProvider? received = null;
        var service = CreateService((provider, _) =>
        {
            received = provider;
            return Task.CompletedTask;
        });

        // Act
        await service.RunProcessAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(_serviceProvider.Object, received);
    }

    [Fact]
    public async Task ProcessAsync_Should_CreateNewScope_When_CalledEachTime()
    {
        // Arrange
        var service = CreateService((_, _) => Task.CompletedTask);

        // Act
        await service.RunProcessAsync(TestContext.Current.CancellationToken);
        await service.RunProcessAsync(TestContext.Current.CancellationToken);

        // Assert
        _scopeFactory.Verify(x => x.CreateScope(), Times.Exactly(2));
        _scope.Verify(x => x.Dispose(), Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessAsync_Should_DisposeScope_When_ProcessThrows()
    {
        // Arrange
        var service = CreateService((_, _) => throw new InvalidOperationException());

        // Act
        await service.RunProcessAsync(TestContext.Current.CancellationToken);

        // Assert
        _scope.Verify(x => x.Dispose(), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ShouldNot_Throw_When_ProcessThrows()
    {
        // Arrange
        var error = new InvalidOperationException(_fixture.Create<string>());
        var service = CreateService((_, _) => throw error);

        // Act
        var exception = await Record.ExceptionAsync(() => service.RunProcessAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(exception);
        _logger.VerifyLog(LogLevel.Error, Times.Once(), error);
    }

    [Fact]
    public async Task ProcessAsync_Should_ThrowOperationCanceledException_When_TokenCancelledAndProcessThrows()
    {
        // Arrange
        using var tokenSource = new CancellationTokenSource();
        var service = CreateService((_, _) =>
        {
            tokenSource.Cancel();
            throw new InvalidOperationException();
        });

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.RunProcessAsync(tokenSource.Token));
        _logger.VerifyLog(LogLevel.Error, Times.Once());
    }

    [Fact]
    public void ServiceName_Should_ReturnUpperCaseTypeNameInBrackets_When_Called()
    {
        // Arrange
        var service = CreateService((_, _) => Task.CompletedTask);

        // Act
        var name = service.ServiceName;

        // Assert
        Assert.Equal("[TESTSCOPEDHOSTEDSERVICE]", name);
    }

    private TestScopedHostedService CreateService(Func<IServiceProvider, CancellationToken, Task> process)
        => new TestScopedHostedService(_scopeFactory.Object, _logger.Object, process);

    private sealed class TestScopedHostedService : ScopedHostedServiceBase
    {
        private readonly Func<IServiceProvider, CancellationToken, Task> _process;

        public TestScopedHostedService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger logger,
            Func<IServiceProvider, CancellationToken, Task> process)
            : base(serviceScopeFactory, logger)
        {
            _process = process;
        }

        public Task RunProcessAsync(CancellationToken token) => ProcessAsync(token);

        protected override Task ProcessInScopeAsync(IServiceProvider serviceProvider, CancellationToken token)
            => _process(serviceProvider, token);
    }
}
