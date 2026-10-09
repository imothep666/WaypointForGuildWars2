using System.Text.Json;

using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Services;

/// <summary>
/// The real store: the list in settings, the secrets in the platform keystore.
///
/// Two stores, split by what the thing is rather than by convenience. The list — ids, names,
/// last known addresses — is ordinary preference data and goes in <see cref="ISettings"/>.
/// The secrets go in <see cref="ISecrets"/>, which is the Android Keystore, because a
/// pairing secret grants write access to somebody's clipboard and one of these used to sit in
/// plain preferences next to the window it opened.
///
/// Kept in its own file, away from <see cref="KnownBridges"/>: this only stores, and everything
/// that decides anything lives on the other side of <see cref="IBridgeStore"/>.
/// </summary>
public sealed class SettingsBridgeStore(ISettings settings, ISecrets secrets) : IBridgeStore
{
    private const string ListKey = "bridges";
    private const string SecretPrefix = "bridge_secret_";

    /// <summary>The two keys a single-PC pairing used to live under, read once and retired.</summary>
    private const string LegacyHostKey = "bridge_host";
    private const string LegacyCodeKey = "bridge_code";

    public IReadOnlyList<KnownBridge> Read()
    {
        var stored = settings.Get(ListKey, string.Empty);

        if (stored.Length == 0) return [];

        try
        {
            return JsonSerializer.Deserialize(stored, KnownBridgeJson.Default.ListKnownBridge) ?? [];
        }
        catch (JsonException)
        {
            // Unreadable is treated as empty rather than fatal. The cost is re-pairing, which
            // is a QR scan; the alternative is an app that cannot open its own settings.
            return [];
        }
    }

    public void Write(IReadOnlyList<KnownBridge> all) =>
        settings.Set(
            ListKey, JsonSerializer.Serialize(all.ToList(), KnownBridgeJson.Default.ListKnownBridge));

    public Task<string?> SecretAsync(string id) => secrets.GetAsync(SecretPrefix + id);

    public Task SetSecretAsync(string id, string secret) =>
        secrets.SetAsync(SecretPrefix + id, secret);

    public void RemoveSecret(string id) => secrets.Remove(SecretPrefix + id);

    public (string? Host, string? Code) LegacyPairing() =>
        (settings.Get(LegacyHostKey, string.Empty),
            settings.Get(LegacyCodeKey, string.Empty));

    public void ClearLegacyPairing()
    {
        settings.Remove(LegacyHostKey);
        settings.Remove(LegacyCodeKey);
    }
}
