using System.Windows;
using System.Windows.Media;
using WinLock.Core.Ipc;

namespace WinLock.Agent.UI;

/// <summary>
/// Launched only via <c>WinLock.Agent.UI.exe --pair</c>, by an administrator (a shortcut the
/// installer marks "Run as administrator" — a standard, non-admin child account can't supply
/// the UAC credentials that requires). The service double-checks this independently by
/// impersonating the pipe connection; see <c>NamedPipeServerHost.IsCurrentClientAdministrator</c>.
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
        StatusText.Text = "Подключение к службе WinLock...";
        _pipeClient.MessageReceived += OnMessageReceived;

        var connected = await _pipeClient.ConnectAsync(TimeSpan.FromSeconds(5), _cts.Token);
        if (!connected)
        {
            StatusText.Text = "Не удалось подключиться к службе WinLock. Убедитесь, что она запущена.";
            return;
        }

        StatusText.Text = "Запрашиваем QR-код...";
        await _pipeClient.SendAsync(new BeginPairingRequest(), _cts.Token);
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
