using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace GuildWarsCompanion.Bridge.Protocol;

/// <summary>
/// Answers "is there a bridge on this network?" so that nobody has to type an address.
///
/// The problem this exists for is not typing an IP once — it is that the IP stops being true.
/// A DHCP lease renews, a laptop moves from the desk to the hotspot, and the address the phone
/// was told becomes a lie that reports itself as "no bridge answered". An id that never changes,
/// looked up fresh each time, is the fix; this is the lookup.
///
/// <b>Broadcast rather than mDNS.</b> Symmetric — a fixed probe string in, one JSON object out —
/// with no responder library on either side, and it works on a Windows mobile hotspot, which is
/// exactly where the address churns most. mDNS would also let other tools find the bridge, which
/// is a reason to add it later and not a reason to depend on it now.
///
/// <b>What it will not say.</b> The reply carries the id, the name and the TCP port, and never
/// the secret or a proof. This answers an unauthenticated question from anything on the network,
/// so it may only give away what is already public; proving identity is
/// <see cref="BridgeProtocol.HelloPath"/>'s job, over TCP, when a caller asks with a nonce.
/// </summary>
public sealed class BridgeBeacon(Func<BridgeConfig> config) : IDisposable
{
    private UdpClient? _socket;
    private CancellationTokenSource? _stopping;

    /// <summary>Why discovery is not running, when it is not. Null while all is well.</summary>
    public string? Problem { get; private set; }

    /// <summary>
    /// Starts listening, or explains why it could not.
    ///
    /// A failure here is not fatal and must not be: the phone's cached address still works, and
    /// a bridge that refused to start over this would be a bridge that stopped pasting because
    /// of a feature whose whole job is convenience.
    /// </summary>
    public void Start()
    {
        Stop();

        try
        {
            _stopping = new CancellationTokenSource();

            // Bound to every interface, because the network the phone is on is not knowable
            // here — a hotspot, a LAN and a VPN can all be up at once.
            _socket = new UdpClient(AddressFamily.InterNetwork);
            _socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.Client.Bind(new IPEndPoint(IPAddress.Any, BridgeProtocol.DiscoveryPort));
            _socket.EnableBroadcast = true;

            Problem = null;

            _ = ListenAsync(_socket, _stopping.Token);
        }
        catch (Exception ex)
        {
            Problem = $"Discovery is off — could not listen on UDP {BridgeProtocol.DiscoveryPort}: {ex.Message}";
            Console.Error.WriteLine($"[bridge] {Problem}");

            Stop();
        }
    }

    public void Stop()
    {
        _stopping?.Cancel();
        _stopping?.Dispose();
        _stopping = null;

        _socket?.Dispose();
        _socket = null;
    }

    private async Task ListenAsync(UdpClient socket, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult probe;

            try
            {
                probe = await socket.ReceiveAsync(ct);
            }
            catch (Exception)
            {
                // Cancelled, or the socket went away underneath us. Either way there is nothing
                // left to listen on.
                return;
            }

            try
            {
                if (!IsProbe(probe.Buffer)) continue;

                // Unicast back to whoever asked rather than broadcast: the answer is only of
                // interest to the phone that asked, and a broadcast reply would tell the whole
                // network which PCs are here every time any phone looked.
                var reply = Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(
                        config().Identity(), ProtocolJson.Default.BridgeIdentity));

                await socket.SendAsync(reply, probe.RemoteEndPoint, ct);
            }
            catch (Exception)
            {
                // One malformed probe, or one reply that could not be sent, must not take the
                // listener down with it — the next phone to ask deserves an answer.
            }
        }
    }

    /// <summary>
    /// Whether this is the one thing this socket understands.
    ///
    /// Length-capped before anything else touches it. A UDP port open to the network is the
    /// least authenticated surface this app has, so the only thing it does with a datagram is
    /// compare it to a constant — there is deliberately no parser here to get wrong.
    /// </summary>
    private static bool IsProbe(byte[] datagram) =>
        datagram.Length is > 0 and <= BridgeProtocol.MaxDiscoveryBytes &&
        Encoding.UTF8.GetString(datagram).Trim()
            .StartsWith(BridgeProtocol.DiscoveryProbe, StringComparison.OrdinalIgnoreCase);

    public void Dispose() => Stop();
}
