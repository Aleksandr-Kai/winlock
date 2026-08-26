using WinLock.Core.Warnings;

namespace WinLock.Core.Tests;

public class TimeWarningTrackerTests
{
    [Fact]
    public void Check_FiresOnceAtEachThreshold_AsBudgetCountsDown()
    {
        var tracker = new TimeWarningTracker();

        Assert.Null(tracker.Check(TimeSpan.FromMinutes(20), false, true));
        Assert.Null(tracker.Check(TimeSpan.FromMinutes(16), false, true));
        Assert.Equal(15, tracker.Check(TimeSpan.FromMinutes(15), false, true));
        Assert.Null(tracker.Check(TimeSpan.FromMinutes(15), false, true)); // same tick shouldn't refire
        Assert.Null(tracker.Check(TimeSpan.FromMinutes(11), false, true));
        Assert.Equal(10, tracker.Check(TimeSpan.FromMinutes(10), false, true));
        Assert.Null(tracker.Check(TimeSpan.FromMinutes(6), false, true));
        Assert.Equal(5, tracker.Check(TimeSpan.FromMinutes(5), false, true));
        Assert.Null(tracker.Check(TimeSpan.FromMinutes(1), false, true));
        Assert.Null(tracker.Check(TimeSpan.Zero, false, true));
    }

    [Fact]
    public void Check_NeverFires_WhileLocked()
    {
        var tracker = new TimeWarningTracker();
        Assert.Null(tracker.Check(TimeSpan.FromMinutes(5), shouldBeLocked: true, scheduleConfigured: true));
    }

    [Fact]
    public void Check_NeverFires_OnAnUnconfiguredDevice()
    {
        var tracker = new TimeWarningTracker();
        Assert.Null(tracker.Check(TimeSpan.FromMinutes(5), shouldBeLocked: false, scheduleConfigured: false));
    }

    [Fact]
    public void Check_RearmsAfterABudgetIncrease_SoTheSameThresholdCanFireAgain()
    {
        var tracker = new TimeWarningTracker();

        Assert.Equal(5, tracker.Check(TimeSpan.FromMinutes(5), false, true));

        // A parent extends time, or a new day rolls the budget over — remaining goes back up.
        Assert.Null(tracker.Check(TimeSpan.FromMinutes(30), false, true));

        Assert.Null(tracker.Check(TimeSpan.FromMinutes(16), false, true));
        Assert.Equal(15, tracker.Check(TimeSpan.FromMinutes(15), false, true));
    }

    [Fact]
    public void Check_RearmsAfterUnlocking_SoAFreshSessionThatStartsBelowAThreshold_StillWarnsOnce()
    {
        var tracker = new TimeWarningTracker();

        // Locked with very little budget left (e.g. a short daily limit); once it unlocks,
        // the very first observation should still produce one warning at the true remaining value.
        Assert.Null(tracker.Check(TimeSpan.FromMinutes(3), shouldBeLocked: true, scheduleConfigured: true));
        Assert.Equal(3, tracker.Check(TimeSpan.FromMinutes(3), shouldBeLocked: false, scheduleConfigured: true));
    }

    [Fact]
    public void Check_DoesNotFire_WhenRemainingIsAlreadyZero()
    {
        var tracker = new TimeWarningTracker();
        Assert.Null(tracker.Check(TimeSpan.Zero, false, true));
    }
}
