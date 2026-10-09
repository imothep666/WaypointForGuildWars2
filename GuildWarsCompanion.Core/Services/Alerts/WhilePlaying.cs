namespace GuildWarsCompanion.Services.Alerts;

/// <summary>
/// Which alerts still go to the phone while a character is in the game.
///
/// Quiet hours guess when somebody can play with a clock. The bridge knows when they are
/// playing, and at that moment the game is already telling them about a sale or a finished
/// vault objective. Only those duplicates are held back: a world boss reminder or a price watch
/// is something the game does not say, and playing is exactly when it is wanted.
///
/// Held back, not deferred — the same rule as quiet hours. The alert is marked seen, and the
/// player has just been told by the game anyway.
/// </summary>
public static class WhilePlaying
{
    /// <summary>
    /// Whether the bridge is worth asking at all. Most polls find nothing new, and finding a PC
    /// on the local network is not free, so it is only asked when its answer could change
    /// something.
    /// </summary>
    public static bool NeedsAsking(IEnumerable<AccountAlert> news) =>
        news.Any(alert => alert.GameSaysItToo);

    /// <summary>
    /// The alerts to show. Only a named character holds anything back: an unreachable bridge is
    /// ignorance, and every ambiguous case lets a notification through.
    /// </summary>
    public static IReadOnlyList<AccountAlert> Keep(IReadOnlyList<AccountAlert> news, LiveReading live) =>
        live.Name is null ? news : news.Where(alert => !alert.GameSaysItToo).ToList();
}
