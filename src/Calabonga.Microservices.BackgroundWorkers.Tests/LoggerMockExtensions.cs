using Microsoft.Extensions.Logging;
using Moq;

namespace Calabonga.Microservices.BackgroundWorkers.Tests;

/// <summary>
/// Verification helpers for <see cref="ILogger"/> mocks
/// </summary>
internal static class LoggerMockExtensions
{
    /// <summary>
    /// Verifies that logger was called with <paramref name="level"/> and (optionally) <paramref name="exception"/>
    /// </summary>
    public static void VerifyLog(this Mock<ILogger> logger, LogLevel level, Times times, Exception? exception = null)
    {
        logger.Verify(x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.Is<Exception?>(e => exception == null || e == exception),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }
}
