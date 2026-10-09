namespace GuildWarsCompanion.Services;

/// <summary>
/// Deciding what still needs fetching, and in what order.
///
/// The order is the interesting part. Filling the catalogue is 371 requests, and a phone can
/// lose its connection or the user can walk away at any point in that. Fetching the tradable
/// items first means the thing this was built for — searching the trading post — starts
/// working after the first third, and everything after that is a bonus rather than a
/// prerequisite.
/// </summary>
public static class CataloguePlan
{
    /// <summary>The API's own ceiling on ids per request.</summary>
    public const int BatchSize = 200;

    /// <summary>
    /// Ids that are not stored yet, tradable ones first, each group in ascending order so a
    /// resumed fill picks up predictably rather than re-shuffling what is left.
    /// </summary>
    public static IReadOnlyList<int> Missing(
        IEnumerable<int> everything, IReadOnlySet<int> known, IReadOnlySet<int> tradable) =>
        everything
            .Distinct()
            .Where(id => !known.Contains(id))
            .OrderBy(id => tradable.Contains(id) ? 0 : 1)
            .ThenBy(id => id)
            .ToList();

    /// <summary>
    /// Requests needed for a given number of ids. Used to tell the user what they are in for
    /// before they start.
    /// </summary>
    public static int BatchCount(int idCount) => (idCount + BatchSize - 1) / BatchSize;
}
