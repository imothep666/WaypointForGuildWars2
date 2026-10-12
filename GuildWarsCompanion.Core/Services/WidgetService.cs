using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Keeps the home screen widgets fed, and answers what they should draw.
///
/// Split in two on purpose. <see cref="RefreshAsync"/> talks to the API and is called from the
/// periodic worker; <see cref="ReadAsync"/> never does and is called when the launcher wants a
/// redraw. A widget that fetched on draw would either block the launcher or show a spinner on
/// the home screen, and it gets redrawn on every rotation, unlock and theme change.
///
/// Its store is deliberately its own — <c>widget/…</c> keys rather than the ones the screens
/// use. The screens' snapshots exist to survive a lost connection and are only written when
/// somebody opens a page; these are written on a schedule whether the app is opened or not, and
/// one purpose must not quietly start depending on the other's timing.
/// </summary>
public sealed class WidgetService(
    Gw2Client client, ApiKeySession session, SnapshotStore snapshots, BridgeClient bridge, HereService here,
    LocalMapNames names, IWidgetRedraw? redraw = null)
{
    private const string BossesKey = "widget/worldbosses";
    private const string HereKey = "widget/here";
    private const string DailyKey = "widget/vault/daily";
    private const string WeeklyKey = "widget/vault/weekly";

    /// <summary>
    /// How stale the stored figures must be before the widget spends radio on them.
    ///
    /// Deliberately far slower than the redraw. The redraw has to keep the quarter-hour beat
    /// because that is when a boss changes, but working out which boss is next costs nothing —
    /// <see cref="EventSchedule"/> is a local table. The only things a fetch can change are the
    /// vault figures and which bosses have been killed, and both of those move only while the
    /// player is in the game, at a PC, not looking at their phone's home screen. Fetching on the
    /// redraw's beat was four radio wakeups an hour to learn nothing; measured on the device, the
    /// radio was already the app's single largest draw.
    /// </summary>
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromMinutes(55);

    /// <summary>
    /// Reads what the widgets show and stores it, if what is stored has aged out.
    ///
    /// Every part is optional and failures are swallowed per part: a key without "progression"
    /// still gets a boss timetable, because that half needs no account at all.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        // Every time, unlike the account below: where the character is changes by the minute,
        // and asking costs a request on the local network rather than the radio's wake-up.
        await RefreshHereAsync(ct);

        // Nothing stored yet reads as infinitely old, so a first run always fetches.
        var stored = await snapshots.LoadAsync(DailyKey, Gw2JsonContext.Default.VaultTrack, ct);

        if (stored is { } last && DateTimeOffset.UtcNow - last.FetchedAt < RefreshAfter) return;

        var signedIn = await session.EnsureRestoredAsync(ct) is not null;

        if (!signedIn || !session.HasPermission(Gw2Permissions.Progression)) return;

        await TryStoreAsync(BossesKey, Gw2JsonContext.Default.ListString,
            client.GetCompletedWorldBossesAsync, ct);

        await TryStoreAsync(DailyKey, Gw2JsonContext.Default.VaultTrack,
            token => client.GetVaultTrackAsync("daily", token), ct);

        await TryStoreAsync(WeeklyKey, Gw2JsonContext.Default.VaultTrack,
            token => client.GetVaultTrackAsync("weekly", token), ct);
    }

    /// <summary>
    /// Asks the PC who is in the game and where, and stores what is left there. No subnet
    /// sweep and five seconds' leash, as Today asks: a PC that is off must cost the poll nothing.
    /// A reading is stored only while someone is playing, so the widget keeps the last one.
    /// </summary>
    private async Task RefreshHereAsync(CancellationToken ct)
    {
        GameReport? report;

        using (var leash = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            leash.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                report = await bridge.GameAsync(leash.Token, sweep: false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return;
            }
        }

        if (report is not { IsLive: true }) return;

        report = await names.NameAsync(report, ct);

        string? line;

        try
        {
            line = await here.DescribeAsync(report, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            line = null;
        }

        await StoreHereAsync(report, line, ct);
    }

    /// <summary>
    /// Keeps a reading of where someone is playing and redraws the widgets with it. Today calls
    /// this too, having just worked the same line out, so opening the app brings the widget up
    /// to date at once rather than at the next quarter hour.
    /// </summary>
    public async Task StoreHereAsync(GameReport? report, string? line, CancellationToken ct = default)
    {
        if (report is not { IsLive: true, State: { CharacterName.Length: > 0 } state }) return;

        await snapshots.SaveAsync(
            HereKey, new HereReading(state.CharacterName, report.LocalMapName ?? report.MapName, line),
            Gw2JsonContext.Default.HereReading, ct);

        redraw?.Redraw();
    }

    /// <summary>
    /// What to draw, from what was last stored. Never touches the network.
    ///
    /// The boss half is whole regardless: <see cref="EventSchedule"/> is a local table, so an
    /// account that was never read, or a phone with no signal, still gets correct times — only
    /// the "already killed" marks are missing, and <see cref="WidgetContent.Bosses"/> says so by
    /// leaving them null rather than guessing.
    /// </summary>
    public async Task<(BossPanel Bosses, VaultPanel Vault, HerePanel Here)> ReadAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var done = await snapshots.LoadAsync(BossesKey, Gw2JsonContext.Default.ListString, ct);
        var daily = await snapshots.LoadAsync(DailyKey, Gw2JsonContext.Default.VaultTrack, ct);
        var weekly = await snapshots.LoadAsync(WeeklyKey, Gw2JsonContext.Default.VaultTrack, ct);

        var bosses = WidgetContent.Bosses(
            EventSchedule.NextAppearances(now),
            done is null ? null : new HashSet<string>(done.Value.Value, StringComparer.OrdinalIgnoreCase),
            done?.FetchedAt,
            now);

        // Both tracks are read together, so either timestamp dates the pair.
        var vault = WidgetContent.Vault(
            Counts(daily?.Value), Counts(weekly?.Value), daily?.FetchedAt ?? weekly?.FetchedAt, now);

        var reading = await snapshots.LoadAsync(HereKey, Gw2JsonContext.Default.HereReading, ct);

        return (bosses, vault, HereWidget.Panel(reading?.Value, reading?.FetchedAt, now));
    }

    private static TrackCounts? Counts(VaultTrack? track) =>
        track is null
            ? null
            : new TrackCounts(
                track.MetaProgressCurrent,
                track.MetaProgressComplete,
                track.Objectives.Count(objective => objective.IsUnclaimedCompletion),
                track.IsMetaUnclaimed);

    /// <summary>
    /// One part of the refresh. A part that fails leaves its last value in place: a rate limit
    /// or a dropped connection should cost the widget its freshness, not its contents.
    /// </summary>
    private async Task TryStoreAsync<T>(
        string key,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        Func<CancellationToken, Task<T>> fetch,
        CancellationToken ct)
    {
        try
        {
            await snapshots.SaveAsync(key, await fetch(ct), typeInfo, ct);
        }
        catch (Gw2ApiException)
        {
            // Offline, rate limited, missing scope or the API is down — try again next time.
        }
    }
}
