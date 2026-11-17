using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Native.Protocol.Share;

/// <summary>Descobre o IP do PC na rede de casa (e ignora placas virtuais, VPN, WSL...).</summary>
public static class Network
{
    private static readonly string[] VirtualHints =
        { "virtual", "vethernet", "hyper-v", "vmware", "virtualbox", "wsl", "docker", "tailscale", "zerotier", "vpn", "loopback", "bluetooth" };

    public static IReadOnlyList<IPAddress> LanAddresses()
    {
        var list = new List<(IPAddress Ip, int Score)>();
        NetworkInterface[] nics;
        try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
        catch { return Array.Empty<IPAddress>(); }

        foreach (var nic in nics)
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var label = (nic.Name + " " + nic.Description).ToLowerInvariant();
            if (VirtualHints.Any(label.Contains)) continue;

            IPInterfaceProperties props;
            try { props = nic.GetIPProperties(); } catch { continue; }
            bool hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));

            foreach (var ua in props.UnicastAddresses)
            {
                var ip = ua.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork || !IsPrivate(ip) || IsLinkLocal(ip)) continue;
                int score = (hasGateway ? 10 : 0) + (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 3 : 0)
                          + (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 2 : 0)
                          + (ip.GetAddressBytes()[0] == 192 ? 1 : 0);
                list.Add((ip, score));
            }
        }
        return list.OrderByDescending(x => x.Score).Select(x => x.Ip).ToList();
    }

    public static IPAddress? PrimaryLanAddress() => LanAddresses().FirstOrDefault();

    /// <summary>Endereços de rede local (casa/escritório), loopback ou link-local.</summary>
    public static bool IsLocalNetwork(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b6 = ip.GetAddressBytes();
            return ip.IsIPv6LinkLocal || (b6[0] & 0xFE) == 0xFC;   // fe80::/10 ou fc00::/7
        }
        return IsPrivate(ip) || IsLinkLocal(ip);
    }

    public static bool IsPrivate(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        if (b.Length != 4) return false;
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
    }

    private static bool IsLinkLocal(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b.Length == 4 && b[0] == 169 && b[1] == 254;
    }
}
