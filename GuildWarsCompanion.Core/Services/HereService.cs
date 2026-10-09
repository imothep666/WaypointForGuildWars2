using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Reads what <see cref="HereLine"/> needs for the map in a live game report.
///
/// Runs after Today's trackers on purpose: the map chest and world boss lists they have just
/// fetched are in the snapshot store, so they are read from there rather than asked for twice. A
/// snapshot from before the last reset is yesterday's answer and is not used.
/// </summary>
public sealed class HereService(
    Gw2Client client, SnapshotStore snapshots, CompassTargets targets, ApiKeySession session, MetaSource metas)
{
    /// <summary>How far ahead a boss is worth mentioning — the same window the compass lists them in.</summary>
    private static readonly TimeSpan BossWindow = TimeSpan.FromHours(2);

    public async Task<string?> DescribeAsync(GameReport report, CancellationToken ct = default)
    {
        if (report is not { IsLive: true, State: { CharacterName.Length: > 0 } state }) return null;

        var now = DateTimeOffset.UtcNow;

        var chest = report.MapName is { } name ? await ChestAsync(name, now, ct) : null;
        var (heroes, left) = await HeroPointsAsync(state, ct);
        var bosses = await BossesAsync(state.MapId, now, ct);
        var meta = report.MapName is { } map ? await MetaAsync(map, now, ct) : null;

        return HereLine.Describe(new HereFacts(chest, heroes, left, bosses, meta));
    }

    /// <summary>
    /// The map's meta from the wiki's timetable, matched by name — the wiki writes the map's name
    /// as the game does. On now, or the next one; the cycles run two hours, so the next is never far.
    /// </summary>
    private async Task<HereMeta?> MetaAsync(string mapName, DateTimeOffset now, CancellationToken ct)
    {
        var map = (await metas.GetAsync(ct))
            .FirstOrDefault(meta => string.Equals(meta.Name, mapName, StringComparison.OrdinalIgnoreCase));

        if (map is null) return null;

        var next = MetaSchedule.Next(map, now);

        if (MetaSchedule.Current(map, now) is { } on)
            return new HereMeta(on.Name, true, TimeSpan.Zero,
                next is { } after ? (after.Name, after.Start - now) : null);

        return next is { } coming ? new HereMeta(coming.Name, false, coming.Start - now) : null;
    }

    private async Task<bool?> ChestAsync(string mapName, DateTimeOffset now, CancellationToken ct)
    {
        // The list of chests changes with a game build, not a reset, so any stored copy will do.
        if (await StoredAsync("today/mapchests", DateTimeOffset.MinValue, ct) is not { } all) return null;
        if (HereLine.ChestFor(mapName, all) is not { } chest) return null;

        return await TodaysAsync("today/mapchests/done", now, ct) is { } done
            ? done.Contains(chest, StringComparer.OrdinalIgnoreCase)
            : null;
    }

    /// <summary>
    /// The map's hero points and how many this character has not taken. Asked of the API rather
    /// than a snapshot: Today does not read them, and the compass's cached copy can be a map old.
    /// In practice the count is never known today; see the note on the empty answer below.
    /// </summary>
    private async Task<(int Total, int? Left)> HeroPointsAsync(GameState state, CancellationToken ct)
    {
        if (!session.HasPermission(Gw2Permissions.Characters) ||
            !session.HasPermission(Gw2Permissions.Progression))
            return (0, null);

        try
        {
            var ids = (await targets.OnMapAsync(state.MapId, ct))
                .Where(target => target is { Kind: TargetKind.HeroPoint, Id: not null })
                .Select(target => target.Id!)
                .ToList();

            if (ids.Count == 0) return (0, null);

            var taken = await client.GetCharacterHeroPointsAsync(state.CharacterName, ct);

            // The endpoint has answered [] for every character since End of Dragons' "What Lies
            // Beneath" (the wiki's note, and measured: 0 for a 111-hour Deadeye). An empty answer is
            // therefore no answer, and "all 7 left" would be the API's bug presented as fact. A
            // character that really has taken none loses the part, which costs little; if ArenaNet
            // mends it, the part comes back by itself.
            if (taken.Count == 0) return (ids.Count, null);

            var done = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);

            return (ids.Count, ids.Count(id => !done.Contains(id)));
        }
        catch (Gw2ApiException)
        {
            return (0, null);
        }
    }

    private async Task<IReadOnlyList<(string Name, TimeSpan Wait)>> BossesAsync(
        int mapId, DateTimeOffset now, CancellationToken ct)
    {
        var due = EventSchedule.NextAppearances(now)
            .Where(boss => BossMaps.For(boss.Location) == mapId && boss.Next - now <= BossWindow)
            .ToList();

        if (due.Count == 0) return [];

        // Without today's kills a boss already done would be announced as due; say nothing instead.
        if (await TodaysAsync("today/worldbosses/done", now, ct) is not { } done) return [];

        return due
            .Where(boss => !done.Contains(boss.ApiId, StringComparer.OrdinalIgnoreCase))
            .Select(boss => (boss.Name, boss.Next - now))
            .ToList();
    }

    /// <summary>A list Today stored since the last daily reset, or null.</summary>
    private Task<List<string>?> TodaysAsync(string key, DateTimeOffset now, CancellationToken ct) =>
        StoredAsync(key, Resets.LastDaily(now), ct);

    private async Task<List<string>?> StoredAsync(string key, DateTimeOffset since, CancellationToken ct)
    {
        var stored = await snapshots.LoadAsync(key, Gw2JsonContext.Default.ListString, ct);

        return stored is { } kept && kept.FetchedAt >= since ? kept.Value : null;
    }
}
