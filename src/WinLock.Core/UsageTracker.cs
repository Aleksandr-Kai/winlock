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

    // Environment.TickCount64 (what the monotonic clock is backed by) is NOT paused by sleep
    // — only a full reboot resets it — so a long sleep, or the service itself being stopped
    // and restarted later, would otherwise look identical to hours of continuous active use.
    // EnforcementWorker calls Evaluate() roughly every 5 seconds while the machine is
    // genuinely awake and the service is genuinely running, so a gap far larger than that
    // between two consecutive calls can only mean it wasn't. 30s is generous relative to that
    // real cadence (tolerates an occasional slow tick — GC, a busy CPU, whatever) while still
    // being trivially short next to any real sleep or outage.
    private static readonly TimeSpan MaxChargeablePerEvaluation = TimeSpan.FromSeconds(30);

    /// <summary>How many days of <see cref="UsageState.History"/> to keep — old entries are
    /// dropped rather than kept forever.</summary>
    private const int MaxHistoryDays = 30;

    private readonly IMonotonicClock _clock;
    private ScheduleConfig _schedule;
    private readonly UsageState _state;

    /// <summary>Fired exactly when a new daily budget is granted — a fresh install's first
    /// Evaluate(), an ordinary midnight rollover, or a parent saving a new schedule (see
    /// <see cref="UpdateSchedule"/>) — with the date it's for and how many minutes were
    /// granted. Purely for logging/diagnostics on the host side; nothing in the enforcement
    /// logic itself depends on this firing. Deliberately not raised by <see cref="ExtendTime"/>
    /// or <see cref="SetRemainingBudget"/> — those are an ad-hoc top-up, not a new limit
    /// period starting.</summary>
    public event Action<DateOnly, TimeSpan>? DailyBudgetGranted;

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
        GrantDailyBudget(DateOnly.FromDateTime(_clock.UtcNow.ToLocalTime().DateTime));
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

    /// <summary>Sets today's remaining budget to an exact value, rather than adding an
    /// increment on top of whatever's currently left — a parent picking "6 hours left" on a
    /// clock face rather than tapping +30 repeatedly. Otherwise mirrors <see cref="ExtendTime"/>:
    /// clears a tamper flag and any manual lock, and grants a matching allowed-window bypass,
    /// since an authenticated, explicit grant of a specific amount of time right now is just
    /// as strong a signal here as it is there.</summary>
    public void SetRemainingBudget(TimeSpan value)
    {
        _state.RemainingBudget = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        _state.ClockTamperSuspected = false;
        _state.ManuallyLocked = false;

        if (_state.RemainingBudget > TimeSpan.Zero)
        {
            var overrideUntil = _clock.UtcNow + _state.RemainingBudget;
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
            GrantDailyBudget(DateOnly.FromDateTime(nowUtc.ToLocalTime().DateTime));
        }
        else
        {
            // Environment.TickCount64 can only ever climb within one boot session — it has
            // nothing to do with the wall clock, so nothing short of an actual restart can
            // make it read lower than the last time this ran. That makes it an unambiguous
            // "the machine was genuinely off" signal, distinct from a child winding the wall
            // clock forward (which leaves the monotonic clock moving normally).
            var rebooted = nowMonotonicMs < _state.LastMonotonicMs;
            var elapsedMonotonic = TimeSpan.FromMilliseconds(Math.Max(0, nowMonotonicMs - _state.LastMonotonicMs));
            var elapsedReal = nowUtc - _state.LastRealUtc;

            // The wall clock should advance in lockstep with the monotonic clock between two
            // evaluations. A mismatch beyond tolerance means the system date/time was changed
            // -- except right after a genuine reboot, where elapsedMonotonic is clamped to
            // (near) zero while elapsedReal reflects however long the machine was actually
            // off; skip the check there, or every ordinary overnight shutdown would falsely
            // trip it. Sticky by design otherwise: once set, this only clears via ExtendTime
            // (an authenticated command from the phone). Otherwise a child could wind the
            // clock forward, wait out one quiet poll cycle for the flag to self-clear, and
            // ride the resulting "new day" rollover to an unlocked machine with a freshly
            // reset budget.
            if (!rebooted && (elapsedReal - elapsedMonotonic).Duration() > ClockTamperTolerance)
                _state.ClockTamperSuspected = true;

            if (!_state.ClockTamperSuspected)
            {
                var today = DateOnly.FromDateTime(nowUtc.ToLocalTime().DateTime);
                if (_state.BudgetDate != today)
                {
                    // The calendar still rolls over on schedule even while manually locked —
                    // a lock spanning midnight shouldn't hand back a stale, days-old budget
                    // once lifted — but that's the only exception; see the pause below.
                    GrantDailyBudget(today);
                }
                else if (!_state.ManuallyLocked)
                {
                    var chargeable = elapsedMonotonic > MaxChargeablePerEvaluation
                        ? MaxChargeablePerEvaluation
                        : elapsedMonotonic;

                    // EnforcementWorker keeps ticking every ~5s regardless of whether the
                    // machine is currently locked -- BudgetExhausted is just a Decide() result,
                    // not something that pauses this loop. Once RemainingBudget has already
                    // hit zero, `chargeable` would otherwise still get recorded as if it were
                    // real usage, even though nothing was actually spent (the budget itself
                    // stays clamped at zero) -- for a session left open at the lock screen for
                    // hours after running out, History would silently grow to reflect that
                    // whole span instead of the ~daily limit actually used. Record only what's
                    // genuinely deducted.
                    var actuallyDeducted = chargeable < _state.RemainingBudget ? chargeable : _state.RemainingBudget;
                    if (actuallyDeducted < TimeSpan.Zero)
                        actuallyDeducted = TimeSpan.Zero;

                    _state.RemainingBudget -= actuallyDeducted;
                    if (_state.RemainingBudget < TimeSpan.Zero)
                        _state.RemainingBudget = TimeSpan.Zero;
                    RecordUsage(today, actuallyDeducted);
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

    /// <summary>Resets the budget to the current schedule's daily limit for the given date
    /// and announces it via <see cref="DailyBudgetGranted"/> — the one place all three
    /// "a new limit period starts now" call sites (first run, midnight rollover, an explicit
    /// schedule save) actually apply the change, so they can't drift out of sync with each
    /// other or forget to raise the event.</summary>
    private void GrantDailyBudget(DateOnly date)
    {
        _state.BudgetDate = date;
        _state.RemainingBudget = TimeSpan.FromMinutes(_schedule.DailyLimitMinutes);
        DailyBudgetGranted?.Invoke(date, _state.RemainingBudget);
    }

    /// <summary>Adds to today's history entry (creating it if this is the first charge of the
    /// day), then trims anything older than <see cref="MaxHistoryDays"/>. Only ever called
    /// with genuinely chargeable time — see the caller in <see cref="Evaluate"/> — so this
    /// never needs to handle a zero-or-negative amount.</summary>
    private void RecordUsage(DateOnly date, TimeSpan amount)
    {
        // The caller now only ever passes what was genuinely deducted from RemainingBudget
        // (see Evaluate), which is exactly zero once the budget's already exhausted for the
        // day -- skip those rather than create a pointless zero-length entry for a day that
        // otherwise saw no use at all.
        if (amount <= TimeSpan.Zero)
            return;

        var index = _state.History.FindIndex(r => r.Date == date);
        if (index >= 0)
            _state.History[index] = _state.History[index] with { UsedTime = _state.History[index].UsedTime + amount };
        else
            _state.History.Add(new DailyUsageRecord(date, amount));

        if (_state.History.Count > MaxHistoryDays)
            _state.History = _state.History.OrderBy(r => r.Date).TakeLast(MaxHistoryDays).ToList();
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
