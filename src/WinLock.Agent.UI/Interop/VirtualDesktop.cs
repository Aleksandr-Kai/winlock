using System.Runtime.InteropServices;

namespace WinLock.Agent.UI.Interop;

/// <summary>
/// Detects when a child has switched to a Windows virtual desktop ("Task View") the lock
/// screen doesn't happen to be on — a window created on one virtual desktop simply isn't
/// shown on another unless it's explicitly "pinned", and the lock screen isn't, so a fresh
/// desktop looks completely open.
///
/// Uses DWM's own "cloaked" flag on the window (DWMWA_CLOAKED via dwmapi.dll) rather than the
/// COM IVirtualDesktopManager interface: DWM itself is what hides a window that's on a
/// different virtual desktop, by cloaking it — so this is asking the actual source of truth
/// directly, through one plain, stable P/Invoke, instead of a separate COM object that has
/// its own chance to misbehave or simply not reflect reality here.
/// </summary>
internal static class VirtualDesktop
{
    private const int DWMWA_CLOAKED = 14;
    private const int S_OK = 0;

    /// <summary>True if visible on the current desktop, false if cloaked (on another virtual
    /// desktop, most likely), null if the check itself failed — callers should treat null as
    /// "assume covered" so a failure here can't itself trigger spawning an extra lock window.</summary>
    public static bool? IsOnCurrentDesktop(nint hwnd)
    {
        if (hwnd == 0)
            return null;

        try
        {
            var hr = DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int));
            return hr == S_OK ? cloaked == 0 : null;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
}
