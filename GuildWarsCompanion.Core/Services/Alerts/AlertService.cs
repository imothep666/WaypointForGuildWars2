using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services.Alerts;

/// <summary>
/// Runs the watchers and works out what is genuinely new.
///
/// Watchers report everything currently true; this class turns that into news. The
/// important part is the baseline: the first time a watcher runs, whatever it finds is
/// pre-existing state — 90 days of trading history, objectives finished last week — and is
/// recorded silently. Without that, enabling notifications would fire a hundred of them.
/// </summary>
public sealed class AlertService(
    IEnumerable<IAccountWatcher> watchers,
    AlertStore store,
    ApiKeySession session,
    INotifier notifier,
    NotifySchedule schedule,
    BridgeClient bridge,
    Bridge.Protocol.Journal journal,
    TestEvidence evidence)
{
    /// <summary>The test plan's checks that a notification on the shade settles, by watcher.</summary>
    private static readonly Dictionary<string, string> Checks = new()
    {
        ["goals"] = "goal-ready",
        ["metas"] = "meta-remind",
        ["reset"] = "reset-reminder"
    };

    public async Task<IReadOnlyList<AccountAlert>> CheckAsync(CancellationToken ct = default)
    {
        // A background run starts with no session in memory; restore the stored key first.
        // A failed restore is not the end of the poll: a watcher that needs no permissions —
        // price watches, which read public market data — still has work to do.
        var signedIn = await session.EnsureRestoredAsync(ct) is not null;

        var news = new List<AccountAlert>();

        foreach (var watcher in watchers)
        {
            if (watcher.RequiredPermissions.Count > 0 &&
                (!signedIn || !watcher.RequiredPermissions.All(session.HasPermission)))
                continue;

            IReadOnlyList<AccountAlert> found;
            try
            {
                found = await watcher.CheckAsync(ct);
            }
            catch (Gw2ApiException)
            {
                // Offline, rate limited or the API is down: try again on the next poll.
                continue;
            }

            var keys = found.Select(a => a.Key).ToList();
            var unseen = await store.UnseenAsync(keys, ct);
            await store.MarkSeenAsync(keys, ct);

            if (watcher.NeedsBaseline && !await store.IsBaselinedAsync(watcher.Name, ct))
            {
                await store.MarkBaselinedAsync(watcher.Name, ct);
                continue;
            }

            news.AddRange(found
                .Where(a => unseen.Contains(a.Key))
                .Select(a => a with { GameSaysItToo = watcher.GameSaysItToo, Watcher = watcher.Name }));
        }

        await store.PruneAsync(TimeSpan.FromDays(120), ct);
        return news;
    }

    /// <summary>
    /// Checks, then puts anything new on the notification shade — unless the hour is outside
    /// the player's window.
    ///
    /// The check still runs. Skipping it would leave the poll's bookkeeping behind, and every
    /// alert of the quiet hours would then arrive in one heap the moment the window opened.
    /// Nothing is deferred either: a world boss reminder that turns up after the boss has
    /// gone is worse than silence, and everything else is still on its own screen in the app.
    ///
    /// While a character is in the game, what the game already says is held back too; see
    /// <see cref="WhilePlaying"/>.
    /// </summary>
    public async Task<AlertRun> CheckAndNotifyAsync(CancellationToken ct = default)
    {
        var news = await CheckAsync(ct);

        // Said, not swallowed: "Check now" outside the hours used to answer "nothing waiting" while
        // something was due and withheld.
        if (!QuietHours.Allows(schedule.Current, DateTimeOffset.Now)) return new AlertRun(0, 0, null, news.Count);

        var live = WhilePlaying.NeedsAsking(news) ? await PlayingNowAsync(ct) : LiveReading.Unknown;
        var shown = WhilePlaying.Keep(news, live);
        var held = news.Count - shown.Count;

        if (held > 0)
        {
            journal.Write("alerts", $"held {held} back while {live.Name} is in the game");
        }

        foreach (var alert in shown)
        {
            await notifier.NotifyAsync(alert, ct);

            if (alert.Watcher is { } name && Checks.TryGetValue(name, out var check)) evidence.Saw(check);
        }

        return new AlertRun(shown.Count, held, live.Name);
    }

    /// <summary>
    /// Who is in the game, asked with a short leash. This runs in the background poll, which has
    /// two minutes for everything, and a PC that is off must not spend them: no subnet sweep, and
    /// ten seconds before the answer counts as unknown — which lets every alert through.
    /// </summary>
    private async Task<LiveReading> PlayingNowAsync(CancellationToken ct)
    {
        using var leash = CancellationTokenSource.CreateLinkedTokenSource(ct);
        leash.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            return await bridge.PlayingNowAsync(leash.Token, sweep: false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return LiveReading.Unknown;
        }
    }
}

/// <summary>What one poll put on the shade, and what it held back because the game said it.</summary>
/// <param name="Player">The character in the game when anything was held back; null otherwise.</param>
/// <param name="Quiet">What was due but withheld because the hour is outside the player's window.</param>
public sealed record AlertRun(int Sent, int Held, string? Player, int Quiet = 0);
