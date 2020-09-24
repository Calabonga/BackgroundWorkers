using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Calabonga.Microservices.BackgroundWorkers
{
    /// <summary>
    /// Background processor with services' scope
    /// </summary>
    public abstract class ScopedBackgroundHostedService : BackgroundHostedService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
 
        /// <inheritdoc />
        protected ScopedBackgroundHostedService(IServiceScopeFactory serviceScopeFactory, ILogger logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            Logger = logger;
        }
 
        #region properties
 
        /// <summary>
        /// Current service name
        /// </summary>
        public string ServiceName => $"[{GetType().Name.ToUpperInvariant()}]";
 
        /// <summary>
        /// Represents instance of the active logger
        /// </summary>
        protected ILogger Logger { get; }
 
        #endregion
 
        protected override async Task ProcessAsync(CancellationToken token)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                await ProcessInScopeAsync(scope.ServiceProvider, token);
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, GetType().Name);
                token.ThrowIfCancellationRequested();
            }
        }
 
        /// <summary>
        /// ProcessAsync in scope
        /// </summary>
        /// <param name="serviceProvider"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        protected abstract Task ProcessInScopeAsync(IServiceProvider serviceProvider, CancellationToken token);
    }
}