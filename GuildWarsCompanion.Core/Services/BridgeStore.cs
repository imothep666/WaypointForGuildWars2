namespace GuildWarsCompanion.Services;

/// <summary>
/// Where the paired PCs are kept, as the one thing the rest of the bridge code needs to know
/// about storage.
///
/// This interface is the seam the whole bridge refactor turned on. <see cref="KnownBridges"/>
/// used to reach straight for <c>Preferences.Default</c> and <c>SecureStorage.Default</c>, and
/// because those are MAUI statics with no substitute, every line that decided <em>which</em> PC
/// to talk to was unreachable from a test host — the pure ordering in
/// <see cref="BridgePlan"/> was tested and the deciding was not. That is precisely where two
/// paired PCs went wrong: the search picked one winner and gave up when it failed. With storage
/// behind an interface, <see cref="BridgeFinder"/> can be driven end to end on a laptop.
///
/// The split between list and secret is not this interface's idea — see
/// <see cref="KnownBridges"/> for why a pairing secret does not live in preferences — but it is
/// preserved here so that an implementation cannot accidentally collapse the two.
/// </summary>
public interface IBridgeStore
{
    /// <summary>The stored list, in whatever order it was written, or empty.</summary>
    IReadOnlyList<KnownBridge> Read();

    /// <summary>Replaces the stored list wholesale.</summary>
    void Write(IReadOnlyList<KnownBridge> all);

    Task<string?> SecretAsync(string id);

    Task SetSecretAsync(string id, string secret);

    void RemoveSecret(string id);

    /// <summary>
    /// The address and code a pairing from before bridges had identities was stored under.
    ///
    /// Behind this interface rather than read directly so that the migration — which has
    /// already deleted somebody's only pairing once — is something a test can watch happen.
    /// </summary>
    (string? Host, string? Code) LegacyPairing();

    void ClearLegacyPairing();
}
