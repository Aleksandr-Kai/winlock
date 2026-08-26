using WinLock.Core.Models;

namespace WinLock.Core.Tests;

public class TimeWindowTests
{
    [Theory]
    [InlineData(9, 0, true)]
    [InlineData(17, 59, true)]
    [InlineData(18, 0, false)]
    [InlineData(8, 59, false)]
    public void Contains_OrdinaryWindow_WithinSameDay(int hour, int minute, bool expected)
    {
        var window = new TimeWindow(new TimeOnly(9, 0), new TimeOnly(18, 0));
        Assert.Equal(expected, window.Contains(new TimeOnly(hour, minute)));
    }

    [Theory]
    [InlineData(23, 0, true)]  // 23:00 is within a 22:00-06:00 window
    [InlineData(2, 0, true)]   // 02:00 also is (wraps past midnight)
    [InlineData(6, 0, false)]  // exclusive end
    [InlineData(21, 59, false)]
    public void Contains_WindowCrossingMidnight(int hour, int minute, bool expected)
    {
        var window = new TimeWindow(new TimeOnly(22, 0), new TimeOnly(6, 0));
        Assert.Equal(expected, window.Contains(new TimeOnly(hour, minute)));
    }
}
