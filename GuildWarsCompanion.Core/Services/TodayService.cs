using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// What is left before the next reset.
///
/// Every tracker works the same way: a static list of everything that counts, and an account
/// list of what is already done. Subtracting one from the other is the whole feature — no
/// opinions, and it answers the question people actually open a companion app to ask.
/// </summary>
public sealed class TodayService(
    Gw2Client client, SnapshotStore snapshots, OfflineReader offline, VaultSignal vaultSignal,
    FractalService fractals, TestEvidence evidence, Gw2Cache cache, ItemLookup items)
{
    /// <summary>Dailies roll over at 00:00 UTC; the weekly vault at Monday 07:30 UTC.</summary>
    public static DateTimeOffset NextDailyReset(DateTimeOffset utcNow) =>
        Resets.NextDaily(utcNow);

    /// <summary>
    /// The trackers from what was stored by the last read, without the network, for the page to
    /// show at once while <see cref="GetAsync"/> asks for today's. Null when too little is stored to
    /// be worth showing - a first run would otherwise flash a page of unread rows.
    /// </summary>
    public async Task<IReadOnlyList<TodaySection>?> GetStoredAsync(CancellationToken ct = default)
    {
        using var _ = OfflineReader.StoredOnly();

        var sections = await GetAsync(ct);

        return sections.Any(section => section.Any(row => !row.Unread)) ? sections : null;
    }

    public async Task<IReadOnlyList<TodaySection>> GetAsync(CancellationToken ct = default)
    {
        var daily = new List<TodayRow>();
        var weekly = new List<TodayRow>();

        // The vault first: its objectives are what most players are actually chasing.
        foreach (var (track, label, bucket) in new[]
                 {
                     ("daily", L.T("Wizard's Vault dailies"), daily),
                     ("weekly", L.T("Wizard's Vault weekly"), weekly)
                 })
        {
            try
            {
                var vault = (await offline.ReadAsync(
                    $"today/vault/{track}", Gw2JsonContext.Default.VaultTrack,
                    token => client.GetVaultTrackAsync(track, token), ct)).Value;
                var claimed = vault.Objectives.Count(o => o.Claimed);
                var unclaimed = vault.Objectives
                    .Where(o => !o.Claimed)
                    .Select(o => o.Title)
                    .ToList();

                // Unchanged since before the reset means the account has not rolled over —
                // the vault endpoints wait for the player to log in. Listing expired
                // objectives as though they were today's is worse than saying nothing.
                var changedAt = await snapshots.RecordAsync(
                    $"vault/{track}", vault, Gw2JsonContext.Default.VaultTrack, ct);

                var stale = Resets.LastFor(track, DateTimeOffset.UtcNow) is { } reset &&
                            changedAt < reset;

                vaultSignal.Saw(track, vault, stale);

                bucket.Add(new TodayRow(
                    label, claimed, vault.Objectives.Count, Join(unclaimed), stale, "//vault"));
            }
            catch (Gw2ApiException)
            {
                // Missing scope or a bad day at ArenaNet. The row stays so the screen does
                // not quietly shrink by one.
                bucket.Add(new TodayRow(label, 0, 0, null, false, "//vault", Unread: true));
            }
        }

        daily.Add(await TrackAsync(L.T("World bosses"),
            client.GetWorldBossIdsAsync, client.GetCompletedWorldBossesAsync, ct, "//events",
            "worldbosses", GameNames.BossById));

        daily.Add(await TrackAsync(L.T("Map bonus chests"),
            client.GetMapChestIdsAsync, client.GetCompletedMapChestsAsync, ct,
            key: "mapchests", label: await ChestNamesAsync(ct)));

        daily.Add(await TrackAsync(L.T("Time-gated crafting"),
            client.GetDailyCraftingIdsAsync, client.GetCompletedDailyCraftingAsync, ct,
            key: "dailycrafting", label: await CraftingNamesAsync(ct)));

        daily.Add(await DungeonsAsync(ct));

        daily.Add(await FractalsAsync(ct));

        // Raids are the one tracker on the weekly clock: encounters clear at Monday 07:30
        // UTC, not at the daily reset, so listing them under "Today" would be a lie.
        weekly.Add(await RaidsAsync(ct));

        var sections = new List<TodaySection>();

        var live = TodaySummary.Worth(daily);
        if (live.Count > 0)
            sections.Add(new TodaySection(
                L.T("Today"), TodaySummary.Of(live), live.OrderBy(r => r.IsComplete)));

        var week = TodaySummary.Worth(weekly);
        if (week.Count > 0)
            sections.Add(new TodaySection(
                L.T("This week"), TodaySummary.Of(week), week.OrderBy(r => r.IsComplete)));

        return sections;
    }

    /// <summary>
    /// One tracker: everything that counts, minus what is done. A failure yields an empty row
    /// that the caller drops, rather than taking the whole screen down with it.
    /// </summary>
    private async Task<TodayRow> TrackAsync(
        string name,
        Func<CancellationToken, Task<List<string>>> all,
        Func<CancellationToken, Task<List<string>>> completed,
        CancellationToken ct,
        string? route = null,
        string? key = null,
        Func<string, string>? label = null)
    {
        try
        {
            // Both halves are kept: the list of what counts changes only with a game build,
            // and what is done changes daily — but neither is reachable on the underground.
            var everything = key is null
                ? await all(ct)
                : (await offline.ReadAsync($"today/{key}", Gw2JsonContext.Default.ListString, all, ct)).Value;

            var done = key is null
                ? await completed(ct)
                : (await offline.ReadAsync($"today/{key}/done", Gw2JsonContext.Default.ListString,
                    completed, ct)).Value;

            var count = ResetProgress.Of(everything, done, label);

            return new TodayRow(
                name, count.Done, count.Total, Join(count.Remaining), false, route);
        }
        catch (Gw2ApiException)
        {
            // Rate limited, offline, or the API had a moment. Say so; do not disappear.
            return new TodayRow(name, 0, 0, null, false, route, Unread: true);
        }
    }

    /// <summary>
    /// Dungeon paths left today.
    ///
    /// Progress is counted in paths — thirty-three of them — because that is what the account
    /// endpoint reports and what actually pays. The names shown are the dungeons those paths
    /// belong to: the path ids are internal ("ac_story", "hodgins"), and "Ac Story" is not a
    /// thing any player would recognise.
    /// </summary>
    private async Task<TodayRow> DungeonsAsync(CancellationToken ct)
    {
        try
        {
            var dungeons = (await offline.ReadAsync(
                "dungeons", Gw2JsonContext.Default.ListDungeon, client.GetDungeonsAsync, ct)).Value;
            var done = (await offline.ReadAsync(
                "account/dungeons", Gw2JsonContext.Default.ListString,
                client.GetCompletedDungeonPathsAsync, ct)).Value.ToHashSet();

            var paths = dungeons.SelectMany(d => (d.Paths ?? []).Select(p => p.Id));
            var count = ResetProgress.Of(paths, done);

            var unfinished = dungeons
                .Where(d => (d.Paths ?? []).Any(p => !done.Contains(p.Id)))
                .Select(d => DungeonName(d.Id))
                .ToList();

            return new TodayRow(L.T("Dungeon paths"), count.Done, count.Total, Join(unfinished));
        }
        catch (Gw2ApiException)
        {
            return new TodayRow(L.T("Dungeon paths"), 0, 0, null, Unread: true);
        }
    }

    /// <summary>
    /// The daily fractals left: each of the day's three at the account's tier, and the recommended
    /// scales it can enter. An account that has never run a fractal has none, and no row.
    /// </summary>
    private async Task<TodayRow> FractalsAsync(CancellationToken ct)
    {
        try
        {
            var view = await fractals.GetAsync(withWallet: false, ct);

            // Kept from before the reset and read offline: yesterday's three fractals are not
            // today's, and saying so beats listing them.
            if (view.IsYesterday)
                return new TodayRow(L.T("Daily fractals"), 0, 0, null, Route: "fractals", Unread: true);

            var counted = view.Counted;

            if (counted.Count > 0) evidence.Saw("fractals-row");

            return new TodayRow(
                L.T("Daily fractals"), counted.Count(task => task.Done), counted.Count,
                Join([.. counted.Where(task => !task.Done).Select(task => task.Name)]), Route: "fractals");
        }
        catch (Gw2ApiException)
        {
            return new TodayRow(L.T("Daily fractals"), 0, 0, null, Route: "fractals", Unread: true);
        }
    }

    /// <summary>
    /// Raid encounters left this week.
    ///
    /// Only bosses count. The wings also contain four checkpoints, and the account endpoint
    /// reports those as cleared too — so they are excluded from both sides rather than
    /// inflating the total.
    /// </summary>
    private async Task<TodayRow> RaidsAsync(CancellationToken ct)
    {
        try
        {
            var raids = (await offline.ReadAsync(
                "raids", Gw2JsonContext.Default.ListRaid, client.GetRaidsAsync, ct)).Value;
            var done = (await offline.ReadAsync(
                "account/raids", Gw2JsonContext.Default.ListString,
                client.GetCompletedRaidEncountersAsync, ct)).Value;

            var bosses = raids
                .SelectMany(r => r.Wings ?? [])
                .SelectMany(w => w.Events ?? [])
                .Where(e => e.IsBoss)
                .Select(e => e.Id);

            var count = ResetProgress.Of(bosses, done, GameNames.Raid);

            return new TodayRow(L.T("Raid encounters"), count.Done, count.Total, Join(count.Remaining));
        }
        catch (Gw2ApiException)
        {
            return new TodayRow(L.T("Raid encounters"), 0, 0, null, Unread: true);
        }
    }

    /// <summary>The map whose chest it is, by name; see <see cref="ChestNamesAsync"/>.</summary>
    private IReadOnlyDictionary<string, string>? _chestMaps;

    /// <summary>
    /// A map chest by its map's name in the phone's language - "Goldene Senke", not "Auric Basin
    /// Heros Choice Chest". The API names chests by nothing but an id made from the map's English
    /// name, so the map is found by that name in the map store the picker and the compass fill.
    /// A chest whose map the store does not hold yet keeps the id's own words, less the "heros
    /// choice chest" every one of them ends in.
    /// </summary>
    private async Task<Func<string, string>> ChestNamesAsync(CancellationToken ct)
    {
        if (_chestMaps is null or { Count: 0 })
        {
            try
            {
                var english = await cache.GetEnglishMapNamesAsync(ct);
                var byName = new Dictionary<string, string>();

                foreach (var map in await cache.GetMapsAsync(ct))
                    byName.TryAdd(HereLine.Slug(english.GetValueOrDefault(map.Id, map.Name)), map.Name);

                _chestMaps = byName;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _chestMaps = new Dictionary<string, string>();
            }
        }

        return id =>
        {
            var map = id.EndsWith(ChestSuffix, StringComparison.Ordinal) ? id[..^ChestSuffix.Length] : id;

            return _chestMaps.GetValueOrDefault(map) ?? Ids.ToDisplayName(map);
        };
    }

    private const string ChestSuffix = "_heros_choice_chest";

    /// <summary>
    /// The time-gated materials as items, which the API names in the phone's language. The ids
    /// of /v2/dailycrafting are names, not items, so the five are listed here; one added by a later
    /// release keeps the id's own words until it is listed too.
    /// </summary>
    private static readonly Dictionary<string, int> CraftedItems = new()
    {
        ["charged_quartz_crystal"] = 43772,
        ["glob_of_elder_spirit_residue"] = 46744,
        ["lump_of_mithrilium"] = 46742,
        ["spool_of_silk_weaving_thread"] = 46740,
        ["spool_of_thick_elonian_cord"] = 46745
    };

    private async Task<Func<string, string>> CraftingNamesAsync(CancellationToken ct)
    {
        IReadOnlyDictionary<int, Item> known;

        try
        {
            known = await items.ResolveAsync(CraftedItems.Values.ToList(), ct);
        }
        catch (Gw2ApiException)
        {
            known = new Dictionary<int, Item>();
        }

        return id => CraftedItems.TryGetValue(id, out var item) && known.GetValueOrDefault(item) is { } named
            ? named.Name
            : Ids.ToDisplayName(id);
    }

    /// <summary>
    /// A dungeon as the game names it. /v2/dungeons has ids and paths but no names, and the id
    /// loses the apostrophe ("Caudecus Manor"), so the eight are written out and translated.
    /// </summary>
    private static string DungeonName(string id) => id switch
    {
        "ascalonian_catacombs" => L.T("Ascalonian Catacombs"),
        "caudecus_manor" => L.T("Caudecus's Manor"),
        "twilight_arbor" => L.T("Twilight Arbor"),
        "sorrows_embrace" => L.T("Sorrow's Embrace"),
        "citadel_of_flame" => L.T("Citadel of Flame"),
        "honor_of_the_waves" => L.T("Honor of the Waves"),
        "crucible_of_eternity" => L.T("Crucible of Eternity"),
        "ruined_city_of_arah" => L.T("The Ruined City of Arah"),
        _ => Ids.ToDisplayName(id)
    };

    /// <summary>A few names are useful; a wall of thirty is not.</summary>
    private static string? Join(IReadOnlyList<string> remaining) => remaining.Count switch
    {
        0 => null,
        <= 3 => string.Join(" · ", remaining),
        _ => L.F("{0} · and {1} more", string.Join(" · ", remaining.Take(3)), remaining.Count - 3)
    };


}
