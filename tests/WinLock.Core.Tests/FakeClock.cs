using WinLock.Core.Timing;

namespace WinLock.Core.Tests;

/// <summary>Deterministic clock for tests: both readings advance only when told to.</summary>
public sealed class FakeClock : IMonotonicClock
{
    public long ElapsedMilliseconds { get; private set; }
    public DateTimeOffset UtcNow { get; private set; }

    public FakeClock(DateTimeOffset startUtc)
    {
        UtcNow = startUtc;
        ElapsedMilliseconds = 0;
    }

    /// <summary>Advances both clocks together — the normal, un-tampered case.</summary>
    public void Advance(TimeSpan by)
    {
        ElapsedMilliseconds += (long)by.TotalMilliseconds;
        UtcNow += by;
    }

    /// <summary>Simulates the user changing the system date/time: only the wall clock jumps.</summary>
    public void JumpWallClockOnly(TimeSpan by) => UtcNow += by;
}
