using System.Runtime.Versioning;
using WinLock.Core;
using WinLock.Core.Ipc;
using WinLock.Service.Network;

namespace WinLock.Service.Ipc;

/// <summary>
/// Handles the pairing-related messages on the named pipe — sent only by the separate Setup
/// tool (<c>WinLock.Agent.UI.exe --pair</c>), never by the lock screen. Both share the same
/// pipe and protocol; each side's handler just ignores message types meant for the other.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PairingPipeHandler
{
    private static readonly TimeSpan PairingValidity = TimeSpan.FromMinutes(5);

    private readonly NamedPipeServerHost _pipeHost;
    private readonly AgentRuntime _runtime;
    private readonly string _certificateFingerprintHex;
    private readonly int _port;
    private readonly ILogger<PairingPipeHandler> _logger;

    public PairingPipeHandler(
        NamedPipeServerHost pipeHost, AgentRuntime runtime, string certificateFingerprintHex, int port, ILogger<PairingPipeHandler> logger)
    {
        _pipeHost = pipeHost;
        _runtime = runtime;
        _certificateFingerprintHex = certificateFingerprintHex;
        _port = port;
        _logger = logger;

        _pipeHost.MessageReceived += OnMessageReceived;
    }

    private void OnMessageReceived(UiToServiceMessage message, PipeClient client)
    {
        switch (message)
        {
            case BeginPairingRequest:
                HandleBeginPairing(client);
                break;

            case CancelPairingRequest:
                _runtime.CancelPairing();
                break;
        }
    }

    private void HandleBeginPairing(PipeClient client)
    {
        if (!client.IsAdministrator())
        {
            _logger.LogWarning("BeginPairingRequest rejected: the connected client is not an administrator.");
            _ = client.SendAsync(new PairingFailed("Требуются права администратора."));
            return;
        }

        var address = NetworkAddressHelper.GetPrimaryLocalIPv4();
        if (address is null)
        {
            _ = client.SendAsync(new PairingFailed("Не удалось определить адрес в локальной сети."));
            return;
        }

        var qr = _runtime.BeginPairing(PairingValidity, _certificateFingerprintHex, $"{address}:{_port}");
        _logger.LogInformation("Pairing mode started; QR valid for {Validity}.", PairingValidity);
        _ = client.SendAsync(new PairingQrIssued(qr.ToQrText(), DateTimeOffset.UtcNow + PairingValidity));
    }

    /// <summary>Called by the HTTP pairing endpoint once a phone actually completes
    /// pairing, so the Setup tool can show success instead of leaving the QR up forever.</summary>
    public void NotifyPairingCompleted(string controllerDisplayName) =>
        _ = _pipeHost.SendAsync(new PairingCompleted(controllerDisplayName));
}
