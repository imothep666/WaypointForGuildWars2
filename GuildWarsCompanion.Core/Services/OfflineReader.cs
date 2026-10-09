using System.Text.Json.Serialization.Metadata;
using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// A value, and how old it is. <see cref="FetchedAt"/> is null when it came from the network,
/// which is the only case where nothing needs saying on screen.
/// </summary>
public sealed record Fetched<T>(T Value, DateTimeOffset? FetchedAt)
{
    public bool IsStale => FetchedAt is not null;
}

/// <summary>
/// Fetch, remember, and fall back.
///
/// Only a connection problem falls back to the stored copy. A rejected key or a missing scope
/// is a real answer from the API and must reach the user — quietly showing yesterday's wallet
/// because the key was revoked this morning would hide the one thing they need to fix.
/// </summary>
public sealed class OfflineReader(SnapshotStore store)
{
    public async Task<Fetched<T>> ReadAsync<T>(
        string key,
        JsonTypeInfo<T> typeInfo,
        Func<CancellationToken, Task<T>> fetch,
        CancellationToken ct = default)
    {
        try
        {
            var fresh = await fetch(ct);
            await store.SaveAsync(key, fresh, typeInfo, ct);
            return new Fetched<T>(fresh, null);
        }
        catch (Gw2ApiException ex)
            when (ex.Kind is Gw2ErrorKind.Network or Gw2ErrorKind.ServiceUnavailable)
        {
            var stored = await store.LoadAsync(key, typeInfo, ct);

            // Nothing kept yet: the caller should see the connection error, not an empty
            // screen that pretends everything is fine.
            if (stored is null) throw;

            return new Fetched<T>(stored.Value.Value, stored.Value.FetchedAt);
        }
    }

    /// <summary>
    /// The same, for data a screen can manage without.
    ///
    /// <see cref="ReadAsync{T}"/> rethrows when there is nothing kept, because a wallet with
    /// no figures in it is not a wallet. This degrades to null instead: mastery points are
    /// decoration on a roster, and losing them offline must not lose the roster with them.
    ///
    /// A fetch that legitimately answers "nothing" leaves the stored copy alone — an endpoint
    /// going quiet is not a reason to forget what it last said.
    /// </summary>
    public async Task<Fetched<T?>> ReadOptionalAsync<T>(
        string key,
        JsonTypeInfo<T> typeInfo,
        Func<CancellationToken, Task<T?>> fetch,
        CancellationToken ct = default)
        where T : class
    {
        try
        {
            var fresh = await fetch(ct);

            if (fresh is not null)
                await store.SaveAsync(key, fresh, typeInfo, ct);

            return new Fetched<T?>(fresh, null);
        }
        catch (Gw2ApiException ex)
            when (ex.Kind is Gw2ErrorKind.Network or Gw2ErrorKind.ServiceUnavailable)
        {
            var stored = await store.LoadAsync(key, typeInfo, ct);

            return stored is null
                ? new Fetched<T?>(null, null)
                : new Fetched<T?>(stored.Value.Value, stored.Value.FetchedAt);
        }
    }
}
