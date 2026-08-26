using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WinLock.Service.Network;

public static class NetworkAddressHelper
{
    /// <summary>Best-effort local LAN IPv4 address to embed in the pairing QR. Re-resolved
    /// each time pairing starts rather than cached, since DHCP can hand out a new address
    /// between service starts (or even between two pairing attempts).</summary>
    public static string? GetPrimaryLocalIPv4()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                    return address.Address.ToString();
            }
        }

        return null;
    }
}
