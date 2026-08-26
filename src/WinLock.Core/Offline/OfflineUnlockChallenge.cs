namespace WinLock.Core.Offline;

/// <summary>
/// A single-use challenge shown as a QR code on the lock screen. Deliberately not secret —
/// nothing about the offline flow's security depends on hiding this payload (see
/// <see cref="OfflineUnlockService"/>) — so it doesn't need encryption, only a short
/// integrity tag so the phone app can confirm it's reading a genuine WinLock challenge
/// before bothering the parent with it.
/// </summary>
public sealed record OfflineUnlockChallenge(long ChallengeId, Guid DeviceId, string IntegrityTag)
{
    /// <summary>Compact text encoded into the QR code.</summary>
    public string ToQrText() => $"winlock:v1:{DeviceId:N}:{ChallengeId}:{IntegrityTag}";

    /// <summary>Inverse of <see cref="ToQrText"/> — the parent app doesn't verify the
    /// integrity tag itself (it has no way to, without the device's integrity secret); it
    /// just needs <see cref="ChallengeId"/> to compute a response code, and
    /// <see cref="DeviceId"/> to pick which paired device's secret to use.</summary>
    public static bool TryParse(string qrText, out OfflineUnlockChallenge? challenge)
    {
        challenge = null;
        var parts = qrText.Split(':');
        if (parts.Length != 5 || parts[0] != "winlock" || parts[1] != "v1")
            return false;

        if (!Guid.TryParseExact(parts[2], "N", out var deviceId))
            return false;

        if (!long.TryParse(parts[3], out var challengeId))
            return false;

        challenge = new OfflineUnlockChallenge(challengeId, deviceId, IntegrityTag: parts[4]);
        return true;
    }
}
