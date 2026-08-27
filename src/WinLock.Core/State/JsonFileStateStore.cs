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
        catch (Exception ex) when (ct.IsCancellationRequested == false)
        {
            // Corrupt, tampered, or unreadable state file: fail safe by starting from a
            // fresh, empty state rather than crashing the service or granting free time.
            // Critically, never let that be a SILENT reset — the very next SaveAsync would
            // otherwise overwrite this file with the blank state, permanently destroying the
            // schedule, pairing, and certificate all at once with no trace that anything was
            // ever there. Preserve the unreadable bytes under a sibling name and leave a
            // plain-text breadcrumb explaining why, before handing back the fresh default.
            var reason = TryPreserveUnreadableFile(ex);
            return new AgentPersistedData
            {
                PendingStateRecoveryIncident = new StateRecoveryIncident(DateTimeOffset.UtcNow, reason),
            };
        }
    }

    private string TryPreserveUnreadableFile(Exception cause)
    {
        var reason = $"{cause.GetType().Name}: {cause.Message}";
        if (reason.Length > 300)
            reason = reason[..300];

        try
        {
            var backupPath = $"{_filePath}.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            File.Copy(_filePath, backupPath, overwrite: true);
            File.AppendAllText(
                _filePath + ".errors.log",
                $"{DateTime.UtcNow:O} — could not read/decrypt state; starting from a fresh, " +
                $"empty state instead. Unreadable file backed up to {Path.GetFileName(backupPath)}. " +
                $"Cause: {reason}{Environment.NewLine}");
        }
        catch
        {
            // Best-effort diagnostics only — must never let a failure here mask the original
            // fail-safe fallback or crash the service.
        }

        return reason;
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
