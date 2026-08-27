namespace WinLock.Core.Models;

/// <summary>Recorded when the agent had to fall back to a fresh, empty state because the
/// previously persisted one was unreadable (see JsonFileStateStore). Kept inside the
/// persisted state itself, not just logged locally, so it reaches a parent's phone even if
/// none was connected at the moment it happened — sent on every connect until a parent
/// acknowledges it, which is the only thing that clears it.</summary>
public sealed record StateRecoveryIncident(DateTimeOffset OccurredAtUtc, string Reason);
