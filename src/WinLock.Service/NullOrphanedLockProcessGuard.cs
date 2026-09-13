using WinLock.Service.Security;

namespace WinLock.Service;

/// <summary>Stands in for <see cref="OrphanedLockProcessGuard"/> on non-Windows dev/test hosts,
/// where there's no lock-screen process or session to check.</summary>
public sealed class NullOrphanedLockProcessGuard : IOrphanedLockProcessGuard
{
    public void CheckForOrphanedLockProcess(Func<bool> isLockCurrentlyRequested)
    {
    }
}
