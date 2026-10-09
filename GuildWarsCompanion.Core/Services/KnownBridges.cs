using GuildWarsCompanion.Bridge.Protocol;

namespace GuildWarsCompanion.Services;

/// <summary>
/// The PCs this phone is paired with.
///
/// Storage is <see cref="IBridgeStore"/>'s problem — the list in preferences, the secrets in
/// the keystore, and why they are kept apart. What is left here is the part with rules: what an
/// id means before a bridge has said one, which of two pairings for the same PC wins, and the
/// fact that every write is a read-modify-write over a list somebody else may be writing too.
///
/// <b>Every change is serialised.</b> Not for form's sake: a roster refresh and an emote tap
/// resolve at the same time as a matter of course, both of them call <see cref="Save"/> to
/// record where a PC was found, and both were reading the whole list, editing one entry and
/// writing it back. Two of those interleaved lose whichever PC the other one had just added.
/// </summary>
public sealed class KnownBridges(IBridgeStore store)
{
    private readonly object _gate = new();

    /// <summary>
    /// Every known PC, newest contact first.
    ///
    /// Synchronous and cheap: this is read on the way into every send, and a list of two or
    /// three entries out of preferences is not worth an await.
    /// </summary>
    public IReadOnlyList<KnownBridge> All()
    {
        lock (_gate) return store.Read().OrderByDescending(b => b.LastSeen).ToList();
    }

    public KnownBridge? Find(string id) =>
        All().FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.Ordinal));

    public bool Any => All().Count > 0;

    /// <summary>Adds or replaces one entry, matched on <see cref="KnownBridge.Id"/>.</summary>
    public void Save(KnownBridge bridge)
    {
        lock (_gate)
        {
            store.Write(store.Read().Where(b => b.Id != bridge.Id).Append(bridge).ToList());
        }
    }

    /// <summary>
    /// Pairs from a scanned card: the strong path, and the only one that can prove an address
    /// it was never told.
    /// </summary>
    public async Task<KnownBridge> PairAsync(BridgeCard card)
    {
        await store.SetSecretAsync(card.Id, card.Secret.Trim());

        var bridge = new KnownBridge(
            card.Id, card.Name, Address: null, Network: null, DateTimeOffset.UtcNow,
            Strong: true, Port: card.Port);

        Save(bridge);

        return bridge;
    }

    /// <summary>
    /// Pairs from a typed address and code.
    ///
    /// Kept because a camera is not always available and a QR code is not always readable, and
    /// because this is what every existing pairing is. The id is not known until the bridge
    /// answers once, so it waits in a slot named after the address — see
    /// <see cref="KnownBridge.PendingId"/>, and note that this used to be one shared
    /// <c>"?"</c> for every typed pairing, which meant pairing a second PC by hand deleted the
    /// first without saying so.
    /// </summary>
    public async Task<KnownBridge> PairAsync(string address, string code)
    {
        var host = address.Trim();
        var id = KnownBridge.PendingId(host);

        await store.SetSecretAsync(id, code.Trim());

        var bridge = new KnownBridge(
            id, "PC", host, BridgePlan.NetworkOf(host), DateTimeOffset.UtcNow,
            Strong: BridgeProtocol.IsStrong(code), Port: PortOf(host));

        Save(bridge);

        return bridge;
    }

    /// <summary>
    /// Gives an unidentified pairing the id and name the bridge reports.
    ///
    /// The moment a typed pairing stops being address-shaped and becomes identity-shaped: from
    /// here on it survives a lease change like any scanned one. The secret moves with it, since
    /// it is filed under the id.
    /// </summary>
    public async Task<KnownBridge> AdoptAsync(KnownBridge placeholder, BridgeIdentity identity)
    {
        if (identity.Id.Length == 0 ||
            identity.Id.StartsWith(KnownBridge.Pending, StringComparison.Ordinal))
            return placeholder;

        // This PC is already known, and known better — it was scanned, so it can prove itself.
        // Adopting over the top would replace a strong pairing with the typed one that turned
        // out to point at the same machine, silently losing the ability to follow it to a new
        // address. Drop the placeholder instead.
        if (Find(identity.Id) is { Strong: true } already)
        {
            Forget(placeholder.Id);

            return already;
        }

        var secret = await SecretAsync(placeholder.Id);

        if (secret is not null) await store.SetSecretAsync(identity.Id, secret);

        store.RemoveSecret(placeholder.Id);

        var adopted = placeholder with
        {
            Id = identity.Id,
            Name = identity.Name,
            Strong = BridgeProtocol.IsStrong(secret),
            Port = identity.Port > 0 ? identity.Port : placeholder.Port
        };

        lock (_gate)
        {
            store.Write(store.Read()
                .Where(b => b.Id != placeholder.Id && b.Id != adopted.Id)
                .Append(adopted)
                .ToList());
        }

        return adopted;
    }

    public void Forget(string id)
    {
        store.RemoveSecret(id);

        Drop(id);
    }

    public Task<string?> SecretAsync(string id) => store.SecretAsync(id);

    /// <summary>
    /// Moves a pre-identity pairing into the list, once.
    ///
    /// Called before the first read rather than on upgrade, because there is no upgrade hook to
    /// hang it on. The legacy keys are removed only after the entry is written, so an app killed
    /// in the middle of this still has exactly one copy of the pairing somewhere.
    /// </summary>
    public async Task MigrateAsync()
    {
        var (storedHost, storedCode) = store.LegacyPairing();

        var host = (storedHost ?? string.Empty).Trim();
        var code = (storedCode ?? string.Empty).Trim();

        if (host.Length == 0 || code.Length == 0) return;

        // Gated on this address not already being known, never on the list being empty. An
        // "if (!Any)" here quietly threw the old pairing away for anyone who scanned a second
        // PC before this first ran — the legacy keys were deleted either way, so the PC that
        // was paired first would simply have vanished.
        if (All().All(b => !string.Equals(b.Address, host, StringComparison.OrdinalIgnoreCase)))
            await PairAsync(host, code);

        store.ClearLegacyPairing();
    }

    private void Drop(string id)
    {
        lock (_gate) store.Write(store.Read().Where(b => b.Id != id).ToList());
    }

    /// <summary>
    /// The port out of a typed "host:port", or null. A typed address is the one place a port
    /// arrives without a card to carry it.
    /// </summary>
    private static int? PortOf(string address)
    {
        var parts = address.Split(':');

        return parts.Length == 2 && int.TryParse(parts[1], out var port) && port is > 0 and < 65536
            ? port
            : null;
    }
}
