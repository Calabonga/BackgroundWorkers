namespace Calabonga.Microservices.BackgroundWorkers.Exceptions;

/// <summary>
/// Worker setting has a value outside of the allowed range
/// </summary>
public sealed class WorkerArgumentOutOfRangeException : Exception
{
    public WorkerArgumentOutOfRangeException(string message) : base(message) { }

    public WorkerArgumentOutOfRangeException(string message, Exception innerException) : base(message, innerException) { }
}
