using GuildWarsCompanion.Bridge.Protocol;

namespace GuildWarsCompanion.Services;

/// <summary>One bridge that answered a broadcast, and where it answered from.</summary>
/// <param name="Address">
/// Built from the sender's address and the port the bridge names in its own reply, never from
/// the port it happened to send from — the reply comes off an ephemeral UDP port and the thing
/// worth remembering is where its TCP listener is.
/// </param>
public sealed record DiscoveredBridge(string Address, BridgeIdentity Identity);

/// <summary>
/// Finding bridges without being told where they are.
///
/// An interface because Android is different: it will not route an unbound socket to Wi-Fi when
/// Wi-Fi is not the default network — the same trap <see cref="IBridgeTransport"/> exists for —
/// so there a broadcast has to be sent from a socket bound to the Wi-Fi network specifically.
/// Everywhere else <see cref="SocketBridgeDiscovery"/> does it with plain sockets.
/// </summary>
public interface IBridgeDiscovery
{
    /// <summary>
    /// The /24 the phone's Wi-Fi is on, or null when there is no Wi-Fi or it cannot be read.
    ///
    /// This is the key the address cache is filed under, so null has to mean "do not claim to
    /// know which network this is" rather than any particular network.
    /// </summary>
    string? Network { get; }

    /// <summary>
    /// Why the last sweep found nothing, when there is something to say.
    ///
    /// Added after a sweep failed silently on a phone that was demonstrably on the right
    /// network: a swallowed exception meant the one question worth answering — whether the
    /// broadcast left the handset at all — had no answer anywhere. A discovery that cannot
    /// explain itself is a discovery nobody can fix.
    /// </summary>
    string? Problem { get; }

    /// <summary>
    /// The Wi-Fi's default gateway, or null.
    ///
    /// Worth having on its own because of what a PC hotspot is: the machine sharing the
    /// connection <em>is</em> the gateway, so on the setup where addresses churn most the bridge
    /// sits at an address the phone can read off its own link properties. One TCP probe, and it
    /// uses the transport that is already proven to reach the PC.
    /// </summary>
    string? Gateway { get; }

    Task<IReadOnlyList<DiscoveredBridge>> FindAsync(CancellationToken ct = default);
}
