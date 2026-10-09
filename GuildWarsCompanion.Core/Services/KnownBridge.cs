using System.Text.Json.Serialization;

using GuildWarsCompanion.Bridge.Protocol;

namespace GuildWarsCompanion.Services;

/// <summary>
/// One PC the phone knows how to reach, and where it was last seen.
///
/// The point of this record is what it does <em>not</em> treat as settings. An address used to
/// be typed and stored as configuration, which made a DHCP renewal look like a broken pairing;
/// here <see cref="Address"/> is a cache line — a hint worth trying first and worth nothing if
/// it fails. <see cref="Id"/> is the identity, and it does not move.
/// </summary>
/// <param name="Network">
/// Which network the address was learnt on, as a /24 prefix.
///
/// Without this a laptop that lives on both a home LAN and a phone hotspot has one address slot
/// for two answers, and every switch overwrites the other — so the cache misses every single
/// time it is most needed. Deliberately the subnet and not the SSID: reading an SSID needs
/// ACCESS_FINE_LOCATION on Android 10 and up, and asking for location permission in order to
/// paste an emote is not a trade worth making.
/// </param>
/// <param name="Strong">
/// Whether the stored secret can prove this bridge's identity — see
/// <see cref="BridgeProtocol.IsStrong"/>. Kept here as well as implied by the secret so that
/// listing the known PCs does not have to open the keystore for each one.
/// </param>
/// <param name="Port">
/// The TCP port the pairing card said this bridge listens on, or null for "nobody has said".
///
/// Only matters where the phone is guessing at addresses rather than reading one back — the
/// gateway probe and the subnet sweep, which would otherwise knock on
/// <see cref="BridgeProtocol.DefaultPort"/> and never find a bridge that was moved off it.
/// Nullable rather than defaulted so that an entry written before cards carried a port reads
/// back as "unknown" instead of as port zero.
/// </param>
public sealed record KnownBridge(
    string Id,
    string Name,
    string? Address = null,
    string? Network = null,
    DateTimeOffset LastSeen = default,
    bool Strong = false,
    int? Port = null)
{
    /// <summary>
    /// What an id starts with while the bridge behind it has not said who it is yet.
    ///
    /// It stands in until the bridge answers <see cref="BridgeProtocol.HelloPath"/> once and
    /// says who it really is. A placeholder rather than a null id because the secret is filed
    /// under the id, and a null key is not a place to put anything.
    ///
    /// A <em>prefix</em> rather than one fixed placeholder, and that is the whole point: every
    /// typed pairing used to be filed under the same bare <c>"?"</c>, so pairing a second PC by
    /// hand replaced the first one — list entry, keystore secret and all — because
    /// <see cref="KnownBridges.Save"/> matches on the id. Each pending pairing now gets its own
    /// slot, named after the only thing that distinguishes it before the bridge answers: the
    /// address it was typed as. A bare <c>"?"</c> written by an older version still reads as
    /// pending, which is what lets it be adopted rather than stranded.
    /// </summary>
    public const string Pending = "?";

    /// <summary>The slot a pairing typed as this address waits in until the bridge names itself.</summary>
    public static string PendingId(string address) =>
        Pending + address.Trim().ToLowerInvariant();

    public bool IsIdentified =>
        Id.Length > 0 && !Id.StartsWith(Pending, StringComparison.Ordinal);

    /// <summary>
    /// Where to knock when the address is being guessed at rather than read back.
    ///
    /// A stored <see cref="Address"/> already carries its port; this is for the probes that
    /// build an address out of a subnet and have to supply one.
    /// </summary>
    public int Listens => Port is > 0 and < 65536 ? Port.Value : BridgeProtocol.DefaultPort;

    /// <summary>What to show when the phone says where a paste went.</summary>
    public string Label => string.IsNullOrWhiteSpace(Name) ? "PC" : Name.Trim();

    public KnownBridge Seen(string address, string? network, DateTimeOffset now) =>
        this with { Address = address, Network = network, LastSeen = now };

    /// <summary>
    /// Where this PC was last found, for a list that is history rather than configuration.
    ///
    /// A freshly scanned PC has no address at all — the card carries none, on purpose — and
    /// "last seen at" with nothing after it reads as a bug rather than as a PC not yet found.
    /// </summary>
    public string Where =>
        Address is null ? "not found yet — it will be, once it is running" : $"last seen at {Address}";
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<KnownBridge>))]
internal partial class KnownBridgeJson : JsonSerializerContext;

/// <summary>
/// One paired PC that answered, where it answered from, and what it said.
///
/// The unit the search works in, so that every step — cache, broadcast, gateway, sweep — hands
/// back the same shape and the deciding is done in one place instead of three.
/// </summary>
/// <param name="Identity">
/// What the bridge said about itself, when it said anything. Null for an answer that came from
/// a bridge too old to have an identity, which is still an answer worth using.
/// </param>
/// <param name="Playing">
/// Whether the game is up on that PC. Only ever asked where it can change the answer — with one
/// PC awake there is nothing to choose between, and the round trip would be spent for nothing.
/// </param>
public sealed record BridgeAnswer(
    KnownBridge Bridge,
    string Address,
    BridgeIdentity? Identity = null,
    bool Playing = false)
{
    /// <summary>
    /// Whether this PC turned up somewhere other than where it was last found.
    ///
    /// A PC that has never been found anywhere is not moved — it is simply found, and that
    /// distinction matters: a freshly scanned pairing carries no address on purpose, and
    /// counting that as a move made the PC the user had just paired the least preferred one in
    /// the list and asked permission to use it.
    /// </summary>
    public bool Moved =>
        Bridge.Address is not null &&
        !string.Equals(Bridge.Address, Address, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Which addresses to try, and which answer to believe.
///
/// Pure on purpose. It used to be described as the only part of finding a bridge worth testing,
/// which was true of the code and wrong about the risk: the deciding lived in
/// <see cref="BridgeFinder"/>, behind MAUI statics, untested, and that is where two paired PCs
/// went wrong. Storage is behind <see cref="IBridgeStore"/> now and the search is tested too.
/// What is left here is the arithmetic — the ordering, which is the whole performance story. A
/// cache hit costs one round trip against a <c>/hello</c> that answers in about 4ms, so getting
/// the order right means the common case never reaches the network scan at all.
/// </summary>
public static class BridgePlan
{
    /// <summary>
    /// Cached addresses worth trying, best first, without duplicates.
    ///
    /// Addresses learnt on the network the phone is on now come first, because they are the only
    /// ones with any reason to still be true. Addresses from other networks follow rather than
    /// being dropped: they cost one parallel probe each and they rescue the case where the
    /// subnet could not be read at all.
    /// </summary>
    public static IReadOnlyList<string> Addresses(
        IReadOnlyList<KnownBridge> known, string? network)
    {
        var here = known
            .Where(b => b.Address is not null && Same(b.Network, network))
            .OrderByDescending(b => b.LastSeen);

        var elsewhere = known
            .Where(b => b.Address is not null && !Same(b.Network, network))
            .OrderByDescending(b => b.LastSeen);

        return here.Concat(elsewhere)
            .Select(b => b.Address!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Everything that answered, best first — a queue to work down rather than one winner.
    ///
    /// A list and not a single best answer, because being chosen is not the same as being
    /// usable: a PC can answer and then fail its challenge, or turn out to need a confirmation
    /// nobody has given yet. Picking one winner and returning nothing when it failed meant the
    /// other PC sitting there answering perfectly well was never tried — with two PCs paired,
    /// one of them stale was enough to make every send report "no PC answered".
    ///
    /// <see cref="BridgeAnswer.Playing"/> decides the order when it can: with two PCs awake on
    /// one network, the one rendering a character is the one the person is sitting at, and that
    /// is a better guess than anything the phone could ask them. Recency breaks the remaining
    /// ties, so the order is at least stable rather than arbitrary. Where one PC answered at
    /// more than one address, the address it was already known at wins — that is the one that
    /// needs no confirming.
    /// </summary>
    public static IReadOnlyList<BridgeAnswer> Rank(IEnumerable<BridgeAnswer> answered) =>
        answered
            .GroupBy(answer => answer.Bridge.Id, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(answer => answer.Playing)
                .ThenBy(answer => answer.Moved)
                .First())
            .OrderByDescending(answer => answer.Playing)
            .ThenByDescending(answer => answer.Bridge.LastSeen)
            .ToList();

    /// <summary>
    /// The /24 an address belongs to, or null. "192.168.137.191" becomes "192.168.137".
    ///
    /// A prefix rather than the whole address because the phone's own address moves within the
    /// subnet on every lease while the subnet itself is what identifies the network.
    /// </summary>
    public static string? NetworkOf(string? address)
    {
        var host = (address ?? string.Empty).Split(':')[0].Trim();
        var parts = host.Split('.');

        return parts.Length == 4 && parts.All(p => byte.TryParse(p, out _))
            ? string.Join('.', parts.Take(3))
            : null;
    }

    private static bool Same(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
