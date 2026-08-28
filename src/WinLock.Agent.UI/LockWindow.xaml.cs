using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using WinLock.Core.Ipc;
using WinLock.Core.Models;

namespace WinLock.Agent.UI;

public partial class LockWindow : Window
{
    private readonly AgentPipeClient _pipeClient = new();
    private readonly KeyboardHook _keyboardHook = new();
    private readonly CancellationTokenSource _cts = new();

    private long? _currentChallengeId;
    private bool _allowClose;

    public LockWindow()
    {
        InitializeComponent();

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

        await ConnectAndRequestChallengeAsync();
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
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule!.FileName!;
            Process.Start(new ProcessStartInfo(exePath, "--pair")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user (or a child who can't authenticate) dismissed the UAC
            // prompt — not an error worth surfacing.
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось открыть окно настройки: {ex.Message}");
        }
    }

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
        _keyboardHook.Dispose();
        _pipeClient.Dispose();
        Application.Current.Shutdown();
    }
}
