namespace GuildWarsCompanion.Services;

/// <summary>A boss about to appear that this account has not killed today.</summary>
public sealed record BossCall(
    string Name, string ApiId, string Location, DateTimeOffset At, int MinutesAway)
{
    /// <summary>
    /// One alert per appearance, not per boss — the same boss tomorrow is news again, and the
    /// same boss on this poll and the next is not.
    /// </summary>
    public string Key => $"boss:{ApiId}:{At.UtcDateTime:yyyyMMddHHmm}";
}

/// <summary>
/// Choosing which world bosses are worth interrupting someone for.
///
/// This is the one thing a companion app can do that the game cannot: the game will not tell
/// you that a boss you still need is fifteen minutes out while you are on a train.
/// </summary>
public static class BossAlerts
{
    /// <summary>
    /// How far ahead to look.
    ///
    /// Tied to the background poll, which runs every thirty minutes: a shorter window would
    /// drop any appearance that falls in the gap between two polls and never mention it at
    /// all. The five minutes of slack cover a poll that Android runs late, which it often
    /// does. The cost is that some notice arrives half an hour early rather than fifteen
    /// minutes, which is the right way round to be wrong.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(35);

    /// <summary>
    /// At most this many at once. Early in the day nothing has been killed yet and half the
    /// timetable can fall inside the window; three notifications arriving together would
    /// teach the user to swipe them away without reading.
    /// </summary>
    public const int MostAtOnce = 2;

    /// <summary>
    /// The soonest appearances inside the window that this account still needs.
    ///
    /// <paramref name="remaining"/> is the set of boss ids not yet completed today. A boss
    /// with no scheduled time simply never appears in <paramref name="appearances"/>, which
    /// is how the unscheduled ones exclude themselves.
    /// </summary>
    public static IReadOnlyList<BossCall> Due(
        IEnumerable<(string Name, string ApiId, string Location, int Level, DateTimeOffset Next)> appearances,
        IReadOnlySet<string> remaining,
        DateTimeOffset utcNow) =>
        appearances
            .Where(boss => remaining.Contains(boss.ApiId))
            .Where(boss => boss.Next > utcNow && boss.Next - utcNow <= Window)
            .OrderBy(boss => boss.Next)
            .Take(MostAtOnce)
            .Select(boss => new BossCall(
                boss.Name,
                boss.ApiId,
                boss.Location,
                boss.Next,
                // Round rather than truncate: something 89 seconds out is "in 1 minute", not
                // "in 1 minute" only once it has been waiting for two.
                (int)Math.Round((boss.Next - utcNow).TotalMinutes, MidpointRounding.AwayFromZero)))
            .ToList();

    /// <summary>"Claw of Jormag in 12 minutes".</summary>
    public static string Title(BossCall call) => call.MinutesAway switch
    {
        <= 0 => L.F("{0} is up now", call.Name),
        1 => L.F("{0} in a minute", call.Name),
        _ => L.F("{0} in {1} minutes", call.Name, call.MinutesAway)
    };

    /// <summary>The line underneath: where, and why it is being mentioned at all.</summary>
    public static string Body(BossCall call) =>
        L.F("{0} — you have not done this one today.", call.Location);
}
