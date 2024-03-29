﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Calabonga.Microservices.BackgroundWorkers.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NCrontab;

namespace Calabonga.Microservices.BackgroundWorkers
{
    /// <summary> 
    /// Scheduled and Scoped Background Service with CronTab functionality
    /// * * * * * *
    /// | | | | | |
    /// | | | | | +--- day of week (0 - 6) (Sunday=0)
    /// | | | | +----- month (1 - 12)
    /// | | | +------- day of month (1 - 31)
    /// | | +--------- hour (0 - 23)
    /// | +----------- min (0 - 59)
    /// +------------- sec (0 - 59)
    /// </summary>
    public abstract class ScheduledHostedServiceBase : ScopedHostedServiceBase
    {
        private CrontabSchedule? _schedule;

        protected abstract string Schedule { get; }

        protected ScheduledHostedServiceBase(
            IServiceScopeFactory serviceScopeFactory,
            ILogger logger)
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

        /// <summary>
        /// Next Run information calculated by Cron schedule
        /// </summary>
        public DateTime NextRun { get; private set; }

        /// <summary>
        /// ParseOptions for Cron schedule
        /// </summary>
        protected virtual bool IncludingSeconds => false;

        /// <summary>
        /// Use delay before start. 
        /// It can be helpful when you need start in DEBUG mode your application and want that scheduler starts too <see cref="IsExecuteOnServerRestart"/>
        /// </summary>
        protected virtual bool IsDelayBeforeStart { get;  } = true;
        #endregion


        private void GetSchedule()
        {
            if (string.IsNullOrEmpty(Schedule))
            {
                throw new WorkerArgumentNullException(nameof(Schedule));
            }

            _schedule = CrontabSchedule.Parse(Schedule, new CrontabSchedule.ParseOptions { IncludingSeconds = IncludingSeconds });
            var currentDateTime = DateTime.Now;
            if (IsExecuteOnServerRestart)
            {
                NextRun = currentDateTime.AddSeconds(5);
                Logger.LogInformation($"{DisplayName} ({nameof(IsExecuteOnServerRestart)} = {IsExecuteOnServerRestart})");
            }
            else
            {
                NextRun = _schedule.GetNextOccurrence(currentDateTime);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken token)
        {
            do
            {
                var now = DateTime.Now;
                if (now > NextRun)
                {
                    NextRun = _schedule!.GetNextOccurrence(DateTime.Now);
                    await ProcessAsync(token);
                }

                if (IsDelayBeforeStart)
                {
                    await Task.Delay(5000, token); //5 seconds delay
                }
            }
            while (!token.IsCancellationRequested);
        }
    }
}