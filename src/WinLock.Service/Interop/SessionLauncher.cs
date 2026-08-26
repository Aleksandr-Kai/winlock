using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace WinLock.Service.Interop;

/// <summary>
/// A Windows service runs in Session 0, isolated from the desktop — it cannot show a
/// window directly, even as SYSTEM. This launches the lock-screen UI helper into whichever
/// session is showing the interactive desktop, running as that session's own signed-in
/// user (never elevated), which is the standard, documented way a service gets UI on
/// screen since Windows Vista.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SessionLauncher
{
    public static bool TryLaunchInActiveSession(string exePath, string arguments, out int processId)
    {
        processId = 0;
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
            return false; // no one is logged in on the physical console right now

        if (!WTSQueryUserToken(sessionId, out var userTokenHandle))
            return false;

        using (userTokenHandle)
        {
            if (!DuplicateTokenEx(
                    userTokenHandle,
                    0x02000000 /* MAXIMUM_ALLOWED */,
                    nint.Zero,
                    SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                    TOKEN_TYPE.TokenPrimary,
                    out var primaryTokenHandle))
                return false;

            using (primaryTokenHandle)
            {
                var envBlock = nint.Zero;
                try
                {
                    CreateEnvironmentBlock(out envBlock, primaryTokenHandle, false);

                    var startupInfo = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = "winsta0\\default" };
                    const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
                    const int CREATE_NO_WINDOW = 0x08000000;

                    // Passed as a StringBuilder, not a string: CreateProcessW is documented
                    // to write back into this buffer, and a plain string would be marshaled
                    // as immutable memory (possibly an interned literal), risking an
                    // AccessViolationException.
                    var commandLine = new System.Text.StringBuilder($"\"{exePath}\" {arguments}");
                    var success = CreateProcessAsUser(
                        primaryTokenHandle,
                        null,
                        commandLine,
                        nint.Zero,
                        nint.Zero,
                        false,
                        CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW,
                        envBlock,
                        null,
                        ref startupInfo,
                        out var processInfo);

                    if (!success)
                        return false;

                    processId = processInfo.dwProcessId;
                    CloseHandleSafe(processInfo.hProcess);
                    CloseHandleSafe(processInfo.hThread);
                    return true;
                }
                finally
                {
                    if (envBlock != nint.Zero)
                        DestroyEnvironmentBlock(envBlock);
                }
            }
        }
    }

    private static void CloseHandleSafe(nint handle)
    {
        if (handle != nint.Zero)
            CloseHandle(handle);
    }

    private enum SECURITY_IMPERSONATION_LEVEL
    {
        SecurityAnonymous,
        SecurityIdentification,
        SecurityImpersonation,
        SecurityDelegation,
    }

    private enum TOKEN_TYPE
    {
        TokenPrimary = 1,
        TokenImpersonation = 2,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQueryUserToken(uint sessionId, out SafeAccessTokenHandle phToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(
        SafeAccessTokenHandle hExistingToken,
        uint dwDesiredAccess,
        nint lpTokenAttributes,
        SECURITY_IMPERSONATION_LEVEL impersonationLevel,
        TOKEN_TYPE tokenType,
        out SafeAccessTokenHandle phNewToken);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateEnvironmentBlock(out nint lpEnvironment, SafeAccessTokenHandle hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyEnvironmentBlock(nint lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUser(
        SafeAccessTokenHandle hToken,
        string? lpApplicationName,
        System.Text.StringBuilder lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        bool bInheritHandles,
        int dwCreationFlags,
        nint lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);
}
