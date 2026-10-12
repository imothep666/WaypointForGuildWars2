using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Where the compass gets things to point at.
///
/// Two sources with very different honesty about position, and the difference is carried through
/// rather than smoothed over. Map landmarks come from /v2/continents with real coordinates.
/// World bosses do not: the API publishes the boss list and nothing about where they spawn, so
/// the best truthful answer is the middle of the map they are on.
/// </summary>
public sealed class CompassTargets(Gw2Client api, MarkerStore markers, Gw2Cache cache)
{
    /// <summary>
    /// Maps do not change between game builds, so one fetch per map per run is plenty. Keyed by
    /// map id and never evicted: a play session visits a handful of maps, not a thousand.
    /// </summary>
    private readonly Dictionary<int, MapInfo> _maps = [];

    private readonly Dictionary<int, IReadOnlyList<CompassTarget>> _landmarks = [];

    /// <summary>
    /// Everything placed on one map, nearest-first ordering left to the caller who knows where
    /// the character is.
    ///
    /// Anything without a coordinate is dropped rather than placed at the origin. The API does
    /// occasionally publish a landmark with no <c>coord</c>, and a point at (0,0) is the far
    /// north-west corner of the continent — a needle confidently pointing at nothing.
    /// </summary>
    public async Task<IReadOnlyList<CompassTarget>> OnMapAsync(int mapId, CancellationToken ct)
    {
        if (_landmarks.TryGetValue(mapId, out var cached)) return cached;

        var map = await MapAsync(mapId, ct);

        var detail = await DetailAsync(map, ct);

        var found = new List<CompassTarget>();

        foreach (var task in detail.Tasks?.Values ?? Enumerable.Empty<MapTask>())
            if (Point(task.Coord) is { } at)
                found.Add(new CompassTarget(
                    task.Objective, TargetKind.Heart, map.Id, map.Name, at.X, at.Y,
                    Note: L.F("Level {0}", task.Level)));

        foreach (var poi in detail.PointsOfInterest?.Values ?? Enumerable.Empty<MapPoi>())
        {
            if (Point(poi.Coord) is not { } at) continue;

            // An unnamed point of interest is a vista or an unlock the API has not titled. It is
            // still somewhere, so it keeps its kind as its name rather than being dropped.
            var kind = Kind(poi.Type);

            found.Add(new CompassTarget(
                poi.Name is { Length: > 0 } named ? named : Describe(kind),
                kind, map.Id, map.Name, at.X, at.Y));
        }

        foreach (var hero in detail.SkillChallenges ?? Enumerable.Empty<MapSkillChallenge>())
            if (Point(hero.Coord) is { } at)
                found.Add(new CompassTarget(
                    L.T("Hero point"), TargetKind.HeroPoint, map.Id, map.Name, at.X, at.Y,
                    Id: hero.Id));

        // Whatever a downloaded pack has for this map, on the end. Deliberately not cached
        // into _landmarks with the rest: the pack can be replaced or removed while the app is
        // running, and a chest that survives being removed would have to be explained.
        var packed = markers.OnMap(map);

        _landmarks[mapId] = found;

        return packed.Count == 0 ? found : [.. found, .. packed];
    }

    /// <summary>
    /// The world bosses due next, pointed at to the nearest map.
    ///
    /// <see cref="CompassTarget.Precise"/> is false for every one of these, and that is the whole
    /// point of the flag. The Guild Wars 2 API publishes no spawn coordinates — /v2/worldbosses
    /// is a list of names — and the eleven arena positions are not something to hardcode from
    /// memory and present as fact. The middle of the map is a true statement; the exact spawn
    /// would be a guess wearing a decimal point.
    /// </summary>
    /// <param name="within">
    /// How far ahead to look. A boss six hours out is not a thing anyone is walking towards.
    /// </param>
    public async Task<IReadOnlyList<CompassTarget>> BossesAsync(
        DateTimeOffset utcNow, TimeSpan within, CancellationToken ct)
    {
        var found = new List<CompassTarget>();

        foreach (var boss in EventSchedule.NextAppearances(utcNow).OrderBy(b => b.Next))
        {
            if (boss.Next - utcNow > within) continue;

            if (BossMaps.For(boss.Location) is not { } mapId) continue;

            MapInfo map;

            try
            {
                map = await MapAsync(mapId, ct);
            }
            catch (Gw2ApiException)
            {
                // One map that will not load must not cost the whole list. The others are still
                // worth showing.
                continue;
            }

            var wait = boss.Next - utcNow;
            var note = wait < TimeSpan.Zero ? L.T("up now") : L.F("in {0}", Minutes(wait));

            // The arena itself, where ArenaNet's own v1 event data has one and the map publishes
            // both of its rectangles. This is the difference between a needle pointing at a map
            // and a needle pointing at a fight; see BossArenas.
            if (BossArenas.For(boss.ApiId, mapId) is { } arena &&
                map.MapArea is { } area && map.ContinentArea is { } continent)
            {
                var at = MapSpace.ToContinent(arena.MapX, arena.MapY, area, continent);

                found.Add(new CompassTarget(
                    boss.Name, TargetKind.WorldBoss, map.Id, map.Name, at.X, at.Y,
                    Note: note, Id: boss.ApiId));

                continue;
            }

            // No arena, or a map that will not say where it is. The middle of the map is a true
            // statement about where the map is, and the screen says that is what it is.
            if (map.Centre is not { } centre) continue;

            found.Add(new CompassTarget(
                boss.Name, TargetKind.WorldBoss, map.Id, map.Name, centre.X, centre.Y,
                Precise: false, Note: note, Id: boss.ApiId));
        }

        return found;
    }

    /// <summary>
    /// What this character and account have already finished, as ids.
    ///
    /// Re-read whenever the character changes, and otherwise cached: taking a hero point is not
    /// something the compass needs to notice within the second, and the alternative is two API
    /// calls every time somebody walks through a map border.
    ///
    /// Every failure returns <see cref="CompassFilter.Nothing"/>. A key without the characters
    /// or progression scope, or a phone with no internet, should see an unfiltered dial — which
    /// is what the screen did before this existed — rather than an error or an empty one.
    /// </summary>
    public async Task<CompassFilter> ProgressAsync(string character, CancellationToken ct)
    {
        if (_progressFor == character && _progress is { } cached) return cached;

        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (character.Length > 0)
            try
            {
                var hero = await api.GetCharacterHeroPointsAsync(character, ct);

                foreach (var id in hero) done.Add(id);

                Note = $"{hero.Count} hero points done";
            }
            catch (Exception ex)
            {
                // Never silent. A filter that quietly does nothing looks exactly like an account
                // with nothing finished, and the screen cannot tell the two apart on its own.
                Note = $"hero points unavailable — {ex.Message}";
            }

        try
        {
            var bosses = await api.GetAccountWorldBossesAsync(ct);

            foreach (var id in bosses) done.Add(id);

            Note += $"; {bosses.Count} bosses done today";
        }
        catch (Exception ex)
        {
            Note += $"; world bosses unavailable — {ex.Message}";
        }

        _progressFor = character;
        _progress = new CompassFilter(done);

        return _progress;
    }

    /// <summary>
    /// What the last progress lookup actually did, in words.
    ///
    /// Exists because the two failures this can have — no scope on the key, and an account with
    /// genuinely nothing finished — produce the identical empty set. Without this the screen
    /// would report "nothing hidden" for both and there would be no way to tell which.
    /// </summary>
    public string? Note { get; private set; }

    private string? _progressFor;
    private CompassFilter? _progress;

    /// <summary>Forgets what is done, so the next look asks again. For a screen that wants to
    /// notice a hero point taken a minute ago.</summary>
    public void ForgetProgress()
    {
        _progressFor = null;
        _progress = null;
    }

    /// <summary>
    /// One map's own description — continent, floor, region and footprint — cached for the run.
    ///
    /// Public because the map screen needs the same three numbers this class needs to build a
    /// floor URL, and a second cache of static data on the same phone would only mean a second
    /// request for it.
    /// </summary>
    public Task<MapInfo> InfoAsync(int mapId, CancellationToken ct) => MapAsync(mapId, ct);

    /// <summary>
    /// Every trail the installed packs have for one map, whether or not the map screen is drawing
    /// it. For <see cref="TrailGuide"/>: a trail the player has not switched on for display is still
    /// a way somebody walked, and choosing what the map shows must not change where the needle
    /// points.
    /// </summary>
    public async Task<IReadOnlyList<RouteLine>> RoutesAsync(int mapId, CancellationToken ct) =>
        markers.RoutesOnMap(await MapAsync(mapId, ct));

    /// <summary>
    /// One map's description: from memory, then from disk, then from the API.
    ///
    /// The disk step is what lets a map be opened with no connection at all. Maps are static
    /// data — <see cref="Gw2Cache"/> drops them when the game build moves — so a stored answer
    /// is the same answer, and asking the network for it on a train is asking for nothing.
    /// </summary>
    private async Task<MapInfo> MapAsync(int mapId, CancellationToken ct)
    {
        if (_maps.TryGetValue(mapId, out var cached)) return cached;

        if (await cache.GetMapAsync(mapId, ct) is { } stored)
        {
            _maps[mapId] = stored;

            return stored;
        }

        var map = await api.GetMapAsync(mapId, ct);

        _maps[mapId] = map;

        // One row rather than the whole list: this call says nothing about the maps nobody has
        // asked about, and a replace here would empty a picker that was working a moment ago.
        await cache.SaveMapsAsync([map], ct: ct);

        return map;
    }

    /// <summary>
    /// What is placed on a map, from disk when it is there.
    ///
    /// The tiles have been kept on disk since the map screen was written; without this the
    /// markers were the half that still needed a connection, which is a map that draws
    /// perfectly and has nothing on it.
    /// </summary>
    private async Task<MapDetail> DetailAsync(MapInfo map, CancellationToken ct)
    {
        if (await cache.GetMapDetailAsync(map.Id, ct) is { } stored) return stored;

        var detail = await api.GetMapDetailAsync(map, ct);

        await cache.SaveMapDetailAsync(map.Id, detail, ct);

        return detail;
    }

    private static string Minutes(TimeSpan wait) =>
        wait.TotalMinutes < 1
            ? L.T("under a minute")
            : wait.TotalMinutes < 60
                ? L.F("{0:0} min", wait.TotalMinutes)
                : L.F("{0}h {1:00}m", wait.Hours, wait.Minutes);

    private static (double X, double Y)? Point(List<double>? coord) =>
        coord is [var x, var y, ..] ? (x, y) : null;

    private static TargetKind Kind(string? type) => type switch
    {
        "waypoint" => TargetKind.Waypoint,
        "vista" => TargetKind.Vista,
        _ => TargetKind.Landmark
    };

    private static string Describe(TargetKind kind) => kind switch
    {
        TargetKind.Waypoint => L.T("Waypoint"),
        TargetKind.Vista => L.T("Vista"),
        _ => L.T("Point of interest")
    };
}
