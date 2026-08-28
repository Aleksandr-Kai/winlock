namespace WinLock.Core.Models;

/// <summary>A notice that survives until a parent acknowledges it from the phone — the only
/// thing that clears it, since it's meant to reach a parent even if no phone was connected at
/// the moment it actually happened. Kept inside the persisted state itself, not just logged
/// locally, so it survives a service restart.</summary>
public sealed record PendingNotice(NoticeKind Kind, DateTimeOffset OccurredAtUtc, string Reason);
