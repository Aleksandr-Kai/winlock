using WinLock.Core.Warnings;

namespace WinLock.Service;

/// <summary>Stands in for <see cref="Notifications.TimeWarningNotifier"/> on non-Windows dev/test
/// hosts, where there's no desktop session to overlay onto.</summary>
public sealed class NullTimeWarningNotifier : ITimeWarningNotifier
{
    private readonly ILogger<NullTimeWarningNotifier> _logger;

    public NullTimeWarningNotifier(ILogger<NullTimeWarningNotifier> logger)
    {
        _logger = logger;
    }

    public void Notify(int minutesRemaining) =>
        _logger.LogInformation("Time warning ({Minutes} min remaining) — no-op on this platform.", minutesRemaining);
}
