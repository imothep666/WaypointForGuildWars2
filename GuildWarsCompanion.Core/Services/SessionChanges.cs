using GuildWarsCompanion.Data;

namespace GuildWarsCompanion.Services;

/// <summary>A character's crafting rating, before a session and after it.</summary>
public sealed record CraftingRise(string Character, string Discipline, int From, int To);

/// <summary>A mastery track's levels trained, before a session and after it.</summary>
public sealed record MasteryRise(int Track, int From, int To);

/// <summary>
/// What a session changed beyond playtime and the wallet: the difference of two details. A part
/// either end could not read is null, so the page can say it was not recorded rather than nothing.
/// </summary>
/// <param name="Items">Each item whose count moved, by how much; gains and losses both.</param>
/// <param name="Achievements">Achievements finished, or finished again.</param>
/// <param name="Vault">Wizard's Vault objectives that became done.</param>
/// <param name="MasteryPoints">Mastery points earned, by the API's English region name.</param>
public sealed record SessionChanges(
    IReadOnlyDictionary<int, int>? Items,
    IReadOnlyList<int>? Achievements,
    IReadOnlyList<VaultMark>? Vault,
    IReadOnlyDictionary<string, int>? MasteryPoints,
    IReadOnlyList<MasteryRise>? Masteries,
    IReadOnlyList<CraftingRise>? Crafting)
{
    public static SessionChanges Between(LedgerDetail before, LedgerDetail after) =>
        new(Both(before.Items, after.Items, ItemsMoved),
            Both(before.Achievements, after.Achievements, Finished),
            Both(before.Vault, after.Vault, VaultDone),
            Both(before.MasteryPoints, after.MasteryPoints, PointsEarned),
            Both(before.Masteries, after.Masteries, Trained),
            Both(before.Crafting, after.Crafting, Crafted));

    private static TResult? Both<T, TResult>(T? before, T? after, Func<T, T, TResult> diff)
        where T : class where TResult : class =>
        before is null || after is null ? null : diff(before, after);

    private static IReadOnlyDictionary<int, int> ItemsMoved(
        IReadOnlyDictionary<int, int> before, IReadOnlyDictionary<int, int> after)
    {
        var moved = new Dictionary<int, int>();

        foreach (var id in before.Keys.Union(after.Keys))
        {
            var change = after.GetValueOrDefault(id) - before.GetValueOrDefault(id);
            if (change != 0) moved[id] = change;
        }

        return moved;
    }

    /// <summary>Done now and not before, or repeated more often: a repeatable one starts over.</summary>
    private static IReadOnlyList<int> Finished(
        IReadOnlyDictionary<int, AchievementMark> before, IReadOnlyDictionary<int, AchievementMark> after) =>
        after
            .Where(pair => before.GetValueOrDefault(pair.Key) is not { } was
                ? pair.Value.Done || pair.Value.Repeated > 0
                : pair.Value.Done && !was.Done || pair.Value.Repeated > was.Repeated)
            .Select(pair => pair.Key)
            .Order()
            .ToList();

    /// <summary>
    /// Done now and not done before. Matched by track and id, so an objective of a new day that
    /// was done during the session counts; the API keeps yesterday's until the game is entered,
    /// which makes a session over the reset come out right too.
    /// </summary>
    private static IReadOnlyList<VaultMark> VaultDone(IReadOnlyList<VaultMark> before, IReadOnlyList<VaultMark> after)
    {
        var done = before.Where(mark => mark.Done).Select(mark => (mark.Track, mark.Id)).ToHashSet();

        return after.Where(mark => mark.Done && !done.Contains((mark.Track, mark.Id))).ToList();
    }

    private static IReadOnlyDictionary<string, int> PointsEarned(
        IReadOnlyDictionary<string, int> before, IReadOnlyDictionary<string, int> after) =>
        after
            .Select(pair => (pair.Key, Earned: pair.Value - before.GetValueOrDefault(pair.Key)))
            .Where(region => region.Earned > 0)
            .ToDictionary(region => region.Key, region => region.Earned);

    private static IReadOnlyList<MasteryRise> Trained(
        IReadOnlyDictionary<int, int> before, IReadOnlyDictionary<int, int> after) =>
        after
            .Where(pair => pair.Value > before.GetValueOrDefault(pair.Key))
            .Select(pair => new MasteryRise(pair.Key, before.GetValueOrDefault(pair.Key), pair.Value))
            .ToList();

    private static IReadOnlyList<CraftingRise> Crafted(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> before,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> after)
    {
        var rises = new List<CraftingRise>();

        foreach (var (character, disciplines) in after)
        {
            // A character made during the session starts every discipline at nothing.
            var was = before.GetValueOrDefault(character);

            foreach (var (discipline, rating) in disciplines)
            {
                var from = was?.GetValueOrDefault(discipline) ?? 0;
                if (rating > from) rises.Add(new CraftingRise(character, discipline, from, rating));
            }
        }

        return rises;
    }

    /// <summary>
    /// What the items that moved are worth, at what buyers offer: gains less losses, for the items
    /// a price is known for. Account bound items have none and count for nothing.
    /// </summary>
    public static long Worth(IReadOnlyDictionary<int, int> items, IReadOnlyDictionary<int, long> prices) =>
        items.Sum(pair => pair.Value * prices.GetValueOrDefault(pair.Key));
}
