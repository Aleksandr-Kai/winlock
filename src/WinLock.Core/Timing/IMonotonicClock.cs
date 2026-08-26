namespace WinLock.Core.Timing;

/// <summary>
/// Abstracts the two clocks the tracker needs: a monotonic one that a user cannot
/// change by editing the system date/time, and the wall clock, used only to evaluate
/// the allowed-hours schedule and to detect tampering by comparing against the monotonic clock.
/// </summary>
public interface IMonotonicClock
{
    /// <summary>Milliseconds since an arbitrary, fixed epoch (e.g. boot). Never decreases, immune to date changes.</summary>
    long ElapsedMilliseconds { get; }

    /// <summary>Current wall-clock time, in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
