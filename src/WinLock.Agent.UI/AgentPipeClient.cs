using System.IO;
using System.IO.Pipes;
using WinLock.Core.Ipc;

namespace WinLock.Agent.UI;

/// <summary>
/// Connects to the service's named pipe and exposes incoming messages as an event, marshalled
/// onto whatever thread calls <see cref="Start"/> is irrelevant — callers must dispatch
/// <see cref="MessageReceived"/> to the UI thread themselves.
/// </summary>
public sealed class AgentPipeClient : IDisposable
{
    public event Action<ServiceToUiMessage>? MessageReceived;
    public event Action? Disconnected;

    private NamedPipeClientStream? _pipe;
    private NdjsonChannel<ServiceToUiMessage, UiToServiceMessage>? _channel;
    private CancellationTokenSource? _cts;

    public async Task<bool> ConnectAsync(TimeSpan timeout, CancellationToken ct)
    {
        var pipe = new NamedPipeClientStream(".", IpcEndpoints.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync((int)timeout.TotalMilliseconds, ct);
        }
        catch (TimeoutException)
        {
            pipe.Dispose();
            return false;
        }

        _pipe = pipe;
        _channel = new NdjsonChannel<ServiceToUiMessage, UiToServiceMessage>(pipe);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = Task.Run(() => ReadLoopAsync(_cts.Token), CancellationToken.None);
        return true;
    }

    public Task SendAsync(UiToServiceMessage message, CancellationToken ct = default) =>
        _channel?.WriteAsync(message, ct) ?? Task.CompletedTask;

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var message = await _channel!.ReadAsync(ct);
                if (message is null)
                    break; // pipe closed by the service

                MessageReceived?.Invoke(message);
            }
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // shutting down; nothing to do
        }
        catch (IOException)
        {
            // service went away (e.g. it stopped) — surface as a disconnect
        }

        Disconnected?.Invoke();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _channel?.Dispose();
        _pipe?.Dispose();
    }
}
