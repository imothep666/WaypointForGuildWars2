using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// The pinned goals as Today's rows: how much of each is in hand, what is short, and what buying
/// the rest would cost.
///
/// Recipes come from the downloaded catalogue, not the API: the item page may spend forty recipe
/// lookups on one item behind a tap, and Today refreshing several goals each time it opens could
/// not. Without the catalogue a goal counts only itself, and says why. Holdings are counted, not
/// priced; see <see cref="StorageService.CountAsync"/>.
/// </summary>
public sealed class GoalService(GoalStore store, Gw2Cache cache, StorageService storage, ItemLookup items, Gw2Client client)
{
    /// <summary>How many recipes one goal may read from the catalogue. Local and cheap, but a
    /// cycle in the data must still end.</summary>
    private const int LookupBudget = 200;

    /// <summary>See <see cref="GoalStore.Version"/>.</summary>
    public int Version => store.Version;

    /// <summary>Null when nothing is pinned, so Today shows no empty section.</summary>
    public async Task<IReadOnlyList<TodayRow>?> RowsAsync(CancellationToken ct = default)
    {
        if (await CountAsync(ct) is not { } counted) return null;

        var rows = new List<TodayRow>();

        foreach (var (goal, count) in counted.Goals)
            rows.Add(new TodayRow(
                Title(goal),
                count.InHand, count.Total,
                await CaptionAsync(count, counted.Catalogue, counted.Unreadable, ct),
                Route: $"item?id={goal.ItemId}"));

        return rows;
    }

    /// <summary>
    /// The goals that have everything in hand. For the reminder, which needs no words about
    /// what is short, only whether anything is.
    /// </summary>
    public async Task<IReadOnlyList<Goal>> ReadyAsync(CancellationToken ct = default) =>
        await CountAsync(ct) is { } counted
            ? counted.Goals.Where(pair => pair.Count.IsReady).Select(pair => pair.Goal).ToList()
            : [];

    public static string Title(Goal goal) => goal.Quantity > 1 ? $"{goal.Name} × {goal.Quantity}" : goal.Name;

    private async Task<(IReadOnlyList<(Goal Goal, GoalCount Count)> Goals, bool Catalogue, IReadOnlyList<string> Unreadable)?>
        CountAsync(CancellationToken ct)
    {
        var goals = await store.ListAsync(ct);
        if (goals.Count == 0) return null;

        var (held, unreadable) = await storage.CountAsync(ct);
        var catalogue = await cache.CountRecipesAsync(ct) > 0;

        var counted = new List<(Goal, GoalCount)>();

        foreach (var goal in goals)
        {
            var shapes = catalogue ? await GatherAsync(goal.ItemId, ct) : new Dictionary<int, RecipeShape>();
            counted.Add((goal, GoalProgress.Of(goal.ItemId, goal.Quantity, shapes.GetValueOrDefault, held)));
        }

        return (counted, catalogue, unreadable);
    }

    /// <summary>The recipes behind one item, walked outward through the catalogue.</summary>
    private async Task<Dictionary<int, RecipeShape>> GatherAsync(int itemId, CancellationToken ct)
    {
        var shapes = new Dictionary<int, RecipeShape>();
        var seen = new HashSet<int>();
        var frontier = new Queue<int>([itemId]);

        while (frontier.Count > 0 && seen.Count < LookupBudget)
        {
            var id = frontier.Dequeue();
            if (!seen.Add(id)) continue;

            if (await cache.RecipeShapeAsync(id, ct) is not { } shape) continue;

            shapes[id] = shape;

            foreach (var (ingredient, _) in shape.Ingredients) frontier.Enqueue(ingredient);
        }

        return shapes;
    }

    /// <summary>
    /// "Short 20 Lump of Primordium, 10 Lump of Coal and 3 more · about 3g 20s to buy", or why the
    /// count is narrower than it looks.
    /// </summary>
    private async Task<string> CaptionAsync(
        GoalCount count, bool catalogue, IReadOnlyList<string> unreadable, CancellationToken ct)
    {
        var parts = new List<string>();

        if (count.IsReady)
        {
            parts.Add("Everything is in hand");
        }
        else
        {
            var shown = count.Short.Take(2).ToList();
            var names = await items.ResolveAsync(shown.Select(need => need.ItemId).ToList(), ct);

            var named = string.Join(", ", shown.Select(need =>
                $"{need.Count:N0} {names.GetValueOrDefault(need.ItemId)?.Name ?? $"item {need.ItemId}"}"));

            parts.Add(count.Short.Count > shown.Count
                ? $"Short {named} and {count.Short.Count - shown.Count} more"
                : $"Short {named}");

            parts.Add(await PriceAsync(count.Short, ct));
        }

        // What the count could not see, said rather than left to look complete.
        if (!catalogue) parts.Add("download the item catalogue to count its recipe");
        if (count.Truncated) parts.Add("stopped six levels down");
        if (unreadable.Count > 0) parts.Add($"not counted: {string.Join(", ", unreadable)}");

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// What buying everything short costs at what sellers ask — or that some of it is not on the
    /// market, Mystic Forge components and account-bound gifts above all. A total that left them
    /// out would be the cheapest-looking lie on the screen. Prices are never cached, so with no
    /// connection that is what it says.
    /// </summary>
    private async Task<string> PriceAsync(IReadOnlyList<CraftNeed> needs, CancellationToken ct)
    {
        try
        {
            var prices = (await client.GetPricesAsync(needs.Select(need => need.ItemId), ct))
                .ToDictionary(price => price.Id);

            return GoalPrice.Describe(needs, id => prices.GetValueOrDefault(id)?.Sells?.UnitPrice ?? 0);
        }
        catch (Gw2ApiException ex) when (ex.Kind is Gw2ErrorKind.Network or Gw2ErrorKind.ServiceUnavailable)
        {
            return "prices need a connection";
        }
        catch (Gw2ApiException)
        {
            // Every id unknown to the market answers 404 as a whole: none of it can be bought.
            return GoalPrice.Describe(needs, _ => 0);
        }
    }
}
