using System.Text.Json;
using WinLock.Core.Models;

namespace WinLock.Core.State;

/// <summary>
/// Persists agent state as an encrypted-at-rest JSON blob. Writes are atomic (write to a
/// temp file, then rename over the target) so a crash or power loss mid-write can never
/// leave a corrupt or half-written state file behind.
/// </summary>
public sealed class JsonFileStateStore : IStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    private readonly string _filePath;
    private readonly IStateProtector _protector;

    public JsonFileStateStore(string filePath, IStateProtector protector)
    {
        _filePath = filePath;
        _protector = protector;
    }

    public async Task<AgentPersistedData> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return new AgentPersistedData();

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(_filePath, ct);
            var plaintext = _protector.Unprotect(protectedBytes);
            return JsonSerializer.Deserialize<AgentPersistedData>(plaintext, SerializerOptions)
                   ?? new AgentPersistedData();
        }
        catch (Exception) when (ct.IsCancellationRequested == false)
        {
            // Corrupt, tampered, or unreadable state file: fail safe by starting from a
            // fresh, empty budget rather than crashing the service or granting free time.
            return new AgentPersistedData();
        }
    }

    public async Task SaveAsync(AgentPersistedData data, CancellationToken ct = default)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(data, SerializerOptions);
        var protectedBytes = _protector.Protect(plaintext);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = _filePath + ".tmp";
        await File.WriteAllBytesAsync(tempPath, protectedBytes, ct);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
