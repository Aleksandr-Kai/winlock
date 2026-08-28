using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using WinLock.Core.Ipc;

namespace WinLock.Service.Ipc;

/// <summary>
/// Accepts connections from UI helper processes and relays messages both ways. Two UI
/// processes can legitimately be connected at once — the lock screen (always, while the
/// machine is locked) and the Setup tool (only while an admin has it open, e.g. to re-pair a
/// phone directly from the lock screen) — so this accepts and services any number of
/// concurrent clients rather than assuming there's only ever one.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NamedPipeServerHost : IAsyncDisposable
{
    public event Action<UiToServiceMessage, PipeClient>? MessageReceived;
    public event Action? ClientConnected;

    private readonly ILogger<NamedPipeServerHost> _logger;
    private readonly List<PipeClient> _clients = [];
    private readonly object _clientsGate = new();
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed while waiting for a pipe client to connect; retrying.");
                server.Dispose();
                continue;
            }

            var client = new PipeClient(server, _logger);
            lock (_clientsGate)
                _clients.Add(client);
            ClientConnected?.Invoke();

            // Service this client on its own task so the accept loop can immediately go back
            // to CreatePipeServer/WaitForConnectionAsync for the *next* client instead of
            // being tied up for as long as this one stays connected (the lock screen, in
            // particular, stays connected for as long as the machine is locked — which used
            // to mean nothing else could ever connect while it was).
            _ = ServiceClientAsync(client, ct);
        }
    }

    private async Task ServiceClientAsync(PipeClient client, CancellationToken ct)
    {
        // This must never throw back into the accept loop's caller — an exception here used
        // to permanently kill the whole accept loop (started via a fire-and-forget Task.Run,
        // so nothing observed it either): one bad message from one client and the pipe
        // silently stopped accepting *any* connection for the rest of the service's uptime,
        // with the Windows Service itself still showing "Running" the whole time.
        try
        {
            await ReadFromClientAsync(client, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unhandled error while servicing a pipe client; dropping this connection.");
        }
        finally
        {
            lock (_clientsGate)
                _clients.Remove(client);
            client.Dispose();
        }
    }

    private async Task ReadFromClientAsync(PipeClient client, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var message = await client.Channel.ReadAsync(ct);
                if (message is null)
                    return; // UI helper disconnected

                try
                {
                    MessageReceived?.Invoke(message, client);
                }
                catch (Exception ex)
                {
                    // A bug in one message's handler (pairing, offline-unlock, whatever) must
                    // not cost this client — or any other client — the whole connection. Log
                    // it and keep reading the next message.
                    _logger.LogError(ex, "Unhandled error handling a {MessageType} message from a pipe client.", message.GetType().Name);
                }
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

    /// <summary>Best-effort broadcast to every currently connected client. Each UI helper's
    /// own message-handling switch already ignores message types meant for a different
    /// client (e.g. the lock screen ignores pairing messages), so sending to everyone is
    /// simpler than tracking which specific client a given outgoing message is "for" and
    /// behaves identically in practice.</summary>
    public async Task SendAsync(ServiceToUiMessage message, CancellationToken ct = default)
    {
        PipeClient[] snapshot;
        lock (_clientsGate)
            snapshot = [.. _clients];

        foreach (var client in snapshot)
            await client.TrySendAsync(message, ct);
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

        // NamedPipeServerStream.MaxAllowedServerInstances lets Windows hand out as many
        // concurrent instances of this pipe name as it will allow, so a second (or third) UI
        // helper connecting doesn't have to wait for an earlier one to disconnect first.
        return NamedPipeServerStreamAcl.Create(
            IpcEndpoints.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
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

        PipeClient[] snapshot;
        lock (_clientsGate)
            snapshot = [.. _clients];
        foreach (var client in snapshot)
            client.Dispose();
    }
}

/// <summary>One connected UI helper process. Exposed to <see cref="NamedPipeServerHost.MessageReceived"/>
/// so a handler can reply to (or check the identity of) specifically the client that sent a
/// given message, rather than whichever client happened to be connected most recently.</summary>
[SupportedOSPlatform("windows")]
public sealed class PipeClient : IDisposable
{
    internal readonly NdjsonChannel<UiToServiceMessage, ServiceToUiMessage> Channel;

    private readonly NamedPipeServerStream _server;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    internal PipeClient(NamedPipeServerStream server, ILogger logger)
    {
        _server = server;
        _logger = logger;
        Channel = new NdjsonChannel<UiToServiceMessage, ServiceToUiMessage>(server);
    }

    public async Task SendAsync(ServiceToUiMessage message, CancellationToken ct = default)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            await Channel.WriteAsync(message, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Same as <see cref="SendAsync"/> but swallows a write failure instead of
    /// throwing — used by <see cref="NamedPipeServerHost.SendAsync"/>'s broadcast, where one
    /// client having vanished mid-write must not stop the message reaching everyone else.</summary>
    internal async Task TrySendAsync(ServiceToUiMessage message, CancellationToken ct)
    {
        try
        {
            await SendAsync(message, ct);
        }
        catch (IOException)
        {
            // client vanished mid-write; ServiceClientAsync will notice and clean it up
        }
        catch (ObjectDisposedException)
        {
            // client was already cleaned up between the broadcast snapshot and this send
        }
    }

    /// <summary>Defense in depth: the Setup tool that sends <c>BeginPairingRequest</c> is
    /// meant to require an elevation prompt to even launch, but a service handling a
    /// security-relevant request should not rely solely on a client-side check it can't
    /// verify. This impersonates the connected client to check its token directly.</summary>
    public bool IsAdministrator()
    {
        if (!_server.IsConnected)
            return false;

        try
        {
            var isAdmin = false;
            _server.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent();
                isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            });
            return isAdmin;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify a connected pipe client's identity.");
            return false;
        }
    }

    public void Dispose()
    {
        Channel.Dispose();
        _server.Dispose();
        _sendLock.Dispose();
    }
}
