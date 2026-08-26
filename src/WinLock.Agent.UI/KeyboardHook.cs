using System.Runtime.InteropServices;

namespace WinLock.Agent.UI;

/// <summary>
/// Low-level keyboard hook that swallows the shortcuts a child would reach for to get
/// around the lock screen: Alt+Tab, Alt+Esc, Ctrl+Esc, the Windows key, Alt+F4, and
/// Ctrl+Shift+Esc (Task Manager). Ctrl+Alt+Delete is deliberately not, and cannot be,
/// intercepted here — Windows reserves that Secure Attention Sequence and never delivers
/// it to any hook, by design. The watchdog on the service side (<c>IpcLockController</c>)
/// is what covers a child using that route to kill this process: it notices the exit and
/// relaunches the lock window immediately while the machine is still supposed to be locked.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_F4 = 0x73;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LMENU = 0xA4; // left Alt
    private const int VK_RMENU = 0xA5; // right Alt

    // Keeps the delegate alive for as long as the hook is installed — otherwise the GC
    // could collect it while Windows still holds a native pointer to it.
    private readonly LowLevelKeyboardProc _proc;
    private nint _hookHandle;

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Install()
    {
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            var vkCode = Marshal.ReadInt32(lParam);

            if (vkCode is VK_LWIN or VK_RWIN)
                return 1;

            var altDown = IsDown(VK_LMENU) || IsDown(VK_RMENU);
            if (altDown && vkCode is VK_TAB or VK_ESCAPE or VK_F4)
                return 1;

            var ctrlDown = IsDown(VK_LCONTROL) || IsDown(VK_RCONTROL);
            if (ctrlDown && vkCode == VK_ESCAPE)
                return 1;

            var shiftDown = IsDown(VK_LSHIFT) || IsDown(VK_RSHIFT);
            if (ctrlDown && shiftDown && vkCode == VK_ESCAPE)
                return 1;
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool IsDown(int virtualKeyCode) => (GetAsyncKeyState(virtualKeyCode) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hookHandle != 0)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = 0;
        }
    }

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint GetModuleHandle(string lpModuleName);
}
