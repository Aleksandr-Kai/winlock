using System.Runtime.InteropServices;

namespace WinLock.Agent.UI;

/// <summary>
/// Low-level keyboard hook, allowlist-based: while the lock screen has the foreground, every
/// key is swallowed except what's actually needed to type into its own minutes/code fields
/// (letters, digits, Shift, and basic editing/navigation) — rather than a list of specific
/// shortcuts to block, which can never be exhaustive against combinations nobody's thought of
/// yet. Ctrl+Alt+Delete is deliberately not, and cannot be, intercepted here — Windows
/// reserves that Secure Attention Sequence and never delivers it to any hook, by design. The
/// watchdog on the service side (<c>IpcLockController</c>) is what covers a child using that
/// route to kill this process: it notices the exit and relaunches the lock window immediately
/// while the machine is still supposed to be locked.
///
/// The allowlist only applies while a "trusted" window has the foreground — our own process,
/// or (see <see cref="TrustForegroundProcess"/>) the elevated pairing tool this same lock
/// screen can launch. Without that exception, an admin who's already passed UAC to open that
/// tool could find themselves unable to type a password containing ordinary punctuation into
/// whatever needs it next — this hook has no way to tell "the child is trying to escape" apart
/// from "the parent is legitimately typing", so it only restricts input while nothing besides
/// the lock screen itself is in control.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int VK_SHIFT = 0x10;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_BACK = 0x08;
    private const int VK_TAB = 0x09;
    private const int VK_RETURN = 0x0D;
    private const int VK_DELETE = 0x2E;
    private const int VK_HOME = 0x24;
    private const int VK_END = 0x23;
    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;

    // Keeps the delegate alive for as long as the hook is installed — otherwise the GC
    // could collect it while Windows still holds a native pointer to it.
    private readonly LowLevelKeyboardProc _proc;
    private readonly uint _ownProcessId;
    private nint _hookHandle;
    private volatile uint _trustedForegroundProcessId;

    public KeyboardHook()
    {
        _proc = HookCallback;
        _ownProcessId = (uint)Environment.ProcessId;
    }

    public void Install()
    {
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    /// <summary>Suspends the allowlist while the given process has the foreground — call
    /// right after launching the elevated pairing tool (an already-authenticated action), and
    /// clear it (0) the moment the lock screen reclaims the foreground.</summary>
    public void TrustForegroundProcess(uint processId) => _trustedForegroundProcessId = processId;

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN) && !IsForegroundTrusted())
        {
            var vkCode = Marshal.ReadInt32(lParam);
            if (!IsAllowedWhileLocked(vkCode))
                return 1;
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool IsAllowedWhileLocked(int vkCode) => vkCode switch
    {
        >= 0x41 and <= 0x5A => true, // A-Z
        >= 0x30 and <= 0x39 => true, // 0-9 (top row)
        >= 0x60 and <= 0x69 => true, // numpad 0-9
        VK_SHIFT or VK_LSHIFT or VK_RSHIFT => true, // shifted digits (!@# etc.), nothing more
        VK_BACK or VK_DELETE or VK_TAB or VK_RETURN => true, // editing the code/minutes fields
        VK_LEFT or VK_RIGHT or VK_UP or VK_DOWN or VK_HOME or VK_END => true, // cursor movement
        _ => false, // everything else — every Ctrl/Alt/Win combo, function keys, punctuation,
                    // media keys, whatever nobody's thought of yet — is blocked outright
    };

    private bool IsForegroundTrusted()
    {
        var trusted = _trustedForegroundProcessId;
        if (GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundPid) == 0)
            return false;

        return foregroundPid == _ownProcessId || (trusted != 0 && foregroundPid == trusted);
    }

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
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint GetModuleHandle(string lpModuleName);
}
