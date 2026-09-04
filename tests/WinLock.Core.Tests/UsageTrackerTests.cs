using WinLock.Core.Models;

namespace WinLock.Core.Tests;

public class UsageTrackerTests
{
    private static readonly DateTimeOffset StartUtc = new(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);

    private static ScheduleConfig FullDaySchedule(int dailyLimitMinutes) => new()
    {
        IsConfigured = true,
        DailyLimitMinutes = dailyLimitMinutes,
        AllowedWindows = new Dictionary<DayOfWeek, List<TimeWindow>>
        {
            [StartUtc.DayOfWeek] = new() { new TimeWindow(TimeOnly.MinValue, TimeOnly.MaxValue) },
        },
    };

    private static (UsageTracker tracker, FakeClock clock) Build(ScheduleConfig schedule)
    {
        var clock = new FakeClock(StartUtc);
        var tracker = new UsageTracker(clock, schedule, new UsageState());
        return (tracker, clock);
    }

    /// <summary>Advances through genuinely continuous active use by calling Evaluate() every
    /// 5 seconds — the real cadence EnforcementWorker actually polls at — rather than one
    /// unrealistically large single jump, which UsageTracker's per-evaluation charge cap
    /// would otherwise clip (exactly as it's meant to for a real gap that size, e.g. sleep;
    /// see the Evaluate_CapsChargeableTime... tests below). Returns the final decision.</summary>
    private static LockDecision TickThroughActiveUse(UsageTracker tracker, FakeClock clock, TimeSpan totalActiveTime)
    {
        var step = TimeSpan.FromSeconds(5);
        var remaining = totalActiveTime;
        LockDecision? decision = null;
        while (remaining > TimeSpan.Zero)
        {
            var thisStep = remaining < step ? remaining : step;
            clock.Advance(thisStep);
            decision = tracker.Evaluate();
            remaining -= thisStep;
        }

        return decision ?? tracker.Evaluate();
    }

    [Fact]
    public void Evaluate_NeverLocks_OnAnUnconfiguredDevice_RegardlessOfScheduleOrBudget()
    {
        // A freshly installed, unpaired device has an empty AllowedWindows dictionary,
        // which would otherwise mean "never allowed, every day" — an unrecoverable dead end
        // for whoever is trying to pair it. IsConfigured defaults to false specifically to
        // prevent that.
        var (tracker, clock) = Build(new ScheduleConfig());

        var first = tracker.Evaluate();
        clock.Advance(TimeSpan.FromDays(3));
        var later = tracker.Evaluate();

        Assert.False(first.ShouldBeLocked);
        Assert.False(later.ShouldBeLocked);
    }

    [Fact]
    public void FirstEvaluate_DoesNotConsumeBudget_AndInitializesFullDailyLimit()
    {
        var (tracker, _) = Build(FullDaySchedule(dailyLimitMinutes: 60));

        var decision = tracker.Evaluate();

        Assert.False(decision.ShouldBeLocked);
        Assert.Equal(TimeSpan.FromMinutes(60), decision.RemainingBudget);
    }

    [Fact]
    public void Evaluate_ConsumesBudget_ByElapsedMonotonicTime()
    {
        var (tracker, clock) = Build(FullDaySchedule(dailyLimitMinutes: 60));
        tracker.Evaluate(); // establishes anchors

        var decision = TickThroughActiveUse(tracker, clock, TimeSpan.FromMinutes(20));

        Assert.False(decision.ShouldBeLocked);
        Assert.Equal(TimeSpan.FromMinutes(40), decision.RemainingBudget);
    }

    [Fact]
    public void Evaluate_LocksMachine_WhenBudgetExhausted()
    {
        var (tracker, clock) = Build(FullDaySchedule(dailyLimitMinutes: 30));
        tracker.Evaluate();

        var decision = TickThroughActiveUse(tracker, clock, TimeSpan.FromMinutes(45));

        Assert.True(decision.ShouldBeLocked);
        Assert.Equal(LockReason.BudgetExhausted, decision.Reason);
        Assert.Equal(TimeSpan.Zero, decision.RemainingBudget);
    }

    [Fact]
    public void Evaluate_LocksMachine_WhenOutsideAllowedWindow()
    {
        var schedule = new ScheduleConfig
        {
            IsConfigured = true,
            DailyLimitMinutes = 120,
            AllowedWindows = new Dictionary<DayOfWeek, List<TimeWindow>>
            {
                // Window that does not contain StartUtc's time-of-day (10:00).
                [StartUtc.DayOfWeek] = new() { new TimeWindow(new TimeOnly(18, 0), new TimeOnly(20, 0)) },
            },
        };
        var (tracker, _) = Build(schedule);

        var decision = tracker.Evaluate();

        Assert.True(decision.ShouldBeLocked);
        Assert.Equal(LockReason.OutsideAllowedWindow, decision.Reason);
    }

    [Fact]
    public void ExtendTime_ActuallyUnlocksAMachineThatWasLockedForBeingOutsideTheWindow()
    {
        // Regression test: ExtendTime used to only top up RemainingBudget. If the lock
        // reason was "outside the allowed window" rather than "budget exhausted", that
        // budget could never actually be spent — Decide() would immediately re-lock on the
        // very next Evaluate(), a few seconds later, with the same OutsideAllowedWindow
        // reason. Granting more time has to actually grant usable time.
        var schedule = new ScheduleConfig
        {
            IsConfigured = true,
            DailyLimitMinutes = 120,
            AllowedWindows = new Dictionary<DayOfWeek, List<TimeWindow>>
            {
                [StartUtc.DayOfWeek] = new() { new TimeWindow(new TimeOnly(18, 0), new TimeOnly(20, 0)) },
            },
        };
        var (tracker, clock) = Build(schedule);
        var before = tracker.Evaluate();
        Assert.True(before.ShouldBeLocked);
        Assert.Equal(LockReason.OutsideAllowedWindow, before.Reason);

        tracker.ExtendTime(TimeSpan.FromMinutes(30));
        var justAfterGrant = tracker.Evaluate();

        Assert.False(justAfterGrant.ShouldBeLocked);

        // A few seconds later — same "next poll cycle" that exposed the bug — it must
        // still be unlocked, not flip back to locked again.
        clock.Advance(TimeSpan.FromSeconds(5));
        var aFewSecondsLater = tracker.Evaluate();
        Assert.False(aFewSecondsLater.ShouldBeLocked);

        // Once the granted window has actually elapsed, the schedule reasserts itself.
        clock.Advance(TimeSpan.FromMinutes(30));
        var afterOverrideExpires = tracker.Evaluate();
        Assert.True(afterOverrideExpires.ShouldBeLocked);
        Assert.Equal(LockReason.OutsideAllowedWindow, afterOverrideExpires.Reason);
    }

    [Fact]
    public void Evaluate_DetectsAndLocks_WhenSystemClockJumpsForward_WithoutMonotonicTimePassing()
    {
        var (tracker, clock) = Build(FullDaySchedule(dailyLimitMinutes: 60));
        tracker.Evaluate();

        // Child sets the Windows clock forward by a day, hoping to "skip" an out-of-window
        // block or make the schedule think a new day started. Monotonic time barely moves.
        clock.Advance(TimeSpan.FromSeconds(1));
        clock.JumpWallClockOnly(TimeSpan.FromDays(1));

        var decision = tracker.Evaluate();

        Assert.True(decision.ShouldBeLocked);
        Assert.Equal(LockReason.ClockTamperSuspected, decision.Reason);
    }

    [Fact]
    public void Evaluate_StaysLockedOnTamper_EvenAfterDriftSettlesOnANextQuietCycle()
    {
        // Regression test for an exploit: jump the clock forward a day, then let one
        // ordinary poll cycle pass (small, matching elapsed-real/elapsed-monotonic delta)
        // hoping the tamper flag self-clears and the resulting "new day" rolls the budget
        // over to full with the machine unlocked.
        var (tracker, clock) = Build(FullDaySchedule(dailyLimitMinutes: 60));
        tracker.Evaluate();

        clock.Advance(TimeSpan.FromSeconds(1));
        clock.JumpWallClockOnly(TimeSpan.FromDays(1));
        var tamperedDecision = tracker.Evaluate();
        Assert.Equal(LockReason.ClockTamperSuspected, tamperedDecision.Reason);

        clock.Advance(TimeSpan.FromSeconds(5)); // quiet cycle: both clocks move together now
        var nextDecision = tracker.Evaluate();

        Assert.True(nextDecision.ShouldBeLocked);
        Assert.Equal(LockReason.ClockTamperSuspected, nextDecision.Reason);
        Assert.Equal(TimeSpan.FromMinutes(60), nextDecision.RemainingBudget); // untouched, no bogus rollover
    }

    [Fact]
    public void ExtendTime_AddsBudget_AndClearsTamperFlag()
    {
        var (tracker, clock) = Build(FullDaySchedule(dailyLimitMinutes: 10));
        tracker.Evaluate();
        clock.Advance(TimeSpan.FromSeconds(1));
        clock.JumpWallClockOnly(TimeSpan.FromDays(1));
        tracker.Evaluate();
        Assert.True(tracker.State.ClockTamperSuspected);

        tracker.ExtendTime(TimeSpan.FromMinutes(15));

        Assert.False(tracker.State.ClockTamperSuspected);
        Assert.Equal(TimeSpan.FromMinutes(25), tracker.State.RemainingBudget);
    }

    [Fact]
    public void SetRemainingBudget_ReplacesTheBudgetOutright_RatherThanAddingToIt()
    {
        var (tracker, _) = Build(FullDaySchedule(dailyLimitMinutes: 120));
        tracker.Evaluate();

        tracker.SetRemainingBudget(TimeSpan.FromHours(6));

        Assert.Equal(TimeSpan.FromHours(6), tracker.State.RemainingBudget);
    }

    [Fact]
    public void SetRemainingBudget_ClampsNegativeValuesToZero()
    {
        var (tracker, _) = Build(FullDaySchedule(dailyLimitMinutes: 120));
        tracker.Evaluate();

        tracker.SetRemainingBudget(TimeSpan.FromMinutes(-5));

        Assert.Equal(TimeSpan.Zero, tracker.State.RemainingBudget);
    }

    [Fact]
    public void SetRemainingBudget_ClearsTamperFlag_AndLiftsAManualLock()
    {
        var (tracker, clock) = Build(FullDaySchedule(dailyLimitMinutes: 10));
        tracker.Evaluate();
        clock.Advance(TimeSpan.FromSeconds(1));
        clock.JumpWallClockOnly(TimeSpan.FromDays(1));
        tracker.Evaluate();
        Assert.True(tracker.State.ClockTamperSuspected);
        tracker.SetManualLock();

        tracker.SetRemainingBudget(TimeSpan.FromHours(1));

        Assert.False(tracker.State.ClockTamperSuspected);
        Assert.False(tracker.State.ManuallyLocked);
    }

    [Fact]
    public void SetRemainingBudget_ActuallyUnlocksAMachineThatWasLockedForBeingOutsideTheWindow()
    {
        var schedule = new ScheduleConfig
        {
            IsConfigured = true,
            DailyLimitMinutes = 60,
            AllowedWindows = new Dictionary<DayOfWeek, List<TimeWindow>>
            {
                // Never allowed today — forces reliance on the emergency override below.
                [StartUtc.DayOfWeek] = new() { new TimeWindow(new TimeOnly(2, 0), new TimeOnly(3, 0)) },
            },
        };
        var (tracker, _) = Build(schedule);
        Assert.True(tracker.Evaluate().ShouldBeLocked);

        tracker.SetRemainingBudget(TimeSpan.FromHours(2));

        Assert.False(tracker.Evaluate().ShouldBeLocked);
    }

    [Fact]
    public void Evaluate_ResetsBudgetToFullDailyLimit_OnNewCalendarDay()
    {
        var (tracker, clock) = Build(FullDaySchedule(dailyLimitMinutes: 60));
        tracker.Evaluate();
        TickThroughActiveUse(tracker, clock, TimeSpan.FromMinutes(50));
        Assert.Equal(TimeSpan.FromMinutes(10), tracker.State.RemainingBudget);

        clock.Advance(TimeSpan.FromHours(20)); // crosses into the next day

        var decision = tracker.Evaluate();

        Assert.Equal(TimeSpan.FromMinutes(60), decision.RemainingBudget);
    }

    [Fact]
    public void UpdateSchedule_FullyResetsBudget_DiscardingUsageAndBonusMinutes()
    {
        var (tracker, clock) = Build(FullDaySchedule(dailyLimitMinutes: 120));
        tracker.Evaluate();
        TickThroughActiveUse(tracker, clock, TimeSpan.FromMinutes(60)); // half the day's budget used
        tracker.ExtendTime(TimeSpan.FromMinutes(30)); // plus a bonus grant
        Assert.Equal(TimeSpan.FromMinutes(90), tracker.State.RemainingBudget);

        var decision = tracker.Evaluate();
        Assert.Equal(TimeSpan.FromMinutes(90), decision.RemainingBudget);

        // Parent saves a brand new schedule with a different daily limit.
        var newSchedule = FullDaySchedule(dailyLimitMinutes: 45);
        tracker.UpdateSchedule(newSchedule);

        // Not 90, not 45+90, not 45-60 clamped to 0 — exactly the new schedule's fresh
        // daily limit, as if the day just started.
        Assert.Equal(TimeSpan.FromMinutes(45), tracker.State.RemainingBudget);
    }

    [Fact]
    public void UpdateSchedule_ClearsAnyOutstandingEmergencyWindowOverride()
    {
        var schedule = new ScheduleConfig
        {
            IsConfigured = true,
            DailyLimitMinutes = 60,
            AllowedWindows = new Dictionary<DayOfWeek, List<TimeWindow>>
            {
                // Never allowed today — forces reliance on the emergency override below.
                [StartUtc.DayOfWeek] = new() { new TimeWindow(new TimeOnly(2, 0), new TimeOnly(3, 0)) },
            },
        };
        var (tracker, _) = Build(schedule);
        tracker.Evaluate();
        tracker.ExtendTime(TimeSpan.FromMinutes(30)); // grants a window-check bypass
        Assert.False(tracker.Evaluate().ShouldBeLocked);

        tracker.UpdateSchedule(schedule); // parent re-saves (even the same) schedule

        Assert.True(tracker.Evaluate().ShouldBeLocked);
    }

    [Fact]
    public void SetManualLock_LocksImmediately_EvenWithBudgetRemainingAndInsideTheWindow()
    {
        var (tracker, _) = Build(FullDaySchedule(dailyLimitMinutes: 60));
        tracker.Evaluate(); // confirms it would otherwise be unlocked

        tracker.SetManualLock();
        var decision = tracker.Evaluate();

        Assert.True(decision.ShouldBeLocked);
        Assert.Equal(LockReason.ManuallyLocked, decision.Reason);
    }

    [Fact]
    public void SetManualLock_WorksEvenOnAnUnconfiguredDevice()
    {
        var (tracker, _) = Build(new ScheduleConfig());

        tracker.SetManualLock();
        var decision = tracker.Evaluate();

        Assert.True(decision.ShouldBeLocked);
        Assert.Equal(LockReason.ManuallyLocked, decision.Reason);
    }

    [Fact]
    public void Evaluate_PausesTheBudgetCountdown_WhileManuallyLocked()
    {
        var (tracker, clock) = Build(FullDaySchedule(dailyLimitMinutes: 60));
        tracker.Evaluate();
        TickThroughActiveUse(tracker, clock, TimeSpan.FromMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(50), tracker.State.RemainingBudget);

        tracker.SetManualLock();
        clock.Advance(TimeSpan.FromMinutes(15));
        tracker.Evaluate();

        // Budget didn't move at all while manually locked, despite 15 minutes passing.
        Assert.Equal(TimeSpan.FromMinutes(50), tracker.State.RemainingBudget);
    }

    [Fact]
    public void TryClearManualLock_Succeeds_AndMachineUnlocksAgain_WhenBudgetRemains()
    {
        var (tracker, _) = Build(FullDaySchedule(dailyLimitMinutes: 60));
        tracker.Evaluate();
        tracker.SetManualLock();
        tracker.Evaluate();

        var cleared = tracker.TryClearManualLock();
        var decision = tracker.Evaluate();

        Assert.True(cleared);
        Assert.False(decision.ShouldBeLocked);
    }

    [Fact]
    public void ExtendTime_AlsoLiftsAManualLock()
    {
        // Nobody grants more time on a device they still want held locked — an offline
        // unlock code or an online "+30 min" should un-stick a manual lock too, not just
        // silently top up a budget the manual lock would still block from being spent.
        var (tracker, _) = Build(FullDaySchedule(dailyLimitMinutes: 60));
        tracker.Evaluate();
        tracker.SetManualLock();
        Assert.True(tracker.Evaluate().ShouldBeLocked);

        tracker.ExtendTime(TimeSpan.FromMinutes(15));
        var decision = tracker.Evaluate();

        Assert.False(decision.ShouldBeLocked);
        Assert.False(tracker.State.ManuallyLocked);
    }

    [Fact]
    public void TryClearManualLock_Fails_WhenBudgetIsExhausted()
    {
        var (tracker, clock) = Build(FullDaySchedule(dailyLimitMinutes: 10));
        tracker.Evaluate();
        TickThroughActiveUse(tracker, clock, TimeSpan.FromMinutes(10)); // burns the whole daily budget
        tracker.SetManualLock();
        tracker.Evaluate();

        var cleared = tracker.TryClearManualLock();
        var decision = tracker.Evaluate();

        Assert.False(cleared);
        Assert.True(decision.ShouldBeLocked);
        // Still reported as the manual lock, since it was never actually lifted.
        Assert.Equal(LockReason.ManuallyLocked, decision.Reason);
    }

    [Fact]
    public void Evaluate_CapsChargeableTime_WhenGapIsMuchLargerThanNormalPolling()
    {
        // Real-world bug report this guards against: parent turns the PC on (fresh 2-hour
        // budget), uses it briefly, closes the lid (sleeps). Environment.TickCount64 — what
        // the monotonic clock is backed by — is NOT paused by sleep, only a real reboot
        // resets it, and the real EnforcementWorker only calls Evaluate() again once the
        // process actually resumes running. So from here, a real sleep looks exactly like
        // this: one large single jump between two consecutive Evaluate() calls (unlike
        // genuine continuous use, which arrives as many small ~5-second ticks — see
        // TickThroughActiveUse). Without a cap, the entire sleep duration gets charged as if
        // it were active use, and the child finds the machine already locked at 0 the moment
        // they turn it back on.
        var (tracker, clock) = Build(FullDaySchedule(dailyLimitMinutes: 120));
        tracker.Evaluate();

        clock.Advance(TimeSpan.FromHours(3)); // e.g. a laptop closed overnight
        var decision = tracker.Evaluate();

        Assert.False(decision.ShouldBeLocked);
        // Exactly the cap (30s) charged, not the full 3 hours asleep.
        Assert.Equal(TimeSpan.FromMinutes(120) - TimeSpan.FromSeconds(30), decision.RemainingBudget);
    }

    [Fact]
    public void Evaluate_StillChargesNormally_ForAGenuinelyShortGapAroundASuspend()
    {
        // A little real use, then asleep for hours, then a little more real use — only the
        // genuine active minutes should ever be charged.
        var (tracker, clock) = Build(FullDaySchedule(dailyLimitMinutes: 120));
        tracker.Evaluate();
        TickThroughActiveUse(tracker, clock, TimeSpan.FromMinutes(5)); // a bit of real use before sleeping

        clock.Advance(TimeSpan.FromHours(3)); // asleep the whole time
        tracker.Evaluate(); // the tick that "sees" the big gap and clips it

        var decision = TickThroughActiveUse(tracker, clock, TimeSpan.FromMinutes(5)); // a bit more real use after waking

        Assert.False(decision.ShouldBeLocked);
        // 120 - 5 (before) - 30s (clipped sleep charge) - 5 (after).
        Assert.Equal(TimeSpan.FromMinutes(110) - TimeSpan.FromSeconds(30), decision.RemainingBudget);
    }

    [Fact]
    public void Evaluate_DoesNotFalselyFlagClockTamper_ForALargeGapWhereBothClocksMoveTogether()
    {
        // A single big jump where both the monotonic and wall clocks move together (unlike
        // JumpWallClockOnly, used elsewhere to simulate an actual date/time change) is
        // exactly what sleep looks like — must not be mistaken for tampering.
        var (tracker, clock) = Build(FullDaySchedule(dailyLimitMinutes: 120));
        tracker.Evaluate();

        clock.Advance(TimeSpan.FromHours(6));
        var decision = tracker.Evaluate();

        Assert.False(decision.ShouldBeLocked);
        Assert.Equal(LockReason.None, decision.Reason);
    }

    [Fact]
    public void Evaluate_DoesNotFalselyFlagClockTamper_AfterAGenuineReboot()
    {
        // Real bug report: the PC is turned off overnight (not just asleep) and rebooted the
        // next morning. Environment.TickCount64 resets on a real reboot -- unlike sleep, which
        // leaves it running -- so elapsedMonotonic reads as ~0 while elapsedReal reflects the
        // whole night. That mismatch used to be indistinguishable from someone winding the
        // wall clock forward, so an entirely ordinary overnight shutdown falsely tripped the
        // tamper flag and left the machine stuck locked until a parent intervened.
        var (tracker, clock) = Build(FullDaySchedule(dailyLimitMinutes: 120));
        tracker.Evaluate();
        clock.Advance(TimeSpan.FromMinutes(1)); // some real uptime before it's turned off, so
        tracker.Evaluate();                     // the monotonic clock has somewhere to reset from

        clock.Reboot(TimeSpan.FromHours(3)); // off overnight
        var decision = tracker.Evaluate();

        Assert.False(decision.ShouldBeLocked);
        Assert.Equal(LockReason.None, decision.Reason);
        Assert.False(tracker.State.ClockTamperSuspected);
    }

    [Fact]
    public void Evaluate_DoesNotChargeBudget_ForTimeSpentPoweredOff()
    {
        var (tracker, clock) = Build(FullDaySchedule(dailyLimitMinutes: 120));
        tracker.Evaluate();
        TickThroughActiveUse(tracker, clock, TimeSpan.FromMinutes(10)); // a bit of real use first

        clock.Reboot(TimeSpan.FromHours(3)); // short enough not to cross into a new calendar day
        var decision = tracker.Evaluate();

        Assert.Equal(TimeSpan.FromMinutes(110), decision.RemainingBudget);
    }

    [Fact]
    public void Evaluate_StillDetectsClockTamper_WhenTheClockJumpsWithoutARealReboot()
    {
        // Regression guard for the reboot-detection fix above: it must only suppress the
        // tamper check for an actual reboot (monotonic clock reading lower than before), not
        // for a genuine clock-tamper attempt, where the monotonic clock keeps climbing normally.
        var (tracker, clock) = Build(FullDaySchedule(dailyLimitMinutes: 60));
        tracker.Evaluate();

        clock.Advance(TimeSpan.FromSeconds(1));
        clock.JumpWallClockOnly(TimeSpan.FromDays(1));
        var decision = tracker.Evaluate();

        Assert.True(decision.ShouldBeLocked);
        Assert.Equal(LockReason.ClockTamperSuspected, decision.Reason);
    }
}
