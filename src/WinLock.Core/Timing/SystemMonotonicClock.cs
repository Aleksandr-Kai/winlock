namespace WinLock.Core.Timing;

/// <summary>
/// Real clock backed by <see cref="Environment.TickCount64"/>, which tracks time since
/// system start and is unaffected by the user changing the system date/time.
/// </summary>
public sealed class SystemMonotonicClock : IMonotonicClock
{
    public long ElapsedMilliseconds => Environment.TickCount64;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
