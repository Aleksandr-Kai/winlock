namespace WinLock.Core.Models;

/// <summary>What a <see cref="PendingNotice"/> is about. Adding a new kind of notice a parent
/// needs to be told about (even if no phone was connected at the moment it happened) means
/// adding a value here plus UI copy for it — persistence, delivery, and acknowledgement are
/// all generic over <see cref="PendingNotice"/> and need no per-kind changes.</summary>
public enum NoticeKind
{
    /// <summary>The persisted state file was unreadable and the agent fell back to a fresh,
    /// empty one — see JsonFileStateStore.</summary>
    StateRecovery,

    /// <summary>An administrator stopped the WinLock service directly on the PC.</summary>
    ServiceStopped,

    // Appended, not inserted: this is sent over the wire as a plain integer, so existing
    // values must keep their ordinals.
}
