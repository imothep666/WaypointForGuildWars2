namespace GuildWarsCompanion.Host;

/// <summary>
/// Small, plain values kept between runs: switches, times, the list of paired PCs.
///
/// Nothing secret goes here — that is <see cref="ISecrets"/>. Shaped after MAUI's Preferences, so
/// the supported types are the ones it supports: string, bool, int, long, double, float and
/// DateTime.
/// </summary>
public interface ISettings
{
    T Get<T>(string key, T defaultValue);

    void Set<T>(string key, T value);

    void Remove(string key);
}

/// <summary>
/// Values that grant something: the API key, a bridge's pairing secret.
///
/// On Android this is the Keystore. An implementation that cannot keep a secret better than a
/// plain file has to say so rather than quietly fall back to one.
/// </summary>
public interface ISecrets
{
    /// <summary>The value, or null when nothing is stored under the key.</summary>
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);

    bool Remove(string key);
}
