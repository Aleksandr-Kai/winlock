using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using WinLock.Core;
using WinLock.Core.Models;
using WinLock.Core.Network;
using WinLock.Service.Screenshots;

namespace WinLock.Service.Network;

/// <summary>
/// Owns every live WebSocket connection from a paired phone. Several parents can be
/// connected at once — each authenticates independently with its own controller secret,
/// each receives every status push, and a command from any one of them is honored (there's
/// no notion of a single "primary" parent). Cross-platform — only the actual screenshot
/// capture it delegates to <see cref="ScreenCaptureCoordinator"/> is Windows-only.
/// </summary>
public sealed class ControllerHub : IAgentStatusPublisher
{
    private static readonly TimeSpan AuthTimeout = TimeSpan.FromSeconds(10);

    private readonly AgentRuntime _runtime;
    private readonly ScreenCaptureCoordinator _screenCapture;
    private readonly ILogger<ControllerHub> _logger;
    private readonly ConcurrentDictionary<Guid, ConnectionState> _connections = new();

    public ControllerHub(AgentRuntime runtime, ScreenCaptureCoordinator screenCapture, ILogger<ControllerHub> logger)
    {
        _runtime = runtime;
        _screenCapture = screenCapture;
        _logger = logger;
    }

    public async Task HandleConnectionAsync(WebSocket socket, CancellationToken ct)
    {
        var connectionId = Guid.NewGuid();
        var state = new ConnectionState { Socket = socket };
        PairedController? controller = null;

        try
        {
            var nonce = ControllerAuthenticator.GenerateNonce();
            await SendAsync(state, new AuthChallenge(nonce), ct);

            using var authCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            authCts.CancelAfter(AuthTimeout);

            ControllerToServerMessage? first;
            try
            {
                first = await ReceiveAsync(socket, authCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return; // auth timed out
            }

            if (first is not AuthResponse authResponse || authResponse.Nonce != nonce)
            {
                await SendAsync(state, new AuthResult(false), ct);
                await CloseAsync(socket, "authentication failed");
                return;
            }

            controller = _runtime.TryAuthenticateController(nonce, authResponse.ControllerId, authResponse.ResponseBase64);
            if (controller is null)
            {
                await SendAsync(state, new AuthResult(false), ct);
                await CloseAsync(socket, "authentication failed");
                return;
            }

            await SendAsync(state, new AuthResult(true), ct);
            _connections[connectionId] = state;
            _logger.LogInformation("Controller '{Name}' ({Id}) connected.", controller.DisplayName, controller.ControllerId);

            await SendAsync(state, BuildStatus(_runtime.Evaluate()), ct); // bring it up to date immediately
            await SendAsync(state, new AgentVersionInfo(AgentVersion.Current), ct);
            await SendAsync(state, new ScheduleSnapshot(_runtime.CurrentSchedule), ct);
            if (_runtime.PendingStateRecoveryIncident is { } incident)
                await SendAsync(state, new StateRecoveryWarning(incident.OccurredAtUtc, incident.Reason), ct);

            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var message = await ReceiveAsync(socket, ct);
                if (message is null)
                    break;

                await HandleMessageAsync(state, message, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
            // network drop — normal, not worth logging as an error
        }
        finally
        {
            _connections.TryRemove(connectionId, out _);
            state.SendLock.Dispose();
            if (controller is not null)
                _logger.LogInformation("Controller '{Name}' ({Id}) disconnected.", controller.DisplayName, controller.ControllerId);
        }
    }

    private async Task HandleMessageAsync(ConnectionState state, ControllerToServerMessage message, CancellationToken ct)
    {
        switch (message)
        {
            case ExtendTimeCommand extend:
                _runtime.ExtendTime(TimeSpan.FromMinutes(extend.Minutes));
                await SendAsync(state, new CommandAck(extend.RequestId, true, null), ct);
                await PublishAsync(_runtime.Evaluate(), ct); // let every parent see the new budget right away
                break;

            case SetRemainingTimeCommand setTime:
                _runtime.SetRemainingTime(TimeSpan.FromMinutes(setTime.Minutes));
                await SendAsync(state, new CommandAck(setTime.RequestId, true, null), ct);
                await PublishAsync(_runtime.Evaluate(), ct);
                break;

            case UpdateScheduleCommand update:
                // Any explicit schedule from an authenticated, paired parent counts as "now
                // configured" — regardless of whether the sender remembered to set the flag
                // itself. See ScheduleConfig.IsConfigured for why this matters.
                update.Schedule.IsConfigured = true;
                _runtime.UpdateSchedule(update.Schedule);
                await SendAsync(state, new CommandAck(update.RequestId, true, null), ct);
                // Every connected parent's app should reflect the new schedule, not just the
                // one that set it — a second phone still showing the old one would be
                // confusing (and could make it look like a save silently failed).
                await BroadcastAsync(new ScheduleSnapshot(_runtime.CurrentSchedule), ct);
                await PublishAsync(_runtime.Evaluate(), ct);
                break;

            case RequestScreenshotCommand screenshot:
                var result = await _screenCapture.CaptureAsync(TimeSpan.FromSeconds(10), ct);
                await SendAsync(state, new ScreenshotResult(
                    screenshot.RequestId,
                    result.Success,
                    result.ErrorMessage,
                    result.Success ? Convert.ToBase64String(result.JpegBytes!) : null,
                    result.Success ? DateTimeOffset.UtcNow : null), ct);
                break;

            case LockNowCommand lockNow:
                _runtime.LockNow();
                await SendAsync(state, new CommandAck(lockNow.RequestId, true, null), ct);
                await PublishAsync(_runtime.Evaluate(), ct);
                break;

            case UnlockNowCommand unlockNow:
                var unlocked = _runtime.TryUnlockNow();
                await SendAsync(state, new CommandAck(
                    unlockNow.RequestId, unlocked,
                    unlocked ? null : "Лимит времени на сегодня исчерпан — разблокировка невозможна."), ct);
                await PublishAsync(_runtime.Evaluate(), ct);
                break;

            case AcknowledgeStateRecoveryCommand acknowledge:
                _runtime.AcknowledgeStateRecoveryIncident();
                await SendAsync(state, new CommandAck(acknowledge.RequestId, true, null), ct);
                break;
        }
    }

    public Task PublishAsync(LockDecision decision, CancellationToken ct = default) =>
        BroadcastAsync(BuildStatus(decision), ct);

    private async Task BroadcastAsync(ServerToControllerMessage message, CancellationToken ct)
    {
        if (_connections.IsEmpty)
            return;

        foreach (var (id, state) in _connections)
        {
            if (state.Socket.State != WebSocketState.Open)
            {
                _connections.TryRemove(id, out _);
                continue;
            }

            try
            {
                await SendAsync(state, message, ct);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                _connections.TryRemove(id, out _);
            }
        }
    }

    private StatusUpdate BuildStatus(LockDecision decision) =>
        new(_runtime.DeviceId, _runtime.DeviceDisplayName, decision.ShouldBeLocked, decision.Reason, decision.RemainingBudget);

    // WebSocket instances allow at most one send and one receive in flight at a time; a
    // per-connection lock keeps a broadcast from colliding with that connection's own reply.
    private static async Task SendAsync(ConnectionState state, ServerToControllerMessage message, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message);
        await state.SendLock.WaitAsync(ct);
        try
        {
            await state.Socket.SendAsync(json, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        finally
        {
            state.SendLock.Release();
        }
    }

    private static async Task<ControllerToServerMessage?> ReceiveAsync(WebSocket socket, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(chunk, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                // Completing the close handshake here matters: without echoing a close
                // frame back, the peer's own CloseAsync sees the connection simply vanish
                // and throws, instead of completing normally.
                if (socket.State == WebSocketState.CloseReceived)
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, ct);
                return null;
            }

            buffer.Write(chunk, 0, result.Count);
        } while (!result.EndOfMessage);

        buffer.Position = 0;
        return await JsonSerializer.DeserializeAsync<ControllerToServerMessage>(buffer, cancellationToken: ct);
    }

    private static async Task CloseAsync(WebSocket socket, string reason)
    {
        if (socket.State == WebSocketState.Open)
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, reason, CancellationToken.None);
    }

    private sealed class ConnectionState
    {
        public required WebSocket Socket { get; init; }
        public SemaphoreSlim SendLock { get; } = new(1, 1);
    }
}
