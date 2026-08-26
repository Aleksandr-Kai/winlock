using WinLock.Core.Models;

namespace WinLock.Core.Locking;

/// <summary>
/// Applies or lifts the actual on-screen lock. The real Windows implementation talks to a
/// UI helper process running in the user's session (a service cannot draw UI directly);
/// this abstraction keeps that entirely out of the enforcement core.
/// </summary>
public interface ILockController
{
    Task LockAsync(LockReason reason, CancellationToken ct = default);

    Task UnlockAsync(CancellationToken ct = default);
}
