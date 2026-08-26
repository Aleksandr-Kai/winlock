namespace WinLock.Core.Ipc;

public static class IpcEndpoints
{
    /// <summary>Local named pipe the service listens on and the UI helper connects to.</summary>
    public const string PipeName = "WinLockAgentPipe";
}
