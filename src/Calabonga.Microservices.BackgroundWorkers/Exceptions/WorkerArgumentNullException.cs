using System;

namespace Calabonga.Microservices.BackgroundWorkers.Exceptions
{
    public class WorkerArgumentNullException : Exception
    {
        public WorkerArgumentNullException(string message) : base(message){ }

        public WorkerArgumentNullException(string message, Exception innerException) : base(message, innerException){ }
    }
}