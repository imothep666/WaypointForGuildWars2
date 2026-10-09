namespace GuildWarsCompanion.Services;

/// <summary>
/// The kinds of place an account can keep something. The order of these members is the order
/// locations are reported in, so it runs from the most shared to the most personal.
/// </summary>
public enum StoragePlace
{
    Materials,
    Bank,
    Shared,
    Character,
    Equipped
}

/// <summary>
/// A quantity of one item in one place. <paramref name="Where"/> distinguishes between
/// places of the same kind — one per character — and is empty for the account-wide ones.
/// </summary>
public sealed record Holding(StoragePlace Place, string Where, int ItemId, int Count);

/// <summary>
/// Turning raw slot readings into an answer to "where is my X?".
///
/// Every endpoint reports storage a slot at a time, so the same item in three bank slots
/// arrives as three separate readings. Collapsing those, ordering them so the answer reads
/// naturally, and phrasing them is all pure — and all of it is the kind of thing that goes
/// wrong quietly, producing a plausible number that is simply not the total.
/// </summary>
public static class StorageTally
{
    /// <summary>
    /// One entry per item per place, counts summed, ordered for reading: place order first,
    /// then the largest pile.
    ///
    /// Zero and negative counts are dropped. The API does not send them, but a slot that is
    /// present with nothing in it must not become "0 in the bank".
    /// </summary>
    public static IReadOnlyList<Holding> Collate(IEnumerable<Holding> holdings) =>
        holdings
            .Where(h => h.Count > 0)
            .GroupBy(h => (h.Place, h.Where, h.ItemId))
            .Select(g => new Holding(g.Key.Place, g.Key.Where, g.Key.ItemId, g.Sum(h => h.Count)))
            .OrderBy(h => h.Place)
            .ThenByDescending(h => h.Count)
            .ThenBy(h => h.Where, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>How one place is named mid-sentence.</summary>
    public static string PlaceName(StoragePlace place, string where) => place switch
    {
        StoragePlace.Materials => "in material storage",
        StoragePlace.Bank => "in the bank",
        StoragePlace.Shared => "in shared slots",
        StoragePlace.Character => $"on {where}",
        StoragePlace.Equipped => $"equipped on {where}",
        _ => "somewhere"
    };

    /// <summary>The heading a place gets when it is a row of its own rather than part of a sentence.</summary>
    public static string PlaceHeading(StoragePlace place, string where) => place switch
    {
        StoragePlace.Materials => "Material storage",
        StoragePlace.Bank => "Bank",
        StoragePlace.Shared => "Shared slots",
        StoragePlace.Character => where,
        StoragePlace.Equipped => $"{where} — equipped",
        _ => "Elsewhere"
    };

    /// <summary>
    /// "250 in material storage, 12 on Imothep and 3 in the bank" — the whole point of the
    /// feature, in one line.
    /// </summary>
    public static string Describe(IEnumerable<Holding> holdings)
    {
        var parts = Collate(holdings)
            .Select(h => $"{h.Count:N0} {PlaceName(h.Place, h.Where)}")
            .ToList();

        return parts.Count switch
        {
            0 => "None anywhere the app can see.",
            1 => parts[0],
            // "and" before the last, so a three-place answer reads as a sentence rather
            // than a log line.
            _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1]
        };
    }

    /// <summary>Everything held, across every place.</summary>
    public static int Total(IEnumerable<Holding> holdings) =>
        holdings.Where(h => h.Count > 0).Sum(h => h.Count);
}
