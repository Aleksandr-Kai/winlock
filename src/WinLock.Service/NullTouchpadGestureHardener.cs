using WinLock.Service.Security;

namespace WinLock.Service;

/// <summary>Stands in for <see cref="TouchpadGestureHardener"/> on non-Windows dev/test hosts,
/// where there's no touchpad-gesture registry to enforce against.</summary>
public sealed class NullTouchpadGestureHardener : ITouchpadGestureHardener
{
    public void Enforce()
    {
    }
}
