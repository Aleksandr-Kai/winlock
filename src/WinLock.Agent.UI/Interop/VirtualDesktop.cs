using System.Runtime.InteropServices;

namespace WinLock.Agent.UI.Interop;

/// <summary>
/// COM interop for the documented (not the fragile, undocumented pinning APIs) part of
/// Windows' virtual desktop surface, stable since Windows 10 1607. Used to detect when a
/// child has switched to a virtual desktop ("Task View") the lock screen doesn't happen to be
/// on — a window created on one virtual desktop simply isn't shown on another unless it's
/// explicitly "pinned", and the lock screen isn't — so a fresh desktop looks completely open.
/// </summary>
[ComImport]
[Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktopManager
{
    [return: MarshalAs(UnmanagedType.Bool)]
    bool IsWindowOnCurrentVirtualDesktop(nint topLevelWindow);

    Guid GetWindowDesktopId(nint topLevelWindow);

    void MoveWindowToDesktop(nint topLevelWindow, ref Guid desktopId);
}

[ComImport]
[Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")]
internal class VirtualDesktopManagerCoClass;

/// <summary>Best-effort wrapper: virtual desktops (or this specific interface) may not exist
/// on every Windows build this runs on, so every call degrades to "unknown" rather than
/// throwing — callers should treat "unknown" as "assume covered" to fail toward not spuriously
/// spawning extra lock windows on a system where this simply isn't available.</summary>
internal static class VirtualDesktop
{
    private static readonly Lazy<IVirtualDesktopManager?> Manager = new(() =>
    {
        try
        {
            return (IVirtualDesktopManager)new VirtualDesktopManagerCoClass();
        }
        catch
        {
            return null;
        }
    });

    public static bool? IsOnCurrentDesktop(nint hwnd)
    {
        if (hwnd == 0)
            return null;

        try
        {
            return Manager.Value?.IsWindowOnCurrentVirtualDesktop(hwnd);
        }
        catch
        {
            // Most commonly COMException 0x8007000B ("bad format") when called against a
            // window that isn't a real top-level desktop window yet (e.g. mid-construction) —
            // treat as unknown, not as "not covered", so a transient glitch here can't itself
            // trigger spawning an extra lock window.
            return null;
        }
    }
}
