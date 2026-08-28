namespace WinLock.Core.Windows;

/// <summary>Where the agent's persisted state lives — shared by the service (which owns
/// state.json day to day) and the elevated Setup/pairing tool (which writes directly into it
/// to record things like <c>NoticeKind.ServiceStopped</c> before stopping the service that
/// would otherwise record it).</summary>
public static class AgentDataPaths
{
    /// <summary>Overridable via WINLOCK_DATA_DIR so this can run and be tested (e.g. against
    /// the controller stub) without permission to write %ProgramData% — on the real Windows
    /// deployment the service runs as SYSTEM, which always can, so this only ever matters for
    /// local dev/test.</summary>
    public static string DataDir => Environment.GetEnvironmentVariable("WINLOCK_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinLock");

    public static string StateFilePath => Path.Combine(DataDir, "state.json");
}
