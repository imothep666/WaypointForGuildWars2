namespace GuildWarsCompanion.Services;

/// <summary>
/// Saying how old offline data is.
///
/// The wording carries the whole weight of the feature: a wallet total with no date on it is
/// a lie the moment the connection drops, and "cached" tells a player nothing about whether
/// to trust the number. Age does.
/// </summary>
public static class Stale
{
    /// <summary>
    /// "No connection — showing your wallet from 2 hours ago."
    ///
    /// <paramref name="what"/> names the data, lower case, so it drops into the sentence.
    /// Precision falls off with age deliberately: minutes matter for a trading post total,
    /// days do not.
    /// </summary>
    public static string Describe(string what, DateTimeOffset fetchedAt, DateTimeOffset now) =>
        L.F("No connection — showing {0} from {1}.", what, Age(fetchedAt, now));

    /// <summary>
    /// How long ago, in words. Clock skew and a snapshot written a moment ago both land on
    /// "just now" rather than a negative or a jarring "0 minutes".
    /// </summary>
    public static string Age(DateTimeOffset fetchedAt, DateTimeOffset now)
    {
        var elapsed = now - fetchedAt;

        return elapsed.TotalMinutes switch
        {
            < 2 => L.T("just now"),
            < 60 => L.F("{0} minutes ago", (int)elapsed.TotalMinutes),
            < 120 => L.T("an hour ago"),
            < 1440 => L.F("{0} hours ago", (int)elapsed.TotalHours),
            < 2880 => L.T("yesterday"),
            _ => L.F("{0} days ago", (int)elapsed.TotalDays)
        };
    }
}
