namespace GuildWarsCompanion.Services;

/// <summary>
/// When the game's clocks turn over.
///
/// These matter for more than counting down. The Wizard's Vault account endpoints do not roll
/// over at reset — they keep serving the previous cycle's objectives until the player next
/// logs into the game, a known and reported API defect. Detecting that means asking whether
/// the data we hold predates the last reset, which needs the reset boundary as a fact rather
/// than a guess.
/// </summary>
public static class Resets
{
    /// <summary>Dailies turn over at 00:00 UTC.</summary>
    public static DateTimeOffset LastDaily(DateTimeOffset utcNow) =>
        new(utcNow.UtcDateTime.Date, TimeSpan.Zero);

    public static DateTimeOffset NextDaily(DateTimeOffset utcNow) => LastDaily(utcNow).AddDays(1);

    /// <summary>The weekly clock: Monday 07:30 UTC.</summary>
    private static readonly TimeSpan WeeklyTime = new(7, 30, 0);

    public static DateTimeOffset LastWeekly(DateTimeOffset utcNow)
    {
        var utc = utcNow.UtcDateTime;

        // Days back to the most recent Monday, then the time of day decides whether this
        // week's reset has happened yet or we are still inside last week's.
        var daysSinceMonday = ((int)utc.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var candidate = new DateTimeOffset(utc.Date.AddDays(-daysSinceMonday) + WeeklyTime, TimeSpan.Zero);

        return candidate <= utcNow ? candidate : candidate.AddDays(-7);
    }

    public static DateTimeOffset NextWeekly(DateTimeOffset utcNow) => LastWeekly(utcNow).AddDays(7);

    /// <summary>
    /// The reset a Wizard's Vault track answers to, or null for one that has no cycle.
    ///
    /// The special track runs for the length of a season, so nothing about it can be stale in
    /// the sense that matters here.
    /// </summary>
    public static DateTimeOffset? LastFor(string track, DateTimeOffset utcNow) => track switch
    {
        "daily" => LastDaily(utcNow),
        "weekly" => LastWeekly(utcNow),
        _ => null
    };
}
