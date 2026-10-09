using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using GuildWarsCompanion.Bridge.Protocol;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Finds bridges by shouting on the LAN with plain .NET sockets: Linux, so Ubuntu Touch, and the
/// Windows desktop. Android has <c>WifiBridgeDiscovery</c> instead.
///
/// Android needs its own because it sends an unbound socket over mobile data whenever Wi-Fi has no
/// internet, a LAN address included. Linux routes by the most specific route, so a datagram to the
/// gateway or to this subnet's broadcast leaves by the interface that subnet is on, mobile data or
/// not. What it cannot route on its own is 255.255.255.255, which follows the default route; that
/// is one target of three, and the other two are what does the work on a phone.
///
/// Which interface is "the LAN" is the one real decision here: see <see cref="Lan"/>.
/// </summary>
public sealed class SocketBridgeDiscovery : IBridgeDiscovery
{
    /// <summary>The same window as Android's: every PC on a home network answers well inside it,
    /// and it is short enough to sit inside a tap.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(700);

    public string? Network => BridgePlan.NetworkOf(Lan()?.Address.ToString());

    public string? Problem { get; private set; }

    public string? Gateway => Lan()?.Gateway?.ToString();

    public async Task<IReadOnlyList<DiscoveredBridge>> FindAsync(CancellationToken ct = default)
    {
        Problem = null;

        if (Lan() is not { } lan)
        {
            Problem = "This device is on no network with a gateway, so there is nowhere to look.";
            return [];
        }

        var targets = Targets(lan.Gateway, lan.Address, lan.Mask).ToList();
        var found = new Dictionary<string, DiscoveredBridge>(StringComparer.Ordinal);

        try
        {
            // Bound to the LAN's own address, so replies come back to the interface that asked.
            using var socket = new UdpClient(new IPEndPoint(lan.Address, 0)) { EnableBroadcast = true };

            var probe = Encoding.UTF8.GetBytes(BridgeProtocol.DiscoveryProbe);
            var failures = new List<string>();

            foreach (var target in targets)
            {
                try
                {
                    await socket.SendAsync(probe, new IPEndPoint(target, BridgeProtocol.DiscoveryPort), ct);
                }
                catch (SocketException ex)
                {
                    // One target this network will not route is not a failed sweep, but if every
                    // one fails the reasons are the only clue there is.
                    failures.Add($"{target}: {ex.Message}");
                }
            }

            if (failures.Count == targets.Count)
            {
                Problem = $"The probe could not be sent — {string.Join("; ", failures)}";
                return [];
            }

            using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
            window.CancelAfter(Window);

            while (true)
            {
                UdpReceiveResult reply;

                try
                {
                    reply = await socket.ReceiveAsync(window.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // The window closing, which is how this loop ends rather than an error.
                    break;
                }

                if (Read(reply.Buffer) is not { Id.Length: > 0 } identity) continue;

                found[identity.Id] = new DiscoveredBridge(
                    $"{reply.RemoteEndPoint.Address}:{identity.Port}", identity);
            }
        }
        catch (SocketException ex)
        {
            // "Could not look" and "nothing found" are different problems, and only one of them
            // is about the network.
            Problem = $"Could not open a socket to look with — {ex.Message}";
            return [];
        }

        if (found.Count == 0)
            Problem = $"Nothing answered on {Network ?? "this network"} " +
                      $"(tried {string.Join(", ", targets)}). Is the bridge running?";

        return found.Values.ToList();
    }

    /// <summary>
    /// Everywhere worth shouting, most likely to work first: the gateway, because a PC sharing its
    /// connection <em>is</em> the gateway and unicast passes the firewall rule a paste already
    /// uses; then the limited broadcast; then this subnet's own, worked out from its mask.
    /// </summary>
    public static IEnumerable<IPAddress> Targets(IPAddress? gateway, IPAddress address, IPAddress mask)
    {
        if (gateway is not null) yield return gateway;

        yield return IPAddress.Broadcast;

        var host = address.GetAddressBytes();
        var bits = mask.GetAddressBytes();
        var broadcast = new byte[4];

        for (var i = 0; i < 4; i++) broadcast[i] = (byte)(host[i] | ~bits[i]);

        var directed = new IPAddress(broadcast);

        if (!directed.Equals(IPAddress.Broadcast)) yield return directed;
    }

    /// <summary>A bridge's reply, or null for any other datagram that found the port.</summary>
    public static BridgeIdentity? Read(byte[] buffer)
    {
        try
        {
            return JsonSerializer.Deserialize(Encoding.UTF8.GetString(buffer), ProtocolJson.Default.BridgeIdentity);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record LanInterface(IPAddress Address, IPAddress Mask, IPAddress? Gateway);

    /// <summary>
    /// The interface the LAN is on, or null.
    ///
    /// Up, IPv4, with a gateway — an interface without one is a VPN, a bridge for containers or a
    /// link-local leftover, and none of those is where a PC on the same Wi-Fi lives. Among those,
    /// Wi-Fi first: a phone with mobile data up has two interfaces with gateways, and the bridge
    /// is never on the cellular one. Then wired, then whatever is left.
    /// </summary>
    private static LanInterface? Lan()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                              nic.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Select(nic => (nic, props: nic.GetIPProperties()))
                .Select(x => (x.nic,
                    unicast: x.props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork),
                    gateway: x.props.GatewayAddresses
                        .Select(g => g.Address)
                        .FirstOrDefault(g => g.AddressFamily == AddressFamily.InterNetwork && !g.Equals(IPAddress.Any))))
                .Where(x => x.unicast is not null && x.gateway is not null)
                .OrderBy(x => x.nic.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 => 0,
                    NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => 1,
                    _ => 2
                })
                .Select(x => new LanInterface(x.unicast!.Address, x.unicast.IPv4Mask, x.gateway))
                .FirstOrDefault();
        }
        catch (NetworkInformationException)
        {
            // A platform that will not list its interfaces: say nothing rather than guess.
            return null;
        }
    }
}
