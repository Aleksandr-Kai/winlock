using Makaretu.Dns;

namespace WinLock.Service.Network;

/// <summary>
/// Advertises this PC on the local network via mDNS/DNS-SD (<c>_winlock._tcp.local</c>) so a
/// paired phone can find its current address automatically instead of the parent having to
/// re-scan a QR code or retype it after it changes (a new DHCP lease, a different Wi-Fi).
///
/// This is discovery only — it never grants trust. A phone that finds an address this way
/// still has to complete the real challenge-response auth and TLS certificate pin check
/// before this PC accepts it as a controller, exactly as if the address had been typed in by
/// hand. And because mDNS is link-local by construction, this can't be used to find (let
/// alone control) a PC from outside its own network — deliberately: control from outside the
/// LAN is out of scope for this product, on purpose, for safety.
/// </summary>
public sealed class DiscoveryBeacon : IDisposable
{
    public const string ServiceType = "_winlock._tcp";

    private readonly ServiceDiscovery _discovery;

    public DiscoveryBeacon(Guid deviceId, string deviceDisplayName, int port)
    {
        _discovery = new ServiceDiscovery();

        var profile = new ServiceProfile(deviceId.ToString("N"), ServiceType, (ushort)port);
        profile.AddProperty("deviceId", deviceId.ToString("N"));
        profile.AddProperty("displayName", deviceDisplayName);
        _discovery.Advertise(profile);
    }

    public void Dispose() => _discovery.Dispose();
}
