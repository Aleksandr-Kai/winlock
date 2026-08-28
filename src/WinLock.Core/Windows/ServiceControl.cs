namespace WinLock.Core.Windows;

/// <summary>Thin sc.exe wrapper — deliberately not System.ServiceProcess.ServiceController:
/// sc.exe is what the original PowerShell installer used (via New-Service/Get-Service, which
/// themselves shell out to the same SCM APIs), and reusing the exact same commands here keeps
/// the proven-working "update in place, never delete+recreate" behavior identical.</summary>
public static class ServiceControl
{
    public const string ServiceName = "WinLock Agent";

    private const int ErrorServiceDoesNotExist = 1060;

    public static bool Exists(string serviceName) =>
        ProcessRunner.Run("sc.exe", "query", serviceName).ExitCode != ErrorServiceDoesNotExist;

    public static bool IsRunning(string serviceName)
    {
        var result = ProcessRunner.Run("sc.exe", "query", serviceName);
        return result.ExitCode == 0 && result.StandardOutput.Contains("RUNNING");
    }

    public static void Stop(string serviceName)
    {
        ProcessRunner.Run("sc.exe", "stop", serviceName);
        WaitUntil(() => !IsRunning(serviceName), TimeSpan.FromSeconds(15));
    }

    /// <returns>true if the service reached the Running state within the timeout.</returns>
    public static bool Start(string serviceName)
    {
        ProcessRunner.Run("sc.exe", "start", serviceName);
        return WaitUntil(() => IsRunning(serviceName), TimeSpan.FromSeconds(15));
    }

    public static void Create(string serviceName, string displayName, string binaryPath, string description)
    {
        ProcessRunner.Run("sc.exe", "create", serviceName,
            "binPath=", binaryPath, "start=", "auto", "DisplayName=", displayName);
        SetDescriptionAndRecovery(serviceName, description);
    }

    public static void UpdateInPlace(string serviceName, string binaryPath, string description)
    {
        ProcessRunner.Run("sc.exe", "config", serviceName, "binPath=", binaryPath, "start=", "auto");
        SetDescriptionAndRecovery(serviceName, description);
    }

    public static void Delete(string serviceName) =>
        ProcessRunner.Run("sc.exe", "delete", serviceName);

    private static void SetDescriptionAndRecovery(string serviceName, string description)
    {
        ProcessRunner.Run("sc.exe", "description", serviceName, description);
        // Up to 3 restarts/day, 5s apart, then leave it stopped rather than loop forever if
        // something is fundamentally broken.
        ProcessRunner.Run("sc.exe", "failure", serviceName,
            "reset=", "86400", "actions=", "restart/5000/restart/5000/restart/5000");
        ProcessRunner.Run("sc.exe", "failureflag", serviceName, "1");
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            Thread.Sleep(500);
        }
        return condition();
    }
}
