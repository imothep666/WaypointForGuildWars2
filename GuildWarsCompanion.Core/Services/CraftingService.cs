using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>One discipline on one character.</summary>
public sealed record DisciplineRow(string Name, string Character, int Rating, string RatingText)
{
    public double Fraction => Math.Clamp((double)Rating / CraftingLevels.Max, 0, 1);

    public bool IsCapped => Rating >= CraftingLevels.Max;
}

/// <summary>A recipe worth making to advance a discipline.</summary>
public sealed record LevellingRow(
    int ItemId, string Name, string CostText, string HoldingText, string RatingText);

/// <summary>One thing the account could make right now.</summary>
public sealed record MakeableItemRow(
    int ItemId, string Name, string? IconUrl, string? Rarity, string Detail)
{
    public Color RarityColor => Theme.RarityColor(Rarity);
}

/// <summary>What can be made outright, and what that answer is missing.</summary>
public sealed record MakeableView(
    string Summary, IReadOnlyList<MakeableItemRow> Rows, string Note);

/// <summary>What the crafting screen shows.</summary>
public sealed record CraftingView(
    IReadOnlyList<DisciplineRow> Disciplines,
    IReadOnlyList<LevellingRow> Suggestions,
    string? SuggestionHeading,
    string Note);

/// <summary>
/// What to make next to advance a discipline.
///
/// The API will not answer this: it knows recipes and it knows ratings, and nothing joins
/// them. What it does support is the arithmetic — which recipes are within reach, which still
/// grant experience (see <see cref="CraftingLevels"/>), and what each costs at today's prices.
/// Ranking by cost is the only ordering that is a fact rather than an opinion.
/// </summary>
public sealed class CraftingService(
    Gw2Client client, ItemLookup items, StorageService storage, OfflineReader offline,
    Gw2Cache cache)
{
    /// <summary>Enough to choose from without turning the screen into a catalogue.</summary>
    private const int Suggest = 12;

    /// <summary>
    /// A well-stocked account can make hundreds of things, most of them a stack of copper
    /// ingots. The list is capped and says so rather than scrolling for a minute.
    /// </summary>
    private const int MakeableCeiling = 40;

    /// <summary>
    /// The API caps ids per request at 200, and an account can know thousands of recipes.
    /// Four requests is a reasonable ceiling for one screen.
    /// </summary>
    private const int RecipeCeiling = 800;

    public async Task<CraftingView> GetAsync(string? discipline, CancellationToken ct = default)
    {
        var characters = (await offline.ReadAsync(
            "characters", Gw2JsonContext.Default.ListCharacter, client.GetCharactersAsync, ct)).Value;

        var disciplines = characters
            .SelectMany(c => (c.Crafting ?? []).Select(d => (Character: c.Name, Discipline: d)))
            .Where(x => x.Discipline.Rating > 0)
            .OrderByDescending(x => x.Discipline.Rating)
            .ThenBy(x => x.Discipline.Name)
            .Select(x => new DisciplineRow(
                x.Discipline.Name,
                x.Character,
                x.Discipline.Rating,
                CraftingLevels.Describe(x.Discipline.Rating)))
            .ToList();

        if (disciplines.Count == 0)
            return new CraftingView([], [], null, NoDisciplines);

        // Nothing chosen yet: lead with whatever is furthest from its cap, since that is where
        // advice is worth anything.
        var chosen = disciplines.FirstOrDefault(d => d.Name == discipline)
                     ?? disciplines.Where(d => !d.IsCapped)
                         .OrderBy(d => d.Rating)
                         .FirstOrDefault()
                     ?? disciplines[0];

        var (suggestions, note) = await SuggestAsync(chosen, ct);

        return new CraftingView(
            disciplines,
            suggestions,
            $"{chosen.Name} · {chosen.RatingText}",
            note);
    }

    /// <summary>
    /// Everything the account can make outright from what it is holding.
    ///
    /// Both halves are already on the phone — the recipe catalogue on disk and the storage
    /// scan — so this costs no requests at all beyond names for what comes out. It is a join
    /// nobody had written rather than a new thing to fetch.
    /// </summary>
    public async Task<MakeableView> GetMakeableAsync(CancellationToken ct = default)
    {
        if (await cache.CountRecipesAsync(ct) == 0)
            return new MakeableView(string.Empty, [], NoCatalogue);

        if (!storage.HasSnapshot)
            await storage.RefreshAsync(ct);

        var held = storage.Holdings;

        var characters = (await offline.ReadAsync(
            "characters", Gw2JsonContext.Default.ListCharacter, client.GetCharactersAsync, ct)).Value;

        var disciplines = characters
            .SelectMany(c => (c.Crafting ?? []).Select(d => (d.Name, Character: c.Name, d.Rating)))
            .Where(d => d.Rating > 0)
            .ToList();

        if (disciplines.Count == 0)
            return new MakeableView(string.Empty, [], NoDisciplines);

        // Recipes learned from an item as well as the auto-learned ones: the account endpoint
        // reports exactly the half the catalogue cannot infer from a rating.
        var learned = await LearnedAsync(ct);

        var rows = await cache.MakeableAsync(
            held, learned, disciplines.Max(d => d.Rating), ct);

        var plans = Makeable.Plan(rows, disciplines);

        var names = await items.ResolveAsync(
            plans.Select(p => p.OutputItemId).Distinct().ToList(), ct);

        var shown = plans
            .Take(MakeableCeiling)
            .Select(plan =>
            {
                var item = names.GetValueOrDefault(plan.OutputItemId);

                return new MakeableItemRow(
                    plan.OutputItemId,
                    item?.Name ?? $"Item {plan.OutputItemId}",
                    item?.Icon,
                    item?.Rarity,
                    Makeable.Describe(plan));
            })
            .ToList();

        var note = "Every ingredient already in your bags, bank or material storage — nothing " +
                   "to buy. Made by the character with the lowest rating that still qualifies, " +
                   "since crafting below your rating earns nothing.";

        if (plans.Count > shown.Count)
            note += $" Showing the {MakeableCeiling} with the most materials, of {plans.Count:N0}.";

        return new MakeableView(Makeable.Summarise(plans.Count), shown, note);
    }

    /// <summary>
    /// Recipes learned from an item — bought from a trainer or discovered.
    ///
    /// Allowed to be missing: without the 'unlocks' permission the list is just the
    /// auto-learned ones, which is most of a levelling account anyway.
    /// </summary>
    private async Task<IReadOnlyCollection<int>> LearnedAsync(CancellationToken ct)
    {
        try
        {
            return (await offline.ReadAsync(
                "account/recipes", Gw2JsonContext.Default.ListInt32,
                client.GetAccountRecipesAsync, ct)).Value;
        }
        catch (Gw2ApiException)
        {
            return [];
        }
    }

    private async Task<(IReadOnlyList<LevellingRow> Rows, string Note)> SuggestAsync(
        DisciplineRow chosen, CancellationToken ct)
    {
        if (chosen.IsCapped)
            return ([], $"{chosen.Name} is at {CraftingLevels.Max}. Nothing left to learn.");

        // The recipes come from the local catalogue, not from the account.
        //
        // /v2/account/recipes lists only recipes LEARNED FROM AN ITEM — bought or discovered.
        // An account with three disciplines under way reported eight, all of them rating 400.
        // Every auto-learned recipe, which is the entire levelling path, is invisible to it.
        // The catalogue has them all, and auto-learned means every account has them at rating.
        var stored = await cache.CountRecipesAsync(ct);
        if (stored == 0)
            return ([], NoCatalogue);

        // Widest span any recipe type uses, so nothing in range is missed before the exact
        // grey-recipe rule is applied below.
        var candidates = (await cache.RecipesForAsync(chosen.Name, chosen.Rating, 250, ct))
            .Where(r => CraftingLevels.StillTeaches(chosen.Rating, r.MinRating, r.Type))
            .Where(r => (r.Ingredients ?? []).Any(i => i.IsItem))
            .ToList();

        if (candidates.Count == 0)
            return ([], $"Nothing in the catalogue still grants {chosen.Name} experience at " +
                        $"{chosen.Rating}. Recipes bought from a trainer are not auto-learned, " +
                        "so the API cannot see those until you use them.");

        var priced = await PriceAsync(candidates, ct);

        var rows = priced
            .OrderBy(x => x.Cost)
            // More than one recipe can produce the same item — different ingredients, same
            // result. Listing the name twice reads as a bug, and the dearer one is never the
            // answer to "what is cheapest".
            .GroupBy(x => x.Recipe.OutputItemId)
            .Select(g => g.First())
            .Take(Suggest)
            .Select(x => Show(x.Recipe, x.Cost, x.Names))
            .ToList();

        return (rows, "Cheapest first, at what sellers are asking. Only auto-learned recipes " +
                      "that still grant experience at your rating — anything further above it " +
                      "has gone grey and teaches nothing.");
    }

    /// <summary>
    /// What one craft of each costs to buy the materials for. A recipe with an untradable
    /// ingredient is dropped rather than priced as though the ingredient were free.
    /// </summary>
    private async Task<List<(Recipe Recipe, long Cost, Dictionary<int, Item> Names)>> PriceAsync(
        List<Recipe> candidates, CancellationToken ct)
    {
        var ids = candidates
            .SelectMany(r => (r.Ingredients ?? []).Where(i => i.IsItem).Select(i => i.Id))
            .Concat(candidates.Select(r => r.OutputItemId))
            .Distinct()
            .ToList();

        var names = await items.ResolveAsync(ids, ct);

        Dictionary<int, ItemPrice> prices;
        try
        {
            prices = (await client.GetPricesAsync(ids, ct)).ToDictionary(p => p.Id);
        }
        catch (Gw2ApiException)
        {
            prices = [];
        }

        var priced = new List<(Recipe, long, Dictionary<int, Item>)>();

        foreach (var recipe in candidates)
        {
            long cost = 0;
            var complete = true;

            foreach (var ingredient in (recipe.Ingredients ?? []).Where(i => i.IsItem))
            {
                var ask = prices.GetValueOrDefault(ingredient.Id)?.Sells?.UnitPrice ?? 0;
                if (ask == 0) { complete = false; break; }

                cost += ask * ingredient.Count;
            }

            if (complete) priced.Add((recipe, cost, names));
        }

        return priced;
    }

    private LevellingRow Show(Recipe recipe, long cost, Dictionary<int, Item> names)
    {
        // How many you could make from what you already hold, so the cheapest option is not
        // automatically the one you have to go shopping for.
        var lines = (recipe.Ingredients ?? [])
            .Where(i => i.IsItem)
            .Select(i => new CraftLine(i.Id, string.Empty, i.Count, storage.HeldCount(i.Id)))
            .ToList();

        var batches = CraftPlan.Batches(lines);

        return new LevellingRow(
            recipe.OutputItemId,
            names.GetValueOrDefault(recipe.OutputItemId)?.Name ?? $"Item {recipe.OutputItemId}",
            Coin.Format(cost),
            batches switch
            {
                null => "materials not counted",
                0 => "none from stock",
                1 => "1 from stock",
                var many => $"{many:N0} from stock"
            },
            recipe.MinRating > 0 ? $"needs {recipe.MinRating}" : "no requirement");
    }

    private const string NoCatalogue =
        "No recipes downloaded yet. The API cannot say which recipes you know — it reports " +
        "only the ones learned from an item — so the guide reads the full recipe list " +
        "instead. Download it under Settings.";

    private const string NoDisciplines =
        "No crafting disciplines found. The API reports these per character, so a character " +
        "has to have taken one up.";
}
