namespace WinLock.Core.Models;

/// <summary>Per-weekday allowed windows plus a daily usage-time budget, in minutes.</summary>
public sealed class ScheduleConfig
{
    /// <summary>False until a parent has actually set a schedule at least once. A brand
    /// new, unpaired device must not lock the machine before anyone has had a chance to
    /// configure it — that's a bootstrapping dead end, not a security feature — so
    /// enforcement is entirely bypassed while this is false. Set to true by the network
    /// handler the moment a real <c>UpdateScheduleCommand</c> arrives from a paired parent.</summary>
    public bool IsConfigured { get; set; }

    public Dictionary<DayOfWeek, List<TimeWindow>> AllowedWindows { get; init; } = new();

    public int DailyLimitMinutes { get; init; } = 120;

    public bool IsWithinAllowedWindow(DateTimeOffset localNow)
    {
        if (!AllowedWindows.TryGetValue(localNow.DayOfWeek, out var windows) || windows.Count == 0)
            return false;

        var time = TimeOnly.FromDateTime(localNow.DateTime);
        return windows.Any(w => w.Contains(time));
    }
}
