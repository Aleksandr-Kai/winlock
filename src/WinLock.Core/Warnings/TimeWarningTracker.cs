namespace WinLock.Core.Warnings;

/// <summary>
/// Decides when to nag the child with a "time is running out" toast — first at 15 minutes
/// remaining, then again every 5 minutes. Pure decision logic, no I/O: <see cref="Check"/> is
/// called once per enforcement poll with the latest state and returns the number of minutes
/// to show, or null if nothing should be shown this tick.
/// </summary>
public sealed class TimeWarningTracker
{
    // Descending: on a big jump (e.g. after a service restart) only the highest applicable
    // threshold fires, rather than all three firing back-to-back a few seconds apart.
    private static readonly int[] ThresholdsMinutesDescending = { 15, 10, 5 };

    // Null means "not currently armed" — either locked, unconfigured, or budget was just
    // topped up/rolled over. The next observed remaining value re-arms it without firing,
    // so a threshold already below it can be crossed (and warned about) again later.
    private TimeSpan? _previousRemaining;

    public int? Check(TimeSpan remaining, bool shouldBeLocked, bool scheduleConfigured)
    {
        if (shouldBeLocked || !scheduleConfigured)
        {
            _previousRemaining = null;
            return null;
        }

        var previous = _previousRemaining ?? TimeSpan.MaxValue;
        _previousRemaining = remaining;

        if (remaining > previous)
            return null; // budget went up (extend / new day) — thresholds below it can fire again later

        foreach (var minutes in ThresholdsMinutesDescending)
        {
            var threshold = TimeSpan.FromMinutes(minutes);
            if (previous > threshold && remaining <= threshold && remaining > TimeSpan.Zero)
                return (int)Math.Ceiling(remaining.TotalMinutes);
        }

        return null;
    }
}
