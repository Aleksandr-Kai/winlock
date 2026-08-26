using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using WinLock.Core.Ipc;

namespace WinLock.Service.Ipc;

/// <summary>
/// Accepts connections from the (single, transient) lock-screen UI helper process and
/// relays messages both ways. Only one UI instance is ever expected at a time — the service
/// only launches it while locking — so this keeps at most one active connection.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NamedPipeServerHost : IAsyncDisposable
{
    public event Action<UiToServiceMessage>? MessageReceived;
    public event Action? ClientConnected;

    private readonly ILogger<NamedPipeServerHost> _logger;
    private NdjsonChannel<UiToServiceMessage, ServiceToUiMessage>? _currentChannel;
    private NamedPipeServerStream? _currentServer;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public NamedPipeServerHost(ILogger<NamedPipeServerHost> logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = CreatePipeServer();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create named pipe server; retrying in 5s.");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException)
            {
                server.Dispose();
                break;
            }

            var channel = new NdjsonChannel<UiToServiceMessage, ServiceToUiMessage>(server);
            await _sendLock.WaitAsync(ct);
            _currentChannel = channel;
            _currentServer = server;
            _sendLock.Release();
            ClientConnected?.Invoke();

            await ReadFromClientAsync(channel, ct);

            await _sendLock.WaitAsync(CancellationToken.None);
            if (ReferenceEquals(_currentChannel, channel))
            {
                _currentChannel = null;
                _currentServer = null;
            }
            _sendLock.Release();

            channel.Dispose();
            server.Dispose();
        }
    }

    private async Task ReadFromClientAsync(NdjsonChannel<UiToServiceMessage, ServiceToUiMessage> channel, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var message = await channel.ReadAsync(ct);
                if (message is null)
                    return; // UI helper disconnected

                MessageReceived?.Invoke(message);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
            // client vanished — normal when the UI process exits or is killed
        }
    }

    /// <summary>Best-effort: silently drops the message if no UI client is currently
    /// connected (e.g. between the service deciding to lock and the UI finishing startup).</summary>
    public async Task SendAsync(ServiceToUiMessage message, CancellationToken ct = default)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            if (_currentChannel is null)
                return;

            await _currentChannel.WriteAsync(message, ct);
        }
        catch (IOException)
        {
            // client vanished mid-write; the accept loop will notice and clean up
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Defense in depth: the Setup tool that sends <c>BeginPairingRequest</c> is
    /// meant to require an elevation prompt to even launch, but a service handling a
    /// security-relevant request should not rely solely on a client-side check it can't
    /// verify. This impersonates the connected client to check its token directly.</summary>
    public bool IsCurrentClientAdministrator()
    {
        var server = _currentServer;
        if (server is null || !server.IsConnected)
            return false;

        try
        {
            var isAdmin = false;
            server.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent();
                isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            });
            return isAdmin;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify the connected pipe client's identity.");
            return false;
        }
    }

    private static NamedPipeServerStream CreatePipeServer()
    {
        var security = new PipeSecurity();

        // The service (SYSTEM) always needs access; the signed-in user account is who
        // actually needs to talk to it day to day. Nobody else gets a rule at all, which
        // on Windows means nobody else can open the pipe.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            IpcEndpoints.PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security);
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; }
            catch (OperationCanceledException) { }
        }

        _currentChannel?.Dispose();
    }
}
