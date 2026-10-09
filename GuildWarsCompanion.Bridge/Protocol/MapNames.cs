using System.Collections.Concurrent;

namespace GuildWarsCompanion.Bridge.Protocol;

/// <summary>
/// Map ids turned into map names, and remembered.
///
/// MumbleLink gives a number — 28 — and the Guild Wars 2 API is the only thing that knows it
/// means Wayfarer Foothills. That makes this the one place in the bridge that talks to the
/// internet, so the two halves are kept apart: the request itself is <c>Gw2Maps</c> — which is
/// not compiled into the phone app or the tests, and cannot be — and what is here is only the
/// deciding: what to ask for, what to remember, and what not to ask again. A name is asked for
/// once per id and kept for the life of the process; map names are static data, so a second
/// request could only ever return the same answer.
///
/// Nothing here blocks. <see cref="Lookup"/> is called from the poll that draws the window, so
/// it answers with what is already known and starts the request for the <em>next</em> poll two
/// seconds later — a map change shows its id for one tick and its name after that, which is the
/// price of never making the window wait on a socket.
/// </summary>
/// <param name="fetch">
/// Asks the API. Returning null means "there is no such map", which is an answer and is final;
/// throwing means the request failed, which is not an answer and is retried.
/// </param>
public sealed class MapNames(Func<int, CancellationToken, Task<string?>> fetch)
{
    /// <summary>
    /// Long enough that a PC waking up with no network yet does not spend the poll loop on
    /// failing requests, short enough that the name appears without anyone restarting anything.
    /// </summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    /// <summary>Concurrent because the answers arrive on the thread pool while the UI thread is
    /// reading them; every poll calls <see cref="Lookup"/> from a timer.</summary>
    private readonly ConcurrentDictionary<int, string> _names = new();

    /// <summary>Ids the API has no map for. Kept so a bad id costs one request, not one per
    /// poll for as long as the game sits there.</summary>
    private readonly ConcurrentDictionary<int, byte> _unknown = new();

    /// <summary>When a failed id may be asked about again.</summary>
    private readonly ConcurrentDictionary<int, DateTimeOffset> _retryAt = new();

    /// <summary>The request already out for an id, so a second caller can wait on it instead of
    /// making one of its own.</summary>
    private readonly ConcurrentDictionary<int, Task<string?>> _inFlight = new();

    /// <summary>
    /// The name if it is already known, null if it is not — and either way the lookup is under
    /// way by the time this returns. Null is not a failure; it is "not yet", and the caller is
    /// expected to fall back to the id.
    /// </summary>
    public string? Lookup(int mapId, DateTimeOffset now)
    {
        if (_names.TryGetValue(mapId, out var name)) return name;

        _ = ResolveAsync(mapId, now);

        return null;
    }

    /// <summary>
    /// One request, at most, for one id — and when this returns, the answer to it is in.
    ///
    /// Awaitable so a caller that can wait — a test, or anything that wants the name before it
    /// prints anything — does not have to poll for it. That is why a caller arriving while a
    /// request is already out waits on that one rather than returning: bailing out early would
    /// make an awaited lookup answer "not yet" for no reason the caller could see.
    ///
    /// Never throws. It is started and dropped by <see cref="Lookup"/>, and an unobserved
    /// exception out of a timer tick would take the bridge down over a map name.
    /// </summary>
    public async Task ResolveAsync(int mapId, DateTimeOffset now, CancellationToken ct = default)
    {
        // Zero is character select and a loading screen: there is no map to name yet. Asking
        // would be a request per poll for the whole of somebody's login.
        if (mapId <= 0) return;

        if (_names.ContainsKey(mapId) || _unknown.ContainsKey(mapId)) return;

        if (_retryAt.TryGetValue(mapId, out var at) && now < at) return;

        // Whoever gets their own task into the dictionary does the asking; everybody else waits
        // on it. A slow request must not become several — the poll comes round every two
        // seconds, and the API is not quick when it is unwell.
        var mine = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = _inFlight.GetOrAdd(mapId, mine.Task);

        if (ReferenceEquals(request, mine.Task))
        {
            try
            {
                mine.SetResult(await fetch(mapId, ct).ConfigureAwait(false));
            }
            catch (Exception problem)
            {
                mine.SetException(problem);
            }
            finally
            {
                _inFlight.TryRemove(mapId, out _);
            }
        }

        try
        {
            var name = await request.ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(name))
                _unknown[mapId] = 0;
            else
                _names[mapId] = name.Trim();

            _retryAt.TryRemove(mapId, out _);
        }
        catch (Exception)
        {
            // No network, a timeout, the API down during a game build — all the same here, and
            // all worth trying again. The id keeps showing until one of them works.
            _retryAt[mapId] = now + RetryAfter;
        }
    }
}
