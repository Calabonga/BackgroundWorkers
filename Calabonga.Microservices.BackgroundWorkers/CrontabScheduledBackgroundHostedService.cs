using System;
using System.Threading;
using System.Threading.Tasks;
using Calabonga.Microservices.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NCrontab;

namespace Calabonga.Microservices.BackgroundWorkers
{
    /// <summary>
    /// Scheduled Scoped Background Service with CronTab functionality
    /// * * * * * *
    /// | | | | | |
    /// | | | | | +--- day of week (0 - 6) (Sunday=0)
    /// | | | | +----- month (1 - 12)
    /// | | | +------- day of month (1 - 31)
    /// | | +--------- hour (0 - 23)
    /// | +----------- min (0 - 59)
    /// +------------- sec (0 - 59)
    /// </summary>
    public abstract class CrontabScheduledBackgroundHostedService : ScopedBackgroundHostedService
    {
        private CrontabSchedule _schedule;
        private DateTime _nextRun;
 
        protected abstract string Schedule { get; }
 
        protected CrontabScheduledBackgroundHostedService(IServiceScopeFactory serviceScopeFactory, ILogger logger)
            : base(serviceScopeFactory, logger)
        {
            GetSchedule();
        }
 
        #region Properties
 
        /// <summary>
        /// Indicates that hosted service should start process on server restart
        /// </summary>
        protected virtual bool IsExecuteOnServerRestart => false;
 
        /// <summary>
        /// Identify service by name
        /// </summary>
        protected abstract string DisplayName { get; }
 
        #endregion
 
        private void GetSchedule()
        {
            if (string.IsNullOrEmpty(Schedule))
            {
                throw new MicroserviceArgumentNullException(nameof(Schedule));
            }
            _schedule = CrontabSchedule.Parse(Schedule);
            var currentDateTime = DateTime.Now;
            if (IsExecuteOnServerRestart)
            {
                _nextRun = currentDateTime.AddSeconds(5);
                Logger.LogInformation($"{DisplayName} ({nameof(IsExecuteOnServerRestart)} = {IsExecuteOnServerRestart})");
            }
            else
            {
                _nextRun = _schedule.GetNextOccurrence(currentDateTime);
            }
        }
 
        protected override async Task ExecuteAsync(CancellationToken token)
        {
            do
            {
                var now = DateTime.Now;
                if (now > _nextRun)
                {
                    await ProcessAsync(token);
                    _nextRun = _schedule.GetNextOccurrence(DateTime.Now);
                }
                await Task.Delay(5000, token); //5 seconds delay
            }
            while (!token.IsCancellationRequested);
        }
    }
}