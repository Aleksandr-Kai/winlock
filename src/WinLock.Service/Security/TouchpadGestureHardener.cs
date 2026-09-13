using System.Runtime.Versioning;
using Microsoft.Win32;
using WinLock.Service.Interop;

namespace WinLock.Service.Security;

public interface ITouchpadGestureHardener
{
    /// <summary>Call on every enforcement tick — cheap when already disabled, and self-heals
    /// within one poll interval if a child flips the setting back on.</summary>
    void Enforce();
}

/// <summary>
/// Switching virtual desktops isn't only a keyboard shortcut (Win+Tab/Win+Ctrl+D, which the
/// lock screen's own low-level hook can at least try to block) — a three- or four-finger
/// touchpad swipe does it too, and that's not a keyboard event at all, so no
/// WH_KEYBOARD_LL-based hook can ever see it. Unlike most Explorer restrictions, Windows has
/// no Group-Policy-backed switch for this: Settings just writes a couple of DWORD values into
/// the signed-in user's own HKEY_CURRENT_USER, which a standard, non-admin child account can
/// freely flip back in a few seconds. So rather than a one-time write at install time, this
/// re-asserts the disabled values on every enforcement tick from EnforcementWorker — flipping
/// it back on in Settings just gets it turned back off within one poll interval.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TouchpadGestureHardener(ILogger<TouchpadGestureHardener> logger) : ITouchpadGestureHardener
{
    public void Enforce()
    {
        if (!SessionLauncher.TryGetActiveConsoleUserSid(out var sid))
            return; // no one signed in on the console right now — nothing to enforce

        try
        {
            using var precisionTouchpad = Registry.Users.CreateSubKey(
                $@"{sid}\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad", writable: true);
            DisableIfEnabled(precisionTouchpad, "ThreeFingerSlideEnabled");
            DisableIfEnabled(precisionTouchpad, "FourFingerSlideEnabled");

            using var desktop = Registry.Users.CreateSubKey($@"{sid}\Control Panel\Desktop", writable: true);
            DisableIfEnabled(desktop, "TouchGestureSetting");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not enforce disabled touchpad desktop-switch gestures.");
        }
    }

    private void DisableIfEnabled(RegistryKey key, string valueName)
    {
        var current = key.GetValue(valueName);
        if (current is 0)
            return; // already disabled — nothing to do, and nothing worth logging every tick

        key.SetValue(valueName, 0, RegistryValueKind.DWord);
        logger.LogInformation(
            "Disabled touchpad gesture {ValueName} (was {Previous}) — closes the virtual-desktop-switch bypass that doesn't go through the keyboard.",
            valueName, current ?? "unset");
    }
}
