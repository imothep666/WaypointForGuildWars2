using GuildWarsCompanion.Data;

namespace GuildWarsCompanion.Services;

/// <summary>One character's part in a session.</summary>
public sealed record SessionCharacter(string Name, long Seconds, int LevelFrom, int LevelTo);

/// <summary>A stretch of play, as the ledger saw it.</summary>
/// <param name="Before">The last point before play began: what the session is measured from.</param>
/// <param name="LastSeen">The last point at which playtime had risen.</param>
/// <param name="After">The first point after it at which playtime had not; null while still playing.</param>
public sealed record Session(
    LedgerPoint Before, LedgerPoint LastSeen, LedgerPoint? After,
    long PlayedSeconds, IReadOnlyList<SessionCharacter> Characters, IReadOnlyDictionary<int, long> WalletChange)
{
    public bool IsOngoing => After is null;

    /// <summary>Where the session is measured to: the first quiet point, or the latest while playing.</summary>
    public LedgerPoint End => After ?? LastSeen;
}

/// <summary>
/// Finding the last session in the ledger, and saying it.
///
/// A session is a run of points between which some character's playtime rose — the API's own
/// counter, so the duration is what the game recorded, not the gap between two polls. The point
/// before the first rise is the state play started from; the first point after the last rise is
/// where it is measured to, so whatever happened in the last minutes before logging out is in it.
/// The points are half an hour apart at best, which decides only when a recap can be noticed, not
/// what it says.
/// </summary>
public static class SessionRecap
{
    public static Session? Latest(IReadOnlyList<LedgerPoint> points)
    {
        var last = -1;

        for (var i = points.Count - 1; i >= 1; i--)
            if (Played(points[i - 1], points[i]) > 0)
            {
                last = i;
                break;
            }

        if (last < 0) return null;

        var first = last;
        while (first >= 2 && Played(points[first - 2], points[first - 1]) > 0) first--;

        return Between(points, first, last);
    }

    /// <summary>Every session the ledger holds, oldest first; only the last can be ongoing.</summary>
    public static IReadOnlyList<Session> All(IReadOnlyList<LedgerPoint> points)
    {
        var sessions = new List<Session>();

        for (var i = 1; i < points.Count; i++)
        {
            if (Played(points[i - 1], points[i]) == 0) continue;

            var last = i;
            while (last + 1 < points.Count && Played(points[last], points[last + 1]) > 0) last++;

            sessions.Add(Between(points, i, last));
            i = last;
        }

        return sessions;
    }

    /// <summary>
    /// Whether anything the cheap half of a point reads moved between two points: playtime,
    /// a level, the wallet. Nothing in the game moves none of them for long, so an account
    /// that did not change by these did not change at all, and its last detail still holds.
    /// </summary>
    public static bool Changed(LedgerPoint from, LedgerPoint to) =>
        from.Characters.Count != to.Characters.Count ||
        to.Characters.Any(pair => !from.Characters.TryGetValue(pair.Key, out var was) || was != pair.Value) ||
        from.Wallet.Count != to.Wallet.Count ||
        to.Wallet.Any(pair => !from.Wallet.TryGetValue(pair.Key, out var was) || was != pair.Value);

    /// <summary>
    /// The point whose detail stands for <paramref name="at"/>: the latest one with a detail at or
    /// before it, provided the account did not change in between. Null when there is none, which
    /// is every session from before details were kept.
    /// </summary>
    public static LedgerPoint? DetailFor(IReadOnlyList<LedgerPoint> points, LedgerPoint at)
    {
        for (var i = points.Count - 1; i >= 0; i--)
        {
            var point = points[i];
            if (point.At > at.At) continue;

            if (point.HasDetail) return Changed(point, at) ? null : point;
        }

        return null;
    }

    /// <summary>
    /// The details worth keeping: those each session is measured from and to, and the latest,
    /// which the next session may start from. The rest are never read again.
    /// </summary>
    public static IReadOnlyCollection<DateTimeOffset> DetailsToKeep(IReadOnlyList<LedgerPoint> points)
    {
        var keep = new HashSet<DateTimeOffset>();

        foreach (var session in All(points))
        {
            if (DetailFor(points, session.Before) is { } before) keep.Add(before.At);
            if (DetailFor(points, session.End) is { } end) keep.Add(end.At);
        }

        if (points.LastOrDefault(point => point.HasDetail) is { } latest) keep.Add(latest.At);

        return keep;
    }

    /// <summary>The session over the rises from <paramref name="first"/> to <paramref name="last"/>.</summary>
    private static Session Between(IReadOnlyList<LedgerPoint> points, int first, int last)
    {
        var before = points[first - 1];
        var lastSeen = points[last];
        var after = last + 1 < points.Count ? points[last + 1] : null;
        var end = after ?? lastSeen;

        var seconds = new Dictionary<string, long>();

        for (var i = first; i <= last; i++)
            foreach (var (name, (age, _)) in points[i].Characters)
            {
                var was = points[i - 1].Characters.TryGetValue(name, out var earlier) ? earlier.Age : age;
                if (age > was) seconds[name] = seconds.GetValueOrDefault(name) + (age - was);
            }

        var characters = seconds
            .OrderByDescending(pair => pair.Value)
            .Select(pair =>
            {
                // A character made during the session has no level before it; it started at its first.
                var to = end.Characters.TryGetValue(pair.Key, out var after) ? after.Level : 0;
                var from = before.Characters.TryGetValue(pair.Key, out var earlier) ? earlier.Level : to;

                return new SessionCharacter(pair.Key, pair.Value, from, Math.Max(from, to));
            })
            .ToList();

        var wallet = new Dictionary<int, long>();

        foreach (var id in before.Wallet.Keys.Union(end.Wallet.Keys))
        {
            var change = end.Wallet.GetValueOrDefault(id) - before.Wallet.GetValueOrDefault(id);
            if (change != 0) wallet[id] = change;
        }

        return new Session(before, lastSeen, after, seconds.Values.Sum(), characters, wallet);
    }

    /// <summary>Seconds of play between two points, summed over the characters in both.</summary>
    public static long Played(LedgerPoint from, LedgerPoint to) =>
        to.Characters.Sum(pair =>
            from.Characters.TryGetValue(pair.Key, out var was) && pair.Value.Age > was.Age
                ? pair.Value.Age - was.Age
                : 0);

    /// <summary>"1 h 50 min", "25 min".</summary>
    public static string Duration(long seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);

        return span.TotalHours >= 1
            ? L.F("{0} h {1:00} min", (int)span.TotalHours, span.Minutes)
            : L.F("{0} min", Math.Max(1, (int)span.TotalMinutes));
    }

    /// <summary>"Sweet Child O Night · 1 h 20 min · level 79 → 80".</summary>
    public static string Line(SessionCharacter character) =>
        character.LevelTo > character.LevelFrom
            ? L.F("{0} · {1} · level {2} → {3}", character.Name, Duration(character.Seconds), character.LevelFrom, character.LevelTo)
            : $"{character.Name} · {Duration(character.Seconds)}";

    /// <summary>"Sweet Child O Night", or "Sweet Child O Night and 2 more".</summary>
    public static string Who(Session played) => played.Characters.Count switch
    {
        0 => L.T("No character named"),
        1 => played.Characters[0].Name,
        _ => L.F("{0} and {1} more", played.Characters[0].Name, played.Characters.Count - 1)
    };

    /// <summary>
    /// Copper an hour of play, or null for a session too short for a rate to mean anything: ten
    /// minutes spent selling would otherwise read as a fortune an hour.
    /// </summary>
    public static long? PerHour(long copper, long seconds) =>
        seconds < 15 * 60 ? null : (long)Math.Round(copper * 3600.0 / seconds);

    /// <summary>"+34g 20s in all · 15g 40s an hour": coin and items together.</summary>
    public static string Worth(long copper, long seconds)
    {
        var all = L.F("{0} in all", Signed(copper));

        return PerHour(copper, seconds) is { } hourly ? L.F("{0} · {1} an hour", all, Signed(hourly)) : all;
    }

    private static string Signed(long copper) => (copper < 0 ? "−" : "+") + Services.Coin.Format(Math.Abs(copper));

    /// <summary>The coin shown before every currency and as coin; then the rest by size.</summary>
    private const int Coin = 1;

    /// <summary>
    /// "+3g 40s 12c · +12,300 Karma · +450 Astral Acclaim and 2 more". Gains and losses both: a
    /// session spent buying a legendary is a session too.
    /// </summary>
    public static string Wallet(IReadOnlyDictionary<int, long> change, Func<int, string> name, int most = 4)
    {
        var parts = new List<string>();

        if (change.TryGetValue(Coin, out var copper))
            parts.Add((copper < 0 ? "−" : "+") + Services.Coin.Format(Math.Abs(copper)));

        var others = change.Where(pair => pair.Key != Coin).OrderByDescending(pair => Math.Abs(pair.Value)).ToList();

        parts.AddRange(others.Take(Math.Max(0, most - parts.Count))
            .Select(pair => $"{(pair.Value < 0 ? "−" : "+")}{Math.Abs(pair.Value):N0} {name(pair.Key)}"));

        var shown = parts.Count - (change.ContainsKey(Coin) ? 1 : 0);
        var rest = others.Count - shown;

        return parts.Count == 0
            ? L.T("the wallet did not change")
            : rest > 0 ? L.F("{0} and {1} more", string.Join(" · ", parts), rest) : string.Join(" · ", parts);
    }
}
