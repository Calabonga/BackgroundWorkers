using Microsoft.Extensions.Time.Testing;

namespace Calabonga.Microservices.BackgroundWorkers.Tests;

/// <summary>
/// <see cref="FakeTimeProvider"/> wrapper that counts created timers.
/// Worker loops create a new delay timer at the end of every iteration, so waiting for the next timer
/// means "the iteration triggered by Advance has completed" — tests can step the loop deterministically.
/// </summary>
internal sealed class SteppingTimeProvider : TimeProvider
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _fake;
    private int _timerCount;

    public SteppingTimeProvider(DateTimeOffset start)
    {
        _fake = new FakeTimeProvider(start);
    }

    public int TimerCount => Volatile.Read(ref _timerCount);

    public override TimeZoneInfo LocalTimeZone => _fake.LocalTimeZone;

    public override long TimestampFrequency => _fake.TimestampFrequency;

    /// <summary>
    /// Advances time and waits until the loop schedules its next delay (i.e. finishes the triggered iteration)
    /// </summary>
    /// <returns>false when no iteration completed within timeout</returns>
    public async Task<bool> AdvanceToNextIterationAsync(TimeSpan delta, CancellationToken cancellationToken)
    {
        var timersBefore = TimerCount;
        _fake.Advance(delta);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(WaitTimeout);
        while (TimerCount == timersBefore)
        {
            try
            {
                await Task.Delay(5, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Advances time without waiting (use when no timer is expected to fire)
    /// </summary>
    public void Advance(TimeSpan delta) => _fake.Advance(delta);

    public override DateTimeOffset GetUtcNow() => _fake.GetUtcNow();

    public override long GetTimestamp() => _fake.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = _fake.CreateTimer(callback, state, dueTime, period);
        Interlocked.Increment(ref _timerCount);
        return timer;
    }
}
