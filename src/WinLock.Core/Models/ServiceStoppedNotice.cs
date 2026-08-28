namespace WinLock.Core.Models;

/// <summary>Recorded when an administrator stops the WinLock service (e.g. from the Setup
/// tool's "Остановить" button, used to recover a broken pairing). All enforcement is off
/// while it's stopped, so a parent should know even if nobody's phone was connected at that
/// moment — sent on every connect until a parent acknowledges it, same as
/// StateRecoveryIncident.</summary>
public sealed record ServiceStoppedNotice(DateTimeOffset OccurredAtUtc, string Reason);
