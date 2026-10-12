using System.Collections.Concurrent;

using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Puts the phone's own name for a map into what the bridge reports.
///
/// The bridge names maps in English. It asks the API without a key and without knowing what
/// language anybody's phone is in, and its name is the one the wiki matches on, so it stays as
/// it is. The phone's name is a lookup by id in its own map store, in its own language; see
/// <see cref="GameReport.LocalMapName"/>.
/// </summary>
public sealed class LocalMapNames(Gw2Client api, Gw2Cache cache)
{
    /// <summary>How long a map that could not be named waits before it is asked about again.
    /// The stream reports ten times a second, and an unreachable API should not hear each one.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<int, string> _names = new();
    private readonly ConcurrentDictionary<int, DateTimeOffset> _asked = new();

    /// <summary>
    /// The report with the local name in, waiting for it if need be. For a report read once,
    /// where a moment spent on the name is a moment on a screen being opened anyway.
    /// </summary>
    public async Task<GameReport> NameAsync(GameReport report, CancellationToken ct = default)
    {
        if (Unneeded(report, out var mapId)) return report;

        // Bounded: on a slow connection the report is worth more on time in English than late
        // in German.
        using var leash = CancellationTokenSource.CreateLinkedTokenSource(ct);
        leash.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            return await LearnAsync(mapId, leash.Token) is { } name ? report with { LocalMapName = name } : report;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return report;
        }
    }

    /// <summary>
    /// The report with the local name in if it is already known, and as it came otherwise, with
    /// the name sent for. For the stream: a frame cannot wait, and the next one has the name.
    /// </summary>
    public GameReport Name(GameReport report)
    {
        if (Unneeded(report, out var mapId)) return report;

        if (_names.TryGetValue(mapId, out var name)) return report with { LocalMapName = name };

        _ = LearnAsync(mapId, CancellationToken.None);

        return report;
    }

    /// <summary>In English the bridge's name is already the phone's, and with no game there is no map.</summary>
    private bool Unneeded(GameReport report, out int mapId)
    {
        mapId = report.State?.MapId ?? 0;

        return api.Language == Gw2Language.English || report.State is null;
    }

    private async Task<string?> LearnAsync(int mapId, CancellationToken ct)
    {
        if (_names.TryGetValue(mapId, out var known)) return known;

        var now = DateTimeOffset.UtcNow;
        if (_asked.TryGetValue(mapId, out var at) && now - at < RetryAfter) return null;
        _asked[mapId] = now;

        try
        {
            // From the store first: a phone that has opened this map, or the picker, has it.
            var map = await cache.GetMapAsync(mapId, ct) ?? await api.GetMapAsync(mapId, ct);

            return _names[mapId] = map.Name;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Offline, or no such map: the bridge's English name stands in, which it always could.
            return null;
        }
    }
}
