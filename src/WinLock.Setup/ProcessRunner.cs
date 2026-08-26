using System.Diagnostics;

namespace WinLock.Setup;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Runs a console tool (sc.exe, netsh.exe, dotnet.exe) and captures its output —
/// every install step below shells out to one of these instead of reimplementing the
/// underlying Win32 APIs, the same approach the original PowerShell installer used.</summary>
public static class ProcessRunner
{
    public static ProcessResult Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Не удалось запустить {exe}.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }
}
