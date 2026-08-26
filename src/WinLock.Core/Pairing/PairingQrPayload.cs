namespace WinLock.Core.Pairing;

/// <summary>
/// What the PC encodes into the pairing QR code. The scanned QR is itself the secure
/// channel — nobody but someone looking at this screen can read it — so the shared secret
/// travels inside it directly and is never sent over the network, before or after pairing.
/// </summary>
public sealed record PairingQrPayload(
    Guid DeviceId,
    string DeviceDisplayName,
    string Token,
    byte[] Secret,
    string CertificateFingerprintHex,
    string HostAndPort)
{
    public string ToQrText() =>
        $"winlock-pair:v1:{DeviceId:N}:{Uri.EscapeDataString(DeviceDisplayName)}:{Token}:" +
        $"{Convert.ToBase64String(Secret)}:{CertificateFingerprintHex}:{HostAndPort}";

    /// <summary>Inverse of <see cref="ToQrText"/> — shared by every controller-side
    /// implementation (the test stub today, an Android client eventually) so there's exactly
    /// one place that understands the QR wire format.</summary>
    public static bool TryParse(string qrText, out PairingQrPayload? payload)
    {
        payload = null;

        // HostAndPort is last and may itself contain a colon, so cap the split at the
        // number of fields rather than splitting on every colon in the string.
        var parts = qrText.Split(':', 8);
        if (parts.Length != 8 || parts[0] != "winlock-pair" || parts[1] != "v1")
            return false;

        if (!Guid.TryParseExact(parts[2], "N", out var deviceId))
            return false;

        byte[] secret;
        try
        {
            secret = Convert.FromBase64String(parts[5]);
        }
        catch (FormatException)
        {
            return false;
        }

        payload = new PairingQrPayload(
            deviceId,
            Uri.UnescapeDataString(parts[3]),
            parts[4],
            secret,
            parts[6],
            parts[7]);
        return true;
    }
}
