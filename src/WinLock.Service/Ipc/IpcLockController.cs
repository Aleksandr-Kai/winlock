using System.Diagnostics;
using System.Runtime.Versioning;
using WinLock.Core;
using WinLock.Core.Ipc;
using WinLock.Core.Locking;
using WinLock.Core.Models;
using WinLock.Service.Interop;

namespace WinLock.Service.Ipc;

/// <summary>
/// Drives the actual on-screen lock: launches the UI helper into the interactive session,
/// keeps it alive for as long as the machine should stay locked (a watchdog relaunches it
/// if it's ever killed — see <see cref="KeyboardHook"/> for why that matters), and answers
/// the offline-unlock requests it forwards over the pipe.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class IpcLockController : ILockController, IAsyncDisposable
{
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(2);

    private readonly NamedPipeServerHost _pipeHost;
    private readonly AgentRuntime _runtime;
    private readonly ILogger<IpcLockController> _logger;
    private readonly string _uiExePath;

    private readonly object _gate = new();
    private bool _shouldBeLocked;
    private LockReason _currentReason = LockReason.None;
    private int _currentUiProcessId;

    private readonly CancellationTokenSource _watchdogCts = new();

    public IpcLockController(NamedPipeServerHost pipeHost, AgentRuntime runtime, ILogger<IpcLockController> logger)
    {
        _pipeHost = pipeHost;
        _runtime = runtime;
        _logger = logger;
        _uiExePath = ResolveUiExePath();

        _pipeHost.MessageReceived += OnMessageReceived;
        _pipeHost.ClientConnected += OnClientConnected;
        _pipeHost.Start();

        _ = Task.Run(() => WatchdogLoopAsync(_watchdogCts.Token));
    }

    public Task LockAsync(LockReason reason, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _shouldBeLocked = true;
            _currentReason = reason;
            EnsureUiRunningNoLock();
        }

        return _pipeHost.SendAsync(new LockCommand(reason), ct);
    }

    public Task UnlockAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            _shouldBeLocked = false;
            _currentReason = LockReason.None;
        }

        return _pipeHost.SendAsync(new UnlockCommand(), ct);
    }

    private void OnClientConnected()
    {
        LockReason? reason;
        lock (_gate)
            reason = _shouldBeLocked ? _currentReason : null;

        // A freshly (re)connected UI might have missed the original Lock command — e.g. it
        // was still starting up when it was first sent. Bring it up to date immediately.
        if (reason is { } r)
            _ = _pipeHost.SendAsync(new LockCommand(r));
    }

    private void OnMessageReceived(UiToServiceMessage message)
    {
        switch (message)
        {
            case RequestChallenge:
                try
                {
                    var challenge = _runtime.IssueOfflineChallenge();
                    _ = _pipeHost.SendAsync(new ChallengeIssued(challenge.ChallengeId, challenge.ToQrText()));
                }
                catch (InvalidOperationException)
                {
                    _ = _pipeHost.SendAsync(new RedeemResult(false, "Устройство ещё не привязано к родительскому приложению."));
                }
                break;

            case RedeemOfflineUnlock redeem:
                var success = _runtime.TryRedeemOfflineUnlock(redeem.ChallengeId, redeem.Minutes, redeem.Code);
                _logger.LogInformation("Offline unlock redemption for challenge {ChallengeId}: {Success}", redeem.ChallengeId, success);
                _ = _pipeHost.SendAsync(new RedeemResult(success, success ? null : "Неверный код, устаревший QR или превышено число попыток."));
                break;
        }
    }

    private async Task WatchdogLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(WatchdogInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (_gate)
            {
                if (!_shouldBeLocked) continue;
                if (_currentUiProcessId != 0 && IsProcessAlive(_currentUiProcessId)) continue;

                if (_currentUiProcessId != 0)
                    _logger.LogWarning("Lock UI process {Pid} is gone while the machine should stay locked; relaunching.", _currentUiProcessId);

                EnsureUiRunningNoLock();
            }
        }
    }

    /// <summary>Caller must hold <see cref="_gate"/>.</summary>
    private void EnsureUiRunningNoLock()
    {
        if (_currentUiProcessId != 0 && IsProcessAlive(_currentUiProcessId))
            return;

        if (SessionLauncher.TryLaunchInActiveSession(_uiExePath, string.Empty, out var pid))
        {
            _currentUiProcessId = pid;
        }
        else
        {
            _currentUiProcessId = 0;
            _logger.LogWarning("Could not launch the lock UI — no interactive session is signed in right now. Will retry.");
        }
    }

    private static bool IsProcessAlive(int processId)
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

    private static string ResolveUiExePath()
    {
        var serviceDir = AppContext.BaseDirectory;
        // Deployed side by side with the service by the installer.
        return Path.Combine(serviceDir, "WinLock.Agent.UI.exe");
    }

    public async ValueTask DisposeAsync()
    {
        _watchdogCts.Cancel();
        await _pipeHost.DisposeAsync();
    }
}
