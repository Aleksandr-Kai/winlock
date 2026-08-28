using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using WinLock.Core.Ipc;
using WinLock.Core.Models;
using WinLock.Core.State;
using WinLock.Core.Windows;

namespace WinLock.Agent.UI;

/// <summary>
/// Launched only via <c>WinLock.Agent.UI.exe --pair</c>, by an administrator (a shortcut the
/// installer marks "Run as administrator" — a standard, non-admin child account can't supply
/// the UAC credentials that requires). The service double-checks this independently by
/// impersonating the pipe connection; see <c>PipeClient.IsAdministrator</c>.
/// </summary>
public partial class PairingWindow : Window
{
    private readonly AgentPipeClient _pipeClient = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _completed;

    public PairingWindow()
    {
        InitializeComponent();
        // WinLock.Agent.UI and WinLock.Service are always built and shipped together (see
        // installer/build-payload.sh) — no IPC round trip needed to know the service's
        // version, this process already links the same WinLock.Core it does.
        VersionText.Text = $"Версия ПК-агента: {WinLock.Core.AgentVersion.Current}";
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _pipeClient.MessageReceived += OnMessageReceived;
        await RefreshServiceStatusAndConnectAsync();
    }

    private async Task RefreshServiceStatusAndConnectAsync()
    {
        StatusText.Text = "Проверяем службу WinLock...";
        var running = await Task.Run(() => ServiceControl.IsRunning(ServiceControl.ServiceName));
        UpdateServiceButtons(running);

        if (!running)
        {
            StatusText.Text = "Служба WinLock остановлена. Запустите её, чтобы продолжить привязку.";
            return;
        }

        StatusText.Text = "Подключение к службе WinLock...";
        var connected = await _pipeClient.ConnectAsync(TimeSpan.FromSeconds(5), _cts.Token);
        if (!connected)
        {
            StatusText.Text = "Не удалось подключиться к службе WinLock, хотя она запущена. Попробуйте её перезапустить.";
            return;
        }

        StatusText.Text = "Запрашиваем QR-код...";
        await _pipeClient.SendAsync(new BeginPairingRequest(), _cts.Token);
    }

    private void UpdateServiceButtons(bool running)
    {
        ServiceStatusText.Text = running ? "Служба WinLock запущена." : "Служба WinLock остановлена.";
        StartServiceButton.IsEnabled = !running;
        StopServiceButton.IsEnabled = running;
    }

    private async void StartServiceButton_Click(object sender, RoutedEventArgs e)
    {
        StartServiceButton.IsEnabled = false;
        StopServiceButton.IsEnabled = false;
        StatusText.Text = "Запускаем службу...";

        var started = await Task.Run(() => ServiceControl.Start(ServiceControl.ServiceName));
        if (!started)
        {
            StatusText.Text = "Служба не запустилась за отведённое время.";
            UpdateServiceButtons(await Task.Run(() => ServiceControl.IsRunning(ServiceControl.ServiceName)));
            return;
        }

        await RefreshServiceStatusAndConnectAsync();
    }

    private async void StopServiceButton_Click(object sender, RoutedEventArgs e)
    {
        StartServiceButton.IsEnabled = false;
        StopServiceButton.IsEnabled = false;
        StatusText.Text = "Останавливаем службу...";

        // Best-effort: the service is the one normally responsible for recording things like
        // this about itself, but it's the one about to be stopped. Written directly to the
        // same state file (machine-scope DPAPI decrypts for any admin on this machine, not
        // just SYSTEM) so the next time a parent's phone connects — whenever the service is
        // next started — it finds out the service was stopped, even though nothing was
        // running to tell it so at the actual moment.
        await Task.Run(RecordServiceStoppedNotice);

        await Task.Run(() => ServiceControl.Stop(ServiceControl.ServiceName));

        // The lock screen can only ever be dismissed by the service validating an unlock
        // code — with the service stopped, it would otherwise sit there retrying the pipe
        // forever with no way out at all. Stopping the service is already a full,
        // authenticated bypass of every protection this product enforces, so releasing the
        // lock screen at the same moment doesn't open anything that wasn't already open; and
        // with the service down, its watchdog won't relaunch it either.
        var closedLockScreen = await Task.Run(CloseOtherAgentUiProcesses);

        QrImage.Source = null;
        QrTextBox.Text = string.Empty;
        UpdateServiceButtons(false);
        StatusText.Text = closedLockScreen
            ? "Служба остановлена, экран блокировки закрыт вместе с ней."
            : "Служба остановлена.";
    }

    private static void RecordServiceStoppedNotice()
    {
        try
        {
            var dataDir = Environment.GetEnvironmentVariable("WINLOCK_DATA_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinLock");
            var store = new JsonFileStateStore(Path.Combine(dataDir, "state.json"), new DpapiStateProtector());

            var data = store.LoadAsync().GetAwaiter().GetResult();
            // Replace, don't accumulate: repeated stops before a parent ever sees the first
            // one would otherwise pile up duplicate notices for the same kind.
            data.PendingNotices.RemoveAll(n => n.Kind == NoticeKind.ServiceStopped);
            data.PendingNotices.Add(new PendingNotice(
                NoticeKind.ServiceStopped, DateTimeOffset.UtcNow, "Остановлена вручную через окно настройки/привязки на ПК."));
            store.SaveAsync(data).GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort notice only — must never block actually stopping the service just
            // because, say, the state file happened to be locked by something else right now.
        }
    }

    private static bool CloseOtherAgentUiProcesses()
    {
        var currentPid = Environment.ProcessId;
        var closedAny = false;
        foreach (var process in Process.GetProcessesByName("WinLock.Agent.UI"))
        {
            using (process)
            {
                if (process.Id == currentPid)
                    continue;
                try
                {
                    process.Kill();
                    closedAny = true;
                }
                catch
                {
                    // already exiting on its own — fine either way
                }
            }
        }

        return closedAny;
    }

    private void OnMessageReceived(ServiceToUiMessage message)
    {
        Dispatcher.Invoke(() =>
        {
            switch (message)
            {
                case PairingQrIssued qr:
                    QrImage.Source = QrCodeRenderer.Render(qr.QrText, Colors.Black, Colors.White);
                    QrTextBox.Text = qr.QrText;
                    StatusText.Text = $"Действителен до {qr.ExpiresAtUtc.ToLocalTime():T}.";
                    break;

                case PairingCompleted completed:
                    _completed = true;
                    StatusText.Text = $"Готово! Привязано устройство «{completed.ControllerDisplayName}».";
                    QrImage.Source = null;
                    QrTextBox.Text = string.Empty;
                    break;

                case PairingFailed failed:
                    StatusText.Text = failed.Reason;
                    break;
            }
        });
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_completed)
            await _pipeClient.SendAsync(new CancelPairingRequest());

        _cts.Cancel();
        _pipeClient.Dispose();
    }
}
