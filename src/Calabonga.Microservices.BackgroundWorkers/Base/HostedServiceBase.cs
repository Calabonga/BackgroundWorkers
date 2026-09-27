using Microsoft.Extensions.Hosting;

namespace Calabonga.Microservices.BackgroundWorkers.Base;

/// <summary>
/// Background service (Hosted service) as base for all services
/// </summary>
public abstract class HostedServiceBase : IHostedService
{
    private Task? _executingTask;
    private readonly CancellationTokenSource _stoppingCancellationTokenSource = new CancellationTokenSource();

    /// <summary>
    /// Creates service with <see cref="System.TimeProvider.System"/>
    /// </summary>
    protected HostedServiceBase()
        : this(TimeProvider.System)
    {
    }

    /// <summary>
    /// Creates service with custom <see cref="System.TimeProvider"/> (for example, a fake one in tests)
    /// </summary>
    /// <param name="timeProvider">provider for current time and delays</param>
    protected HostedServiceBase(TimeProvider timeProvider)
    {
        TimeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Provider for current time and delays
    /// </summary>
    protected TimeProvider TimeProvider { get; }

    public virtual Task StartAsync(CancellationToken cancellationToken)
    {
        _executingTask = ExecuteAsync(_stoppingCancellationTokenSource.Token);
        return _executingTask.IsCompleted ? _executingTask : Task.CompletedTask;
    }

    public virtual async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_executingTask == null)
        {
            return;
        }

        try
        {
            _stoppingCancellationTokenSource.Cancel();
        }
        finally
        {
            await Task.WhenAny(_executingTask, Task.Delay(Timeout.Infinite, cancellationToken));
        }
    }

    protected virtual async Task ExecuteAsync(CancellationToken token)
    {
        do
        {
            await ProcessAsync(token);
            await TimeProvider.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
        }
        while (!token.IsCancellationRequested);
    }

    protected abstract Task ProcessAsync(CancellationToken token);
}
