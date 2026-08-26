namespace WinLock.Core.Models;

/// <summary>
/// Persisted, locally-owned state of the agent. Everything needed to keep enforcing
/// the schedule survives a reboot and requires no network connectivity to evaluate.
/// </summary>
public sealed class UsageState
{
    /// <summary>Calendar date (local) this budget applies to; reset at local midnight.</summary>
    public DateOnly BudgetDate { get; set; }

    /// <summary>Remaining allowed usage time for <see cref="BudgetDate"/>.</summary>
    public TimeSpan RemainingBudget { get; set; }

    /// <summary>Monotonic clock reading (ms) captured the last time state was persisted.</summary>
    public long LastMonotonicMs { get; set; }

    /// <summary>Wall-clock UTC time captured the last time state was persisted.</summary>
    public DateTimeOffset LastRealUtc { get; set; }

    /// <summary>Set when the wall clock and the monotonic clock disagree beyond tolerance.</summary>
    public bool ClockTamperSuspected { get; set; }

    /// <summary>While set and in the future, bypasses the allowed-window check specifically
    /// (budget and tamper checks still apply). Set by ExtendTime: granting more minutes
    /// while the machine is locked for being outside the schedule window must actually let
    /// it be used for that long, not just top up a budget the window check would still
    /// block from being spent.</summary>
    public DateTimeOffset? ScheduleOverrideUntilUtc { get; set; }

    /// <summary>True while the machine is currently locked out.</summary>
    public bool IsLocked { get; set; }

    /// <summary>An explicit "lock it now" from a parent — overrides schedule and budget
    /// entirely, in either direction: set, it locks even with time remaining and inside the
    /// allowed window; while set, the budget also stops ticking down, since nothing is
    /// actually being used. Cleared only by an explicit unlock, which itself is refused if
    /// the budget has since hit zero.</summary>
    public bool ManuallyLocked { get; set; }
}
