namespace WinLock.Core.Models;

/// <summary>Allowed usage window within a single day, e.g. 08:00-12:00.</summary>
public sealed record TimeWindow(TimeOnly Start, TimeOnly End)
{
    public bool Contains(TimeOnly time) => Start <= End
        ? time >= Start && time < End
        : time >= Start || time < End; // handles windows crossing midnight
}
