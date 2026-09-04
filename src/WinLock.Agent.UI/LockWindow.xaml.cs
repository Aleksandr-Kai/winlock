using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinLock.Agent.UI.Interop;
using WinLock.Core.Ipc;
using WinLock.Core.Models;

namespace WinLock.Agent.UI;

public partial class LockWindow : Window
{
    private readonly AgentPipeClient _pipeClient = new();
    private readonly KeyboardHook _keyboardHook = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly bool _monitorDesktopCoverage;
    private readonly List<Process> _desktopCoverageWindows = [];

    private long? _currentChallengeId;
    private bool _allowClose;
    private DispatcherTimer? _pairingWatchTimer;
    private bool _pairingWindowSeenInForeground;
    private DispatcherTimer? _desktopCoverageTimer;

    /// <summary>monitorDesktopCoverage: only the one lock window the service itself launches
    /// should go hunting for virtual desktops without a lock window on them and spawn more —
    /// see <see cref="EnsureCurrentDesktopIsCovered"/>. A window spawned specifically to cover
    /// one of those desktops passes false, or every lock window would end up independently
    /// spawning more copies for the very same gap.</summary>
    public LockWindow(bool monitorDesktopCoverage = true)
    {
        InitializeComponent();
        _monitorDesktopCoverage = monitorDesktopCoverage;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _keyboardHook.Install();
        Activate();

        _pipeClient.MessageReceived += OnMessageReceived;
        _pipeClient.Disconnected += OnDisconnected;

        if (_monitorDesktopCoverage)
        {
            // Windows' virtual desktops ("Task View") are a real, previously-unnoticed
            // bypass: a window created on one desktop simply isn't shown on another one a
            // child switches to (via the taskbar button or a touchpad gesture — neither goes
            // through the keyboard, so KeyboardHook can't see either), leaving a fresh desktop
            // looking completely unlocked even though nothing about the schedule/budget
            // actually changed. Poll for that and put a lock window on every desktop that
            // needs one.
            _desktopCoverageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _desktopCoverageTimer.Tick += (_, _) => EnsureCurrentDesktopIsCovered();
            _desktopCoverageTimer.Start();
        }

        await ConnectAndRequestChallengeAsync();
    }

    /// <summary>Only ever runs on the one lock window the service launched directly — see the
    /// constructor. Spawns a sibling lock window (itself passing monitorDesktopCoverage:
    /// false) onto whichever virtual desktop is currently active whenever neither this window
    /// nor any window already spawned for a previous gap is on it.</summary>
    private void EnsureCurrentDesktopIsCovered()
    {
        const int maxCoverageWindows = 10; // sanity cap against any runaway spawn loop

        if (VirtualDesktop.IsOnCurrentDesktop(new WindowInteropHelper(this).Handle) != false)
            return; // covered by this window itself, or unknown (fail toward not spawning)

        _desktopCoverageWindows.RemoveAll(p => p.HasExited);

        foreach (var covering in _desktopCoverageWindows)
        {
            covering.Refresh();
            var hwnd = covering.MainWindowHandle;
            if (hwnd == 0)
                return; // a spawn for some earlier gap is still starting up; give it a moment
            if (VirtualDesktop.IsOnCurrentDesktop(hwnd) != false)
                return; // covered by that one, or unknown
        }

        if (_desktopCoverageWindows.Count >= maxCoverageWindows)
            return;

        try
        {
            var exePath = Process.GetCurrentProcess().MainModule!.FileName!;
            var covering = Process.Start(new ProcessStartInfo(exePath, "--covering") { UseShellExecute = false });
            if (covering is not null)
                _desktopCoverageWindows.Add(covering);
        }
        catch
        {
            // Best-effort — the next tick will just try again.
        }
    }

    private async Task ConnectAndRequestChallengeAsync()
    {
        SetStatus(null);
        var connected = await _pipeClient.ConnectAsync(TimeSpan.FromSeconds(5), _cts.Token);
        if (!connected)
        {
            SetStatus("Нет связи со службой WinLock. Повторная попытка через несколько секунд...");
            await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token);
            if (!_cts.IsCancellationRequested)
                await ConnectAndRequestChallengeAsync();
            return;
        }

        await _pipeClient.SendAsync(new RequestChallenge(), _cts.Token);
    }

    private void OnDisconnected()
    {
        Dispatcher.Invoke(async () =>
        {
            if (_cts.IsCancellationRequested) return;
            await ConnectAndRequestChallengeAsync();
        });
    }

    private void OnMessageReceived(ServiceToUiMessage message)
    {
        Dispatcher.Invoke(() =>
        {
            switch (message)
            {
                case LockCommand lockCommand:
                    ReasonText.Text = DescribeReason(lockCommand.Reason);
                    break;

                case ChallengeIssued challenge:
                    _currentChallengeId = challenge.ChallengeId;
                    QrImage.Source = QrCodeRenderer.Render(challenge.QrText, Colors.Black, Colors.White);
                    SetStatus(null);
                    break;

                case RedeemResult { Success: true }:
                    SetStatus(null);
                    CloseAndExit();
                    break;

                case RedeemResult { Success: false } failure:
                    SetStatus(failure.ErrorMessage ?? "Неверный код или истёк срок QR-кода.");
                    break;

                case UnlockCommand:
                    CloseAndExit();
                    break;
            }
        });
    }

    private static string DescribeReason(LockReason reason) => reason switch
    {
        LockReason.BudgetExhausted => "Дневной лимит времени за компьютером исчерпан.",
        LockReason.OutsideAllowedWindow => "Сейчас не время, разрешённое расписанием.",
        LockReason.ClockTamperSuspected => "Обнаружено изменение системного времени. Требуется подтверждение родителя.",
        LockReason.ManuallyLocked => "Компьютер заблокирован родителем.",
        _ => "Компьютер заблокирован.",
    };

    private async void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentChallengeId is not { } challengeId)
        {
            SetStatus("Сначала дождитесь QR-кода.");
            return;
        }

        if (!int.TryParse(MinutesBox.Text, out var minutes) || minutes <= 0)
        {
            SetStatus("Введите положительное число минут.");
            return;
        }

        var code = CodeBox.Text.Trim();
        if (code.Length != 4)
        {
            SetStatus("Код должен состоять из 4 цифр.");
            return;
        }

        await _pipeClient.SendAsync(new RedeemOfflineUnlock(challengeId, minutes, code), _cts.Token);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _currentChallengeId = null;
        QrImage.Source = null;
        await _pipeClient.SendAsync(new RequestChallenge(), _cts.Token);
    }

    /// <summary>This process itself always runs "asInvoker" (see app.manifest) — the lock
    /// screen must never run elevated. Launching the pairing window still needs admin rights,
    /// same as the Start Menu shortcut, so it's requested explicitly per-launch via the
    /// "runas" verb rather than by elevating this whole process.</summary>
    private void PairingButton_Click(object sender, RoutedEventArgs e)
    {
        Process? pairingProcess;
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule!.FileName!;
            pairingProcess = Process.Start(new ProcessStartInfo(exePath, "--pair")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user (or a child who can't authenticate) dismissed the UAC
            // prompt — not an error worth surfacing.
            return;
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось открыть окно настройки: {ex.Message}");
            return;
        }

        if (pairingProcess is null)
            return;

        // The keyboard hook's allowlist would otherwise stop an admin from typing a password
        // with ordinary punctuation into whatever the pairing tool needs next — the runas
        // prompt just proved this launch is legitimate, so lift the restriction for as long as
        // that specific window has the foreground (see ReclaimLockScreen for when it's put back).
        _keyboardHook.TrustForegroundProcess((uint)pairingProcess.Id);

        // The lock screen is deliberately full-screen and Topmost so a child can't get
        // behind it — but that means it would also sit on top of, and swallow every click
        // meant for, the pairing window we just launched. The runas prompt above already
        // proved this launch came from an admin, so it's safe to step out of the way while
        // that window is actually in use.
        WindowState = WindowState.Minimized;
        Topmost = false;

        // Stepping aside must not mean "unlocked until the admin remembers to close that
        // window": alt-tabbing away, minimizing it, or clicking the desktop behind it would
        // otherwise leave the whole machine open indefinitely. Poll the foreground window
        // instead of only watching for process exit — the moment focus is anywhere other
        // than the pairing window, reclaim the screen immediately. The first tick or two
        // legitimately won't see it yet (the elevated process is still starting up), so only
        // start enforcing once it's actually been seen in the foreground at least once.
        _pairingWindowSeenInForeground = false;
        _pairingWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _pairingWatchTimer.Tick += (_, _) => CheckPairingWindowFocus(pairingProcess);
        _pairingWatchTimer.Start();
    }

    private void CheckPairingWindowFocus(Process pairingProcess)
    {
        if (pairingProcess.HasExited)
        {
            ReclaimLockScreen();
            return;
        }

        var isPairingWindowForeground = GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundPid) != 0
            && foregroundPid == (uint)pairingProcess.Id;

        if (isPairingWindowForeground)
        {
            _pairingWindowSeenInForeground = true;
            return;
        }

        if (_pairingWindowSeenInForeground)
            ReclaimLockScreen(); // focus moved away, or the window was minimized/closed
    }

    private void ReclaimLockScreen()
    {
        _pairingWatchTimer?.Stop();
        _pairingWatchTimer = null;
        _keyboardHook.TrustForegroundProcess(0);

        Topmost = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    private void SetStatus(string? text) => StatusText.Text = text ?? string.Empty;

    private void CloseAndExit()
    {
        _allowClose = true;
        Close();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            // Nothing short of ending the process should make this window go away.
            e.Cancel = true;
            return;
        }

        _cts.Cancel();
        _pairingWatchTimer?.Stop();
        _desktopCoverageTimer?.Stop();
        _keyboardHook.Dispose();
        _pipeClient.Dispose();
        Application.Current.Shutdown();
    }
}
