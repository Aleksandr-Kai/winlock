using System.Text;
using System.Text.Json;

namespace WinLock.Core.Ipc;

/// <summary>
/// One message per line, JSON-encoded, over a duplex stream (a named pipe in production).
/// <typeparamref name="TIn"/> is the type this endpoint reads, <typeparamref name="TOut"/>
/// what it writes — the service and the UI helper each instantiate this with the two
/// message hierarchies swapped.
/// </summary>
public sealed class NdjsonChannel<TIn, TOut> : IDisposable
{
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;

    public NdjsonChannel(Stream stream)
    {
        _reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            AutoFlush = true,
        };
    }

    public async Task WriteAsync(TOut message, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message);
        await _writer.WriteLineAsync(json.AsMemory(), ct);
    }

    /// <summary>Returns null when the pipe was closed by the other end.</summary>
    public async Task<TIn?> ReadAsync(CancellationToken ct = default)
    {
        var line = await _reader.ReadLineAsync(ct);
        return line is null ? default : JsonSerializer.Deserialize<TIn>(line);
    }

    public void Dispose()
    {
        _reader.Dispose();
        _writer.Dispose();
    }
}
