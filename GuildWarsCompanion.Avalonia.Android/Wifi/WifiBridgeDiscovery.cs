using System.Text;
using System.Text.Json;

using Android.Content;
using Android.Net;

using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Services;

using Java.Net;

namespace GuildWarsCompanion.Platforms.Android;

/// <summary>
/// Finds bridges by shouting on the Wi-Fi and listening for whoever answers.
///
/// Bound to the Wi-Fi network rather than left to the routing table, for the same reason
/// <see cref="WifiBridgeTransport"/> is: with a PC hotspot up and no internet on it, Android
/// keeps mobile data as the default network and an unbound datagram to 255.255.255.255 leaves
/// by the cellular interface, where nothing is listening. Measured on this phone — the shell's
/// own route to the hotspot's gateway resolved through <c>rmnet_data2</c> while Wi-Fi was
/// associated and working.
/// </summary>
public sealed class WifiBridgeDiscovery : IBridgeDiscovery
{
    /// <summary>
    /// Long enough for every PC on a home network to answer, short enough to sit inside a tap.
    ///
    /// This only runs when the cached address has already missed, so it is the slow path by
    /// construction — but it is still a slow path a person is waiting through.
    /// </summary>
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(700);

    public string? Network => BridgePlan.NetworkOf(WifiAddress());

    public string? Problem { get; private set; }

    public string? Gateway => DefaultGateway();

    public async Task<IReadOnlyList<DiscoveredBridge>> FindAsync(CancellationToken ct = default)
    {
        var wifi = Wifi();

        if (wifi is null)
        {
            Problem = L.T("This phone is not on Wi-Fi, so there is no network to look on.");
            return [];
        }

        var found = await Task.Run(() => Sweep(wifi), ct);

        if (found.Count == 0 && Problem is null)
            Problem = L.F("Nothing answered on {0} (tried {1}). Is the bridge running?",
                          Network ?? L.T("this network"), string.Join(", ", Targets()));

        return found;
    }

    private IReadOnlyList<DiscoveredBridge> Sweep(global::Android.Net.Network wifi)
    {
        Problem = null;

        var found = new Dictionary<string, DiscoveredBridge>(StringComparer.Ordinal);

        DatagramSocket? socket = null;

        try
        {
            socket = new DatagramSocket();
            socket.Broadcast = true;

            // The whole reason this class exists rather than a UdpClient.
            wifi.BindSocket(socket);

            socket.SoTimeout = 150;

            var probe = Encoding.UTF8.GetBytes(BridgeProtocol.DiscoveryProbe);
            var sendFailures = new List<string>();

            // Both the global broadcast and this subnet's own. Some networks drop the first and
            // some drop neither; sending both costs one extra datagram and removes a whole
            // class of "works on my network".
            var targets = 0;

            foreach (var target in Targets())
            {
                targets++;

                try
                {
                    socket.Send(new DatagramPacket(
                        probe, probe.Length, InetAddress.GetByName(target), BridgeProtocol.DiscoveryPort));
                }
                catch (Exception ex)
                {
                    // A target this network will not route to is not a failure of the sweep —
                    // but if every target fails the reason is the only clue there is.
                    sendFailures.Add($"{target}: {ex.Message}");
                }
            }

            if (sendFailures.Count == targets)
                Problem = L.F("The probe could not be sent — {0}", string.Join("; ", sendFailures));

            var deadline = DateTime.UtcNow + Window;

            while (DateTime.UtcNow < deadline)
            {
                var buffer = new byte[BridgeProtocol.MaxDiscoveryBytes];
                var packet = new DatagramPacket(buffer, buffer.Length);

                try
                {
                    socket.Receive(packet);
                }
                catch (Exception)
                {
                    // SoTimeout firing, which is the normal way this loop idles between
                    // replies rather than an error worth reporting.
                    continue;
                }

                var host = packet.Address?.HostAddress;
                if (host is null) continue;

                if (Read(buffer, packet.Length) is not { Id.Length: > 0 } identity) continue;

                found[identity.Id] = new DiscoveredBridge($"{host}:{identity.Port}", identity);
            }
        }
        catch (Exception ex)
        {
            // No Wi-Fi socket, no discovery. The cached address is still there to try — but say
            // what went wrong, because "nothing found" and "could not look" are different
            // problems and only one of them is about the network.
            Problem = L.F("Could not open a Wi-Fi socket to look with — {0}", ex.Message);
        }
        finally
        {
            socket?.Close();
            socket?.Dispose();
        }

        return found.Values.ToList();
    }

    private static BridgeIdentity? Read(byte[] buffer, int length)
    {
        try
        {
            return JsonSerializer.Deserialize(
                Encoding.UTF8.GetString(buffer, 0, length), ProtocolJson.Default.BridgeIdentity);
        }
        catch (JsonException)
        {
            // Anything at all can send a datagram to that port. Something that is not a bridge
            // reply is expected traffic, not a fault.
            return null;
        }
    }

    /// <summary>
    /// Everywhere worth shouting, most likely to work first.
    ///
    /// The gateway is unicast and is here because of what a PC hotspot actually is: the machine
    /// sharing the connection <em>is</em> the gateway, so on the setup where the address churns
    /// most, the bridge is at a known address the phone can read off its own link properties.
    /// It also separates two failures that look identical from the phone — a firewall dropping
    /// inbound broadcast, and nothing listening at all — because unicast is allowed by the same
    /// rule that already lets the phone paste.
    /// </summary>
    private static IEnumerable<string> Targets()
    {
        if (DefaultGateway() is { } gateway) yield return gateway;

        yield return "255.255.255.255";

        if (BridgePlan.NetworkOf(WifiAddress()) is { } prefix) yield return $"{prefix}.255";
    }

    /// <summary>
    /// The Wi-Fi network's default gateway, or null.
    ///
    /// Read from the route table the platform hands over rather than assumed to be x.x.x.1 —
    /// that guess is right on most home networks and wrong often enough to be worth not making.
    /// </summary>
    private static string? DefaultGateway()
    {
        if (Manager() is not { } manager || Wifi() is not { } wifi) return null;

        var routes = manager.GetLinkProperties(wifi)?.Routes;

        if (routes is null) return null;

        foreach (var route in routes)
        {
            var hop = route.Gateway?.HostAddress;

            if (route.IsDefaultRoute && hop is not null && hop.Count(c => c == '.') == 3)
                return hop;
        }

        return null;
    }

    /// <summary>
    /// This phone's own Wi-Fi address, read from the network's link properties.
    ///
    /// Deliberately not from <c>WifiInfo</c>: reading the SSID there needs
    /// ACCESS_FINE_LOCATION on Android 10 and up, and the subnet is all that is wanted.
    /// </summary>
    private static string? WifiAddress()
    {
        if (Manager() is not { } manager || Wifi() is not { } wifi) return null;

        var links = manager.GetLinkProperties(wifi)?.LinkAddresses;

        if (links is null) return null;

        foreach (var link in links)
        {
            var address = link.Address?.HostAddress;

            // IPv4 only: the cache key is a /24, and a link-local IPv6 address says nothing
            // about which LAN this is.
            if (address is not null && address.Count(c => c == '.') == 3) return address;
        }

        return null;
    }

    private static global::Android.Net.Network? Wifi() => WifiNetwork.Current();

    private static ConnectivityManager? Manager() =>
        global::Android.App.Application.Context.GetSystemService(Context.ConnectivityService)
            as ConnectivityManager;
}
