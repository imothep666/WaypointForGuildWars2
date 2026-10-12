namespace GuildWarsCompanion.Services;

/// <summary>One boss appearance, as a widget line.</summary>
/// <param name="Needed">
/// Null where nothing is known — no key, no "progression", or completions too old to trust.
/// Distinct from false, which is the account positively saying this one is done.
/// </param>
public sealed record BossLine(string Name, string Location, DateTimeOffset At, bool? Needed)
{
    /// <summary>The wall clock the player reads, in their own timezone.</summary>
    public string TimeText => At.ToLocalTime().ToString("HH:mm");
}

/// <summary>What the world boss widget draws.</summary>
public sealed record BossPanel(BossLine? Next, IReadOnlyList<BossLine> Later, string Footnote);

/// <summary>One Wizard's Vault track, cut down to what a glance can hold.</summary>
public sealed record TrackCounts(int MetaCurrent, int MetaComplete, int Unclaimed, bool MetaUnclaimed)
{
    /// <summary>Rewards sitting in the vault: finished objectives plus the track's own meta.</summary>
    public int Claimable => Unclaimed + (MetaUnclaimed ? 1 : 0);

    public bool IsComplete => MetaComplete > 0 && MetaCurrent >= MetaComplete;
}

/// <summary>What the Wizard's Vault widget draws.</summary>
public sealed record VaultPanel(
    string Daily, string Weekly, string Claim, string Reset, string? Age, bool HasClaim);

/// <summary>
/// The two home screen widgets, worked out away from Android.
///
/// A widget is drawn by the launcher from a `RemoteViews` tree, which can hold text and little
/// else — no bindings, no converters, nothing that can ask a question later. Everything it shows
/// has to arrive as a finished string, so all of the deciding happens here where it can be
/// tested, and the provider only sets text.
/// </summary>
public static class WidgetContent
{
    /// <summary>
    /// The next world bosses, preferring ones this account still needs today.
    /// </summary>
    /// <param name="done">
    /// Boss ids already killed, from /v2/account/worldbosses, or null if never read.
    /// </param>
    /// <param name="doneAt">
    /// When that set was read. Completions belong to a UTC day and the endpoint empties at
    /// reset, so a set read before the last reset describes yesterday — trusting it would hide
    /// bosses the player has not killed, which is the failure this argument exists to prevent.
    /// </param>
    public static BossPanel Bosses(
        IReadOnlyList<(string Name, string ApiId, string Location, int Level, DateTimeOffset Next)> appearances,
        IReadOnlySet<string>? done,
        DateTimeOffset? doneAt,
        DateTimeOffset now)
    {
        var trusted = done is not null && doneAt is { } read && read >= Resets.LastDaily(now)
            ? done
            : null;

        var calls = appearances
            .OrderBy(boss => boss.Next)
            .Select(boss => new BossLine(
                boss.Name, boss.Location, boss.Next,
                trusted is null ? null : !trusted.Contains(boss.ApiId)))
            .ToList();

        // Lead with one still to do. An account that has killed everything still gets a
        // timetable rather than an empty widget — the day is not over, only the checklist.
        var outstanding = calls.Where(call => call.Needed != false).ToList();
        var shown = outstanding.Count > 0 ? outstanding : calls;

        var footnote = trusted is null
            ? string.Empty
            : L.F("{0} of {1} timed bosses left", calls.Count(call => call.Needed == true), calls.Count);

        return new BossPanel(shown.FirstOrDefault(), shown.Skip(1).Take(2).ToList(), footnote);
    }

    /// <summary>
    /// The vault's two clocks and whatever is waiting to be collected.
    /// </summary>
    /// <param name="readAt">
    /// When the tracks were last read, or null if never. Age is stated only once it is old
    /// enough to matter: a widget refreshed a quarter of an hour ago saying so is noise, and
    /// one showing yesterday's dailies in silence is a lie.
    /// </param>
    public static VaultPanel Vault(
        TrackCounts? daily, TrackCounts? weekly, DateTimeOffset? readAt, DateTimeOffset now)
    {
        var claimable = (daily?.Claimable ?? 0) + (weekly?.Claimable ?? 0);

        return new VaultPanel(
            Progress(daily),
            Progress(weekly),
            claimable > 0 ? L.F("{0} to claim", claimable) : string.Empty,
            L.F("Resets in {0}", Until(Resets.NextDaily(now) - now)),
            readAt is { } at && now - at >= StaleAfter ? Stale.Age(at, now) : null,
            claimable > 0);
    }

    /// <summary>Anything older than this gets its age printed on the widget.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);

    /// <summary>An em dash for a track never read, rather than a confident "0 / 0".</summary>
    private static string Progress(TrackCounts? track) =>
        track is null || track.MetaComplete <= 0
            ? "—"
            : $"{Math.Min(track.MetaCurrent, track.MetaComplete)} / {track.MetaComplete}";

    /// <summary>
    /// "11h 42m", "42m", "now". Hours drop off once there are none, because a widget line has
    /// room for the number that is changing and not for the one that is zero.
    /// </summary>
    public static string Until(TimeSpan left)
    {
        if (left <= TimeSpan.Zero) return L.T("now");

        var hours = (int)left.TotalHours;
        var minutes = left.Minutes;

        return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
    }
}
