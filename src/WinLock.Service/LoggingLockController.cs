using WinLock.Core.Locking;
using WinLock.Core.Models;

namespace WinLock.Service;

/// <summary>
/// Placeholder <see cref="ILockController"/> that only logs. Stands in until the real
/// implementation is built: a UI helper process (WPF), launched by the service into the
/// active user session and driven over a named pipe, that shows the full-screen block
/// window and installs low-level keyboard hooks to suppress Alt+Tab/Alt+F4/Win while locked.
/// </summary>
public sealed class LoggingLockController : ILockController
{
    private readonly ILogger<LoggingLockController> _logger;

    public LoggingLockController(ILogger<LoggingLockController> logger)
    {
        _logger = logger;
    }

    public Task LockAsync(LockReason reason, CancellationToken ct = default)
    {
        _logger.LogWarning("LOCK requested. Reason: {Reason}", reason);
        return Task.CompletedTask;
    }

    public Task UnlockAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("UNLOCK requested.");
        return Task.CompletedTask;
    }
}
