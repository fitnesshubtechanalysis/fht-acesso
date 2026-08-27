using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace FHT.Access.Toletus;

/// <summary>
/// Resolves Windows NIC name ↔ IPv4 so LiteNet3 WebSocket binds to the Ethernet
/// attached to the turnstile (not Wi-Fi / localhost).
/// </summary>
internal static class LocalNetworkResolver
{
    public sealed record NicInfo(string Name, IPAddress Ipv4, string Description);

    public static NicInfo Resolve(string? networkInterfaceOrIp, string? boardIpHint = null)
    {
        var nics = ListUpIpv4Nics().ToList();
        if (nics.Count == 0)
        {
            throw new InvalidOperationException(
                "Nenhuma interface Ethernet/Wi-Fi com IPv4 ativa encontrada.");
        }

        var key = networkInterfaceOrIp?.Trim() ?? string.Empty;

        if (!string.IsNullOrEmpty(key))
        {
            if (IPAddress.TryParse(key, out var asIp))
            {
                var byIp = nics.FirstOrDefault(n => n.Ipv4.Equals(asIp));
                if (byIp is not null)
                    return byIp;

                throw new InvalidOperationException(
                    $"Nenhuma NIC com IPv4 {asIp}. Disponíveis: {Format(nics)}");
            }

            var exact = nics.FirstOrDefault(n =>
                n.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return exact;

            var contains = nics.FirstOrDefault(n =>
                n.Name.Contains(key, StringComparison.OrdinalIgnoreCase));
            if (contains is not null)
                return contains;

            throw new InvalidOperationException(
                $"Interface '{key}' não encontrada. Disponíveis: {Format(nics)}");
        }

        // Prefer NIC on the same /24 as the board.
        if (IPAddress.TryParse(boardIpHint, out var boardIp))
        {
            var sameSubnet = nics.FirstOrDefault(n => SameSlash24(n.Ipv4, boardIp));
            if (sameSubnet is not null)
                return sameSubnet;
        }

        // Prefer names like "Ethernet 2" over Wi-Fi.
        var ethernet = nics.FirstOrDefault(n =>
            n.Name.StartsWith("Ethernet", StringComparison.OrdinalIgnoreCase)
            && !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
            && !n.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase));
        if (ethernet is not null)
            return ethernet;

        return nics[0];
    }

    public static IEnumerable<NicInfo> ListUpIpv4Nics()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback)
                continue;
            if (ni.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211))
                continue;
            if (ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                || ni.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
                continue;

            var ipv4 = ni.GetIPProperties().UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                ?.Address;
            if (ipv4 is null)
                continue;

            yield return new NicInfo(ni.Name, ipv4, ni.Description);
        }
    }

    private static bool SameSlash24(IPAddress a, IPAddress b)
    {
        var ba = a.GetAddressBytes();
        var bb = b.GetAddressBytes();
        return ba.Length == 4 && bb.Length == 4
               && ba[0] == bb[0] && ba[1] == bb[1] && ba[2] == bb[2];
    }

    private static string Format(IEnumerable<NicInfo> nics)
        => string.Join(", ", nics.Select(n => $"{n.Name}={n.Ipv4}"));
}
