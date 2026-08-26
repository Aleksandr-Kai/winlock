using System.Runtime.Versioning;
using WinLock.Core.Warnings;
using WinLock.Service.Interop;

namespace WinLock.Service.Notifications;

/// <summary>
/// Pops a small, corner "time is running out" toast into the signed-in child's session —
/// same launch mechanism as the lock screen and screenshot capture (the service itself, in
/// Session 0, has no desktop to draw into). Unlike the lock screen this is a brand new,
/// self-contained UI helper instance each time: it's shown even while the machine stays
/// unlocked and playable, so there's no existing pipe-connected helper to reuse. The minute
/// count travels as a plain command-line argument — no pipe round trip needed for a one-shot,
/// fire-and-forget notice.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TimeWarningNotifier : ITimeWarningNotifier
{
    private readonly ILogger<TimeWarningNotifier> _logger;
    private readonly string _uiExePath;

    public TimeWarningNotifier(ILogger<TimeWarningNotifier> logger)
    {
        _logger = logger;
        _uiExePath = Path.Combine(AppContext.BaseDirectory, "WinLock.Agent.UI.exe");
    }

    public void Notify(int minutesRemaining)
    {
        try
        {
            if (!SessionLauncher.TryLaunchInActiveSession(_uiExePath, $"--warning {minutesRemaining}", out _))
                _logger.LogInformation("Skipped time warning — no interactive session is signed in right now.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch the time warning toast.");
        }
    }
}
