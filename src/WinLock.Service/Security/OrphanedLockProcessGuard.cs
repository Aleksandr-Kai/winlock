using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WinLock.Service.Interop;

namespace WinLock.Service.Security;

public interface IOrphanedLockProcessGuard
{
    /// <summary>Call right after sending the close command to the lock screen (see
    /// <see cref="WinLock.Core.Locking.ILockController.UnlockAsync"/>) — whatever granted the
    /// extra time (a schedule change, a parent's top-up, a day rolling over), the machine
    /// should end up with nothing from the lock screen still running. Waits ~20s (giving the
    /// close command a head start, rather than racing it) before checking once whether a
    /// lock-screen process is still around anyway.</summary>
    void CheckForOrphanedLockProcess();
}

/// <summary>
/// The reactive per-virtual-desktop covering approach (see
/// LockWindow.EnsureCurrentDesktopIsCovered) has to keep up with however many desktops a child
/// creates, which has already proven fragile past two of them. This is a different, much
/// simpler check — the same idea many programs use to refuse a second copy of themselves (a
/// "already running?" process check), turned around: instead of asking "is a lock window on
/// THIS desktop", just ask Windows whether a WinLock.Agent.UI process exists anywhere in
/// memory at all, independent of any desktop.
///
/// Every time the lock screen is told to close, the machine should shortly end up with nothing
/// from it still running. If a lock process turns up anyway ~20s later, that means one got
/// orphaned on some virtual desktop from the previous lockout instead of properly exiting —
/// exactly the state a child who dodged the lock on an extra desktop would leave behind.
/// Rather than try to guess which desktop still needs covering, the response is to log the
/// whole interactive session off: that destroys every virtual desktop it owns in one shot,
/// escape route included.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OrphanedLockProcessGuard(ILogger<OrphanedLockProcessGuard> logger) : IOrphanedLockProcessGuard
{
    // Two 10s stages, not one 20s wait, so the close command gets a full 10s to actually take
    // effect before the check itself starts counting -- a slow-to-exit window right at the
    // boundary still only needs to close within the second half, not the whole window.
    private static readonly TimeSpan CloseCommandGracePeriod = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CheckDelay = TimeSpan.FromSeconds(10);
    private const string LockProcessName = "WinLock.Agent.UI";

    public void CheckForOrphanedLockProcess()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(CloseCommandGracePeriod);
            await Task.Delay(CheckDelay);
            if (HasLockProcessRunning())
                HandleConfirmedOrphan();
        });
    }

    private static bool HasLockProcessRunning() =>
        Process.GetProcessesByName(LockProcessName).Length > 0;

    private void HandleConfirmedOrphan()
    {
        logger.LogWarning(
            "A {ProcessName} process was still running ~20s after being told to close -- " +
            "treating it as orphaned from a virtual-desktop bypass and forcing a logoff.",
            LockProcessName);

        try
        {
            // Belt and suspenders: WTSLogoffSession below doesn't wait for the logoff to
            // actually complete, so put a lock window back up on whatever's currently on
            // screen for the few seconds that takes.
            var exePath = Path.Combine(AppContext.BaseDirectory, $"{LockProcessName}.exe");
            SessionLauncher.TryLaunchInActiveSession(exePath, string.Empty, out _);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not launch a fresh lock instance before forcing the logoff.");
        }

        try
        {
            var sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId != 0xFFFFFFFF)
                WTSLogoffSession(nint.Zero, sessionId, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not force a logoff of the active session.");
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSLogoffSession(nint hServer, uint sessionId, bool bWait);
}
