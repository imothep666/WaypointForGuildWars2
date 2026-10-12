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
    Gw2Client client, SnapshotStore snapshots, OfflineReader offline, VaultSignal vaultSignal)
{
    /// <summary>Dailies roll over at 00:00 UTC; the weekly vault at Monday 07:30 UTC.</summary>
    public static DateTimeOffset NextDailyReset(DateTimeOffset utcNow) =>
        Resets.NextDaily(utcNow);

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
            "worldbosses"));

        daily.Add(await TrackAsync(L.T("Map bonus chests"),
            client.GetMapChestIdsAsync, client.GetCompletedMapChestsAsync, ct,
            key: "mapchests"));

        daily.Add(await TrackAsync(L.T("Time-gated crafting"),
            client.GetDailyCraftingIdsAsync, client.GetCompletedDailyCraftingAsync, ct,
            key: "dailycrafting"));

        daily.Add(await DungeonsAsync(ct));

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
        string? key = null)
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

            var count = ResetProgress.Of(everything, done);

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
                .Select(d => Ids.ToDisplayName(d.Id))
                .ToList();

            return new TodayRow(L.T("Dungeon paths"), count.Done, count.Total, Join(unfinished));
        }
        catch (Gw2ApiException)
        {
            return new TodayRow(L.T("Dungeon paths"), 0, 0, null, Unread: true);
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

            var count = ResetProgress.Of(bosses, done);

            return new TodayRow(L.T("Raid encounters"), count.Done, count.Total, Join(count.Remaining));
        }
        catch (Gw2ApiException)
        {
            return new TodayRow(L.T("Raid encounters"), 0, 0, null, Unread: true);
        }
    }

    /// <summary>A few names are useful; a wall of thirty is not.</summary>
    private static string? Join(IReadOnlyList<string> remaining) => remaining.Count switch
    {
        0 => null,
        <= 3 => string.Join(" · ", remaining),
        _ => L.F("{0} · and {1} more", string.Join(" · ", remaining.Take(3)), remaining.Count - 3)
    };


}
