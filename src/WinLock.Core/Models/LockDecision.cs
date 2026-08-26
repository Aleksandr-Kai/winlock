namespace WinLock.Core.Models;

public enum LockReason
{
    None,
    OutsideAllowedWindow,
    BudgetExhausted,
    ClockTamperSuspected,
    // Appended, not inserted: this is sent over the wire as a plain integer, so existing
    // values must keep their ordinals.
    ManuallyLocked,
}

public sealed record LockDecision(bool ShouldBeLocked, LockReason Reason, TimeSpan RemainingBudget);
