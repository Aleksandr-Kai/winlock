using System.Windows;

namespace WinLock.Agent.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length >= 2 && e.Args[0] == "--capture-screenshot")
        {
            RunScreenshotCaptureAndExit(e.Args[1]);
            return;
        }

        if (e.Args.Length >= 1 && e.Args[0] == "--pair")
        {
            new PairingWindow().Show();
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--warning" && int.TryParse(e.Args[1], out var minutesRemaining))
        {
            new WarningToastWindow(minutesRemaining).Show();
            return;
        }

        // A lock window spawned by another lock window specifically to cover a virtual
        // desktop the original one isn't on (see LockWindow.EnsureCurrentDesktopIsCovered) —
        // it must not itself go hunting for uncovered desktops too, or every lock window
        // ends up independently spawning more of them for the same gap.
        if (e.Args.Length >= 1 && e.Args[0] == "--covering")
        {
            new LockWindow(monitorDesktopCoverage: false).Show();
            return;
        }

        // No recognized arguments: this is how the service launches the lock screen.
        new LockWindow(monitorDesktopCoverage: true).Show();
    }

    /// <summary>Invisible, one-shot mode: no window, no dispatcher loop needed beyond
    /// startup — capture, write the file, exit immediately.</summary>
    private void RunScreenshotCaptureAndExit(string outputPath)
    {
        try
        {
            ScreenCapture.CaptureToJpegFile(outputPath);
        }
        catch
        {
            // Deliberately swallowed: the coordinator on the service side treats a missing
            // output file as failure and reports that back to the requesting parent.
        }
        finally
        {
            Shutdown();
        }
    }
}
