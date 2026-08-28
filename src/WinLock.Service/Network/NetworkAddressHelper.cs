using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WinLock.Service.Network;

public static class NetworkAddressHelper
{
    /// <summary>Best-effort local LAN IPv4 address to embed in the pairing QR. Re-resolved
    /// each time pairing starts rather than cached, since DHCP can hand out a new address
    /// between service starts (or even between two pairing attempts).</summary>
    public static string? GetPrimaryLocalIPv4() => GetLocalIPv4Addresses().FirstOrDefault()?.ToString();

    /// <summary>All candidate local LAN IPv4 addresses, e.g. to hand to the mDNS beacon so it
    /// doesn't fall back to its own (looser) address selection. Filtering by
    /// <see cref="NetworkInterfaceType.Loopback"/> alone isn't enough: tools like Npcap
    /// (installed by Wireshark and others) add a "Loopback Adapter" reported as an ordinary
    /// Ethernet-type interface that's "Up" and carries 127.0.0.1 — that address would
    /// otherwise sail straight through the type check and get embedded in the QR code or
    /// advertised over mDNS, making the PC completely unreachable from the phone. Checking the
    /// address itself with <see cref="IPAddress.IsLoopback"/> catches that regardless of which
    /// adapter or name it shows up under.</summary>
    public static IEnumerable<IPAddress> GetLocalIPv4Addresses()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
                    yield return address.Address;
            }
        }
    }
}
