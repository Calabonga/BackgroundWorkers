namespace Calabonga.Microservices.BackgroundWorkers;

/// <summary>
/// Unit of <c>MinValue</c> and <c>MaxValue</c> for <see cref="PeriodicHostedServiceBase"/>
/// </summary>
public enum PeriodType
{
    /// <summary>
    /// Interval bounds are in minutes
    /// </summary>
    Minutes,

    /// <summary>
    /// Interval bounds are in hours
    /// </summary>
    Hours
}
