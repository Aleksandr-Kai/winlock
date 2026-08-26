using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using WinLock.Service.Interop;

namespace WinLock.Service.Screenshots;

public sealed record ScreenCaptureResult(bool Success, string? ErrorMessage, byte[]? JpegBytes);

/// <summary>
/// Takes exactly one screenshot, only because a specific parent asked for one right now —
/// there is no automatic or continuous capture anywhere in this codebase, deliberately: a
/// child's screen is sensitive, and an on-demand single frame is a materially different
/// (and more defensible) thing than covert continuous monitoring.
///
/// The service itself runs in Session 0 and has no desktop to capture — same constraint as
/// the lock screen. So this reuses <see cref="SessionLauncher"/> to run a tiny, invisible
/// instance of the UI helper (<c>WinLock.Agent.UI.exe --capture-screenshot &lt;path&gt;</c>)
/// inside the signed-in user's own session, wait for it to write the file, read it back, and
/// delete it immediately. Actual capture only works on Windows (the helper and the session
/// launcher both are); on other platforms this fails cleanly so the rest of the network
/// channel — the part actually under test right now — still runs.
/// </summary>
public sealed class ScreenCaptureCoordinator
{
    private readonly ILogger<ScreenCaptureCoordinator> _logger;
    private readonly string _uiExePath;
    private readonly string _tempDir;

    public ScreenCaptureCoordinator(ILogger<ScreenCaptureCoordinator> logger, string dataDir)
    {
        _logger = logger;
        _uiExePath = Path.Combine(AppContext.BaseDirectory, "WinLock.Agent.UI.exe");
        _tempDir = Path.Combine(dataDir, "tmp");
        EnsureTempDirWithRestrictedAcl();
    }

    public async Task<ScreenCaptureResult> CaptureAsync(TimeSpan timeout, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            return new ScreenCaptureResult(false, "Снимки экрана поддерживаются только на Windows.", null);

        var outputPath = Path.Combine(_tempDir, $"shot-{Guid.NewGuid():N}.jpg");

        bool launched;
        int pid;
        try
        {
            launched = SessionLauncher.TryLaunchInActiveSession(_uiExePath, $"--capture-screenshot \"{outputPath}\"", out pid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch the screenshot helper.");
            return new ScreenCaptureResult(false, "Не удалось запустить процесс снимка экрана.", null);
        }

        if (!launched)
            return new ScreenCaptureResult(false, "Нет активной пользовательской сессии на этом ПК.", null);

        var exited = await WaitForExitAsync(pid, timeout, ct);
        if (!exited)
        {
            TryKill(pid);
            return new ScreenCaptureResult(false, "Истекло время ожидания снимка экрана.", null);
        }

        if (!File.Exists(outputPath))
            return new ScreenCaptureResult(false, "Не удалось создать снимок экрана.", null);

        try
        {
            var bytes = await File.ReadAllBytesAsync(outputPath, ct);
            return new ScreenCaptureResult(true, null, bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read captured screenshot.");
            return new ScreenCaptureResult(false, "Не удалось прочитать снимок экрана.", null);
        }
        finally
        {
            TryDeleteFile(outputPath);
        }
    }

    private static async Task<bool> WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            while (!cts.IsCancellationRequested)
            {
                if (!IsAlive(processId))
                    return true;

                await Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // our own timeout, not the caller's cancellation
        }

        return !IsAlive(processId);
    }

    private static bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void TryKill(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
                process.Kill();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // already gone
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temporary screenshot file {Path}.", path);
        }
    }

    private void EnsureTempDirWithRestrictedAcl()
    {
        if (Directory.Exists(_tempDir))
            return;

        var info = Directory.CreateDirectory(_tempDir);
        if (!OperatingSystem.IsWindows())
            return; // Windows ACL model doesn't apply; plain directory is fine for local dev/test

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        // The capture helper runs as the signed-in child's own (non-admin) account — it's
        // the one actually writing the JPEG here — so it needs write access too. Modify
        // (not FullControl) keeps it from being able to change these permissions.
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(security);
    }
}
