using WinLock.Core.Models;
using WinLock.Core.Timing;

namespace WinLock.Core;

/// <summary>
/// Core enforcement logic: decides whether the machine should be locked right now.
/// Contains no I/O and no Windows dependency, so it runs and is fully testable on any OS.
/// Must be called periodically (e.g. every few seconds) by the host.
/// </summary>
public sealed class UsageTracker
{
    private static readonly TimeSpan ClockTamperTolerance = TimeSpan.FromSeconds(10);

    private readonly IMonotonicClock _clock;
    private ScheduleConfig _schedule;
    private readonly UsageState _state;

    public UsageTracker(IMonotonicClock clock, ScheduleConfig schedule, UsageState state)
    {
        _clock = clock;
        _schedule = schedule;
        _state = state;
    }

    public UsageState State => _state;

    /// <summary>Replaces the schedule wholesale — a parent pressing "save" is a clean-slate
    /// action, not a merge: today's budget resets to the new daily limit outright, discarding
    /// whatever was used *and* any bonus minutes granted since. Any offline/emergency
    /// schedule-window bypass is discarded too, since it was measured against the old
    /// schedule. A manual lock (see <see cref="SetManualLock"/>) is left alone — that's a
    /// separate, deliberate restriction a schedule edit shouldn't silently undo.</summary>
    public void UpdateSchedule(ScheduleConfig schedule)
    {
        _schedule = schedule;
        _state.BudgetDate = DateOnly.FromDateTime(_clock.UtcNow.ToLocalTime().DateTime);
        _state.RemainingBudget = TimeSpan.FromMinutes(schedule.DailyLimitMinutes);
        _state.ScheduleOverrideUntilUtc = null;
        _state.ClockTamperSuspected = false;
    }

    /// <summary>An explicit, unconditional "lock it now" — always succeeds, regardless of
    /// remaining budget or the current schedule window.</summary>
    public void SetManualLock() => _state.ManuallyLocked = true;

    /// <summary>Explicit "unlock it now". Unlike <see cref="SetManualLock"/> this can fail:
    /// if the budget has hit zero while manually locked, lifting the manual lock would just
    /// hand back a machine that immediately re-locks itself for BudgetExhausted anyway — so
    /// this refuses outright instead of reporting a success the child won't actually see.</summary>
    public bool TryClearManualLock()
    {
        if (_state.RemainingBudget <= TimeSpan.Zero)
            return false;

        _state.ManuallyLocked = false;
        return true;
    }

    /// <summary>Applies a parent-granted time extension — whether from the connected app or
    /// redeemed offline via a QR code. Also clears a tamper flag and any manual lock, since
    /// an authenticated grant of more time is a stronger, more specific signal than either:
    /// nobody would extend time on a device they still want held locked, and a passive drift
    /// heuristic shouldn't outrank a parent who just proved they hold the shared secret.
    /// Grants a matching bypass of the allowed-window check too — see
    /// <see cref="UsageState.ScheduleOverrideUntilUtc"/> for why that's necessary.</summary>
    public void ExtendTime(TimeSpan extra)
    {
        _state.RemainingBudget += extra;
        if (_state.RemainingBudget < TimeSpan.Zero)
            _state.RemainingBudget = TimeSpan.Zero;
        _state.ClockTamperSuspected = false;
        _state.ManuallyLocked = false;

        if (extra > TimeSpan.Zero)
        {
            var overrideUntil = _clock.UtcNow + extra;
            if (_state.ScheduleOverrideUntilUtc is not { } current || overrideUntil > current)
                _state.ScheduleOverrideUntilUtc = overrideUntil;
        }
    }

    public LockDecision Evaluate()
    {
        var nowMonotonicMs = _clock.ElapsedMilliseconds;
        var nowUtc = _clock.UtcNow;
        var isFirstRun = _state.LastRealUtc == default;

        if (isFirstRun)
        {
            _state.BudgetDate = DateOnly.FromDateTime(nowUtc.ToLocalTime().DateTime);
            _state.RemainingBudget = TimeSpan.FromMinutes(_schedule.DailyLimitMinutes);
        }
        else
        {
            var elapsedMonotonic = TimeSpan.FromMilliseconds(Math.Max(0, nowMonotonicMs - _state.LastMonotonicMs));
            var elapsedReal = nowUtc - _state.LastRealUtc;

            // The wall clock should advance in lockstep with the monotonic clock between two
            // evaluations. A mismatch beyond tolerance means the system date/time was changed.
            // Sticky by design: once set, this only clears via ExtendTime (an authenticated
            // command from the phone). Otherwise a child could wind the clock forward, wait
            // out one quiet poll cycle for the flag to self-clear, and ride the resulting
            // "new day" rollover to an unlocked machine with a freshly reset budget.
            if ((elapsedReal - elapsedMonotonic).Duration() > ClockTamperTolerance)
                _state.ClockTamperSuspected = true;

            if (!_state.ClockTamperSuspected)
            {
                var today = DateOnly.FromDateTime(nowUtc.ToLocalTime().DateTime);
                if (_state.BudgetDate != today)
                {
                    // The calendar still rolls over on schedule even while manually locked —
                    // a lock spanning midnight shouldn't hand back a stale, days-old budget
                    // once lifted — but that's the only exception; see the pause below.
                    _state.BudgetDate = today;
                    _state.RemainingBudget = TimeSpan.FromMinutes(_schedule.DailyLimitMinutes);
                }
                else if (!_state.ManuallyLocked)
                {
                    _state.RemainingBudget -= elapsedMonotonic;
                    if (_state.RemainingBudget < TimeSpan.Zero)
                        _state.RemainingBudget = TimeSpan.Zero;
                }
                // While manually locked, the budget simply doesn't move — nothing is being
                // used, so nothing should be spent.
            }
            // While tamper is suspected, the budget is neither decremented nor rolled over —
            // Decide() below locks the machine outright, and it stays locked until cleared.
        }

        _state.LastMonotonicMs = nowMonotonicMs;
        _state.LastRealUtc = nowUtc;

        var decision = Decide(nowUtc.ToLocalTime(), nowUtc);
        _state.IsLocked = decision.ShouldBeLocked;
        return decision;
    }

    private LockDecision Decide(DateTimeOffset localNow, DateTimeOffset nowUtc)
    {
        // An explicit "lock it now" from a parent overrides everything else — including a
        // device that isn't configured yet, which otherwise never locks at all.
        if (_state.ManuallyLocked)
            return new LockDecision(true, LockReason.ManuallyLocked, _state.RemainingBudget);

        // A device a parent has never configured has nothing to enforce yet — locking it
        // anyway would just be an unrecoverable dead end for whoever is setting it up.
        if (!_schedule.IsConfigured)
            return new LockDecision(false, LockReason.None, _state.RemainingBudget);

        if (_state.ClockTamperSuspected)
            return new LockDecision(true, LockReason.ClockTamperSuspected, _state.RemainingBudget);

        if (_state.RemainingBudget <= TimeSpan.Zero)
            return new LockDecision(true, LockReason.BudgetExhausted, _state.RemainingBudget);

        var withinOverride = _state.ScheduleOverrideUntilUtc is { } until && nowUtc < until;
        if (!withinOverride && !_schedule.IsWithinAllowedWindow(localNow))
            return new LockDecision(true, LockReason.OutsideAllowedWindow, _state.RemainingBudget);

        return new LockDecision(false, LockReason.None, _state.RemainingBudget);
    }
}
