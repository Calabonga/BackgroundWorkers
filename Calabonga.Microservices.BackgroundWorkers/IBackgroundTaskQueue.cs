using System;
using System.Threading;
using System.Threading.Tasks;

namespace Calabonga.Microservices.BackgroundWorkers
{
    /// <summary>
    /// Background service interface. See https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-3.1&tabs=visual-studio#queued-background-tasks
    /// </summary>
    public interface IBackgroundTaskQueue
    {
        void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem);

        Task<Func<CancellationToken, Task>?> DequeueAsync(CancellationToken cancellationToken);
    }
}