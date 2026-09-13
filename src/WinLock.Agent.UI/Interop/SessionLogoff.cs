using System.Runtime.InteropServices;

namespace WinLock.Agent.UI.Interop;

/// <summary>
/// Logging off one's own interactive session needs no special privilege on Windows (unlike
/// shutdown/reboot) — so the lock screen, running unelevated in the child's own session, can
/// call this directly instead of asking the SYSTEM-privileged service to do it.
/// </summary>
internal static class SessionLogoff
{
    private const uint EWX_LOGOFF = 0;
    private const uint EWX_FORCE = 0x00000004;

    /// <summary>Force-closes everything in the current session and logs it off without
    /// waiting for apps to respond — a deliberately blunt response, matching the service's
    /// own <c>WTSLogoffSession(..., bWait: false)</c> for the same kind of bypass.</summary>
    public static void ForceLogoff() => ExitWindowsEx(EWX_LOGOFF | EWX_FORCE, 0);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);
}
