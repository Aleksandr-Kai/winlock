using System.Runtime.InteropServices;

namespace WinLock.Agent.UI.Interop;

/// <summary>
/// Finds a process's top-level window by enumerating windows directly via Win32, instead of
/// using <see cref="System.Diagnostics.Process.MainWindowHandle"/> — which never finds a
/// window carrying the WS_EX_TOOLWINDOW extended style, and WPF adds exactly that style
/// whenever <c>ShowInTaskbar="False"</c> is set (as LockWindow.xaml does, deliberately, so a
/// covering window spawned per virtual desktop doesn't clutter the taskbar). Without this,
/// a spawned covering window's handle can never be found, so the polling loop that confirms
/// coverage before spawning another one for a further desktop gets stuck forever.
/// </summary>
internal static class WindowFinder
{
    public static nint FindTopLevelWindow(int processId)
    {
        nint found = 0;

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var windowProcessId);
            if (windowProcessId != (uint)processId || !IsWindowVisible(hwnd))
                return true; // keep looking

            found = hwnd;
            return false; // stop enumeration
        }, 0);

        return found;
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
}
