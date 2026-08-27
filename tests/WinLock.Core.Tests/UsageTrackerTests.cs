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

        clock.Advance(TimeSpan.FromMinutes(20));
        var decision = tracker.Evaluate();

        Assert.False(decision.ShouldBeLocked);
        Assert.Equal(TimeSpan.FromMinutes(40), decision.RemainingBudget);
    }

    [Fact]
    public void Evaluate_LocksMachine_WhenBudgetExhausted()
    {
        var (tracker, clock) = Build(FullDaySchedule(dailyLimitMinutes: 30));
        tracker.Evaluate();

        clock.Advance(TimeSpan.FromMinutes(45));
        var decision = tracker.Evaluate();

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
        clock.Advance(TimeSpan.FromMinutes(50));
        tracker.Evaluate();
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
        clock.Advance(TimeSpan.FromMinutes(60)); // half the day's budget used
        tracker.Evaluate();
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
        clock.Advance(TimeSpan.FromMinutes(10));
        tracker.Evaluate();
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
        clock.Advance(TimeSpan.FromMinutes(10)); // burns the whole daily budget
        tracker.Evaluate();
        tracker.SetManualLock();
        tracker.Evaluate();

        var cleared = tracker.TryClearManualLock();
        var decision = tracker.Evaluate();

        Assert.False(cleared);
        Assert.True(decision.ShouldBeLocked);
        // Still reported as the manual lock, since it was never actually lifted.
        Assert.Equal(LockReason.ManuallyLocked, decision.Reason);
    }
}
