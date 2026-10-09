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
        $"No connection — showing {what} from {Age(fetchedAt, now)}.";

    /// <summary>
    /// How long ago, in words. Clock skew and a snapshot written a moment ago both land on
    /// "just now" rather than a negative or a jarring "0 minutes".
    /// </summary>
    public static string Age(DateTimeOffset fetchedAt, DateTimeOffset now)
    {
        var elapsed = now - fetchedAt;

        return elapsed.TotalMinutes switch
        {
            < 2 => "just now",
            < 60 => $"{(int)elapsed.TotalMinutes} minutes ago",
            < 120 => "an hour ago",
            < 1440 => $"{(int)elapsed.TotalHours} hours ago",
            < 2880 => "yesterday",
            _ => $"{(int)elapsed.TotalDays} days ago"
        };
    }
}
