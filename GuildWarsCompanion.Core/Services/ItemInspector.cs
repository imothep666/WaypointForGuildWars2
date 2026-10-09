using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>One line of a recipe, ready to show.</summary>
public sealed record CraftLineView(
    int ItemId, string Name, string? IconUrl, string NeededText, string HeldText, bool IsSatisfied)
{
    /// <summary>Quiet once you have enough; brass while it is still standing in your way.</summary>
    public Color EdgeColor => IsSatisfied ? Theme.Banked : Theme.Brass;
}

/// <summary>What it takes to make this, and whether you can.</summary>
public sealed record CraftView(
    string Heading,
    string Verdict,
    string SourceNote,
    IReadOnlyList<CraftLineView> Lines,
    string? ShortfallCost,
    IReadOnlyList<CraftLineView>? Materials = null,
    string? MaterialsNote = null,
    string? MakeOrBuy = null,
    string? Margin = null)
{
    public bool HasMakeOrBuy => !string.IsNullOrEmpty(MakeOrBuy);
    public bool HasMargin => !string.IsNullOrEmpty(Margin);

    public bool HasShortfallCost => !string.IsNullOrEmpty(ShortfallCost);

    /// <summary>
    /// Only when the full expansion says something the ingredient list does not. For an ingot
    /// made straight from ore the two are identical, and repeating it would be noise.
    /// </summary>
    public bool HasMaterials => Materials is { Count: > 0 };

    public bool HasMaterialsNote => !string.IsNullOrEmpty(MaterialsNote);
}

/// <summary>Everything worth showing about one item, already phrased.</summary>
public sealed record ItemView(
    int Id,
    string Name,
    string? IconUrl,
    string? Rarity,
    string Kind,
    string? Description,
    string? Facts,
    string BuyText,
    string SellText,
    bool HasMarket,
    string MarketNote,
    string VendorText,
    string? WhereText,
    string WikiUrl,
    CraftView? Craft = null,
    string? UsedInSummary = null,
    IReadOnlyList<UsedInItemRow>? UsedIn = null)
{
    public Color RarityColor => Theme.RarityColor(Rarity);

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool HasFacts => !string.IsNullOrWhiteSpace(Facts);
    public bool HasWhere => !string.IsNullOrWhiteSpace(WhereText);
    public bool HasCraft => Craft is not null;

    /// <summary>What this goes into, which is the other half of the crafting question.</summary>
    public bool HasUsedIn => UsedIn is { Count: > 0 };

    /// <summary>
    /// Said only when nothing makes this.
    ///
    /// The API has no drop tables, no salvage results and no gathering locations — /v2/salvage
    /// and /v2/loot do not exist, and /v2/nodes lists home-instance categories rather than
    /// anything per item. So the app can say "buy it" and "craft it" and must hand the third
    /// case over rather than invent it.
    /// </summary>
    public string? Acquisition => HasCraft
        ? null
        : HasMarket
            ? "No recipe makes this. Buy it above, or gather it — the wiki says where."
            : "No recipe makes this, and it is not traded. The wiki says where it comes from.";

    /// <summary>
    /// The button answers the question being asked. On something you have to go and find,
    /// "open the wiki" buries the point.
    /// </summary>
    public string WikiLabel => HasCraft ? "OPEN THE WIKI" : "WHERE TO FIND THIS";
}

/// <summary>
/// One item, looked at closely: what it is, what it is worth, where yours are, and a way
/// through to the wiki for everything the API cannot say.
///
/// The wiki link matters more than it looks. The API knows an item's name, icon and price and
/// almost nothing about how it is used — no recipes, no drop sources, no acquisition. That is
/// all on the wiki, so the honest thing is to hand the player over rather than pretend.
/// </summary>
/// <summary>One thing this item goes into, ready to show.</summary>
public sealed record UsedInItemRow(int ItemId, string Name, string Detail);

public sealed class ItemInspector(
    Gw2Client client, StorageService storage, ItemLookup items, Gw2Cache cache)
{
    /// <summary>
    /// Material storage, read once, as a stand-in when the full storage snapshot has not been
    /// taken. Crafting ingredients almost always live there, so it answers most of the
    /// question for one request instead of four — and the screen says which it used.
    /// </summary>
    private Dictionary<int, int>? _materials;

    public async Task<ItemView?> GetAsync(int id, CancellationToken ct = default)
    {
        var item = await client.GetItemDetailAsync(id, ct);
        if (item is null) return null;

        var (buy, sell, hasMarket, note, buyPrice, sellPrice) = await ReadMarketAsync(id, ct);

        var usedIn = await UsedInAsync(id, ct);

        return new ItemView(
            item.Id,
            item.Name,
            item.Icon,
            item.Rarity,
            Kind(item),
            Prose.Clean(item.Description),
            ItemFacts.Summarise(item.Flags),
            buy,
            sell,
            hasMarket,
            note,
            item.VendorValue > 0 ? Coin.Format(item.VendorValue) : "cannot be sold to a vendor",
            storage.WhereIs(id),
            Wiki.UrlFor(item.Name),
            await CraftAsync(id, buyPrice, sellPrice, ct),
            usedIn.Summary,
            usedIn.Rows);
    }

    /// <summary>How many recipes to list before the page becomes a catalogue.</summary>
    private const int UsedInCeiling = 15;

    /// <summary>
    /// What this item goes into.
    ///
    /// Straight off the local catalogue: no request, and it answers with no connection. Quiet
    /// when the catalogue has not been downloaded — an item page is worth having either way.
    /// </summary>
    private async Task<(string? Summary, IReadOnlyList<UsedInItemRow>? Rows)> UsedInAsync(
        int id, CancellationToken ct)
    {
        try
        {
            var total = await cache.UsedInCountAsync(id, ct);
            if (total == 0) return (null, null);

            var rows = await cache.UsedInAsync(id, UsedInCeiling, ct);

            return (UsedIn.Summarise(rows.Count, total),
                rows.Select(row => new UsedInItemRow(
                        row.OutputItemId,
                        row.OutputName ?? $"Item {row.OutputItemId}",
                        UsedIn.Describe(row)))
                    .ToList());
        }
        catch (Exception)
        {
            // Decoration on this page; the item itself is the point.
            return (null, null);
        }
    }

    /// <summary>
    /// What makes this item, measured against what you are holding.
    ///
    /// Most items have no recipe, so the search is one cheap request that usually ends here.
    /// </summary>
    private async Task<CraftView?> CraftAsync(
        int itemId, long buyPrice, long sellPrice, CancellationToken ct)
    {
        try
        {
            var found = await client.FindRecipesForAsync(itemId, ct);
            if (found.Count == 0) return null;

            var recipes = await client.GetRecipesAsync(found.Take(1), ct);
            var recipe = recipes.FirstOrDefault();

            var ingredients = (recipe?.Ingredients ?? []).Where(i => i.IsItem).ToList();
            if (recipe is null || ingredients.Count == 0) return null;

            var (count, source) = await CountsAsync(ct);
            var names = await items.ResolveAsync(ingredients.Select(i => i.Id).ToList(), ct);

            var lines = ingredients
                .Select(i => new CraftLine(
                    i.Id,
                    names.GetValueOrDefault(i.Id)?.Name ?? $"Item {i.Id}",
                    i.Count,
                    count(i.Id)))
                .ToList();

            var known = source.Length > 0;

            var (materials, materialsNote) = await ExpandAsync(itemId, recipe, count, known, ct);

            // What one batch costs to buy the materials for, which is the basis of both
            // verdicts below. Zero when anything in it is untraded — a comparison against a
            // partial cost would flatter making it every time.
            var batchCost = await BatchCostAsync(ingredients, ct);

            return new CraftView(
                Heading(recipe, found.Count),
                CraftPlan.Verdict(lines, recipe.OutputItemCount, known),
                known ? $"Counted from {source}." : "Could not read what you are holding.",
                lines.Select(line => Show(line, names.GetValueOrDefault(line.ItemId)?.Icon)).ToList(),
                await ShortfallCostAsync(lines, ct),
                materials,
                materialsNote,
                CraftValue.AgainstBuying(batchCost, recipe.OutputItemCount, sellPrice),
                CraftValue.AsProfit(batchCost, recipe.OutputItemCount, buyPrice));
        }
        catch (Gw2ApiException)
        {
            // The rest of the item page is still worth showing.
            return null;
        }
    }

    /// <summary>How many recipe lookups one item is worth before we stop asking.</summary>
    private const int LookupBudget = 40;

    /// <summary>
    /// Everything needed to make one, expanded past the intermediates you would have to craft
    /// on the way — and skipping whatever you already hold.
    ///
    /// Returned only when it says something the ingredient list above it does not. An ingot
    /// made straight from ore expands to exactly its own ingredients, and showing the same
    /// three lines twice would be noise.
    /// </summary>
    private async Task<(IReadOnlyList<CraftLineView>? Lines, string? Note)> ExpandAsync(
        int itemId, Recipe recipe, Func<int, int?> count, bool known, CancellationToken ct)
    {
        var shapes = await GatherAsync(itemId, recipe, ct);

        // Anything not held is unknown rather than zero; the walk spends only what it can see.
        var budget = new Dictionary<int, int>();
        foreach (var id in shapes.SelectMany(pair => pair.Value.Ingredients.Select(i => i.ItemId))
                     .Concat(shapes.Keys)
                     .Distinct())
        {
            if (count(id) is { } have && have > 0) budget[id] = have;
        }

        var needs = CraftTree.Materials(itemId, recipe.OutputItemCount, Lookup, budget, out var deep);

        // Same set as the immediate ingredients: the expansion added nothing.
        var immediate = (recipe.Ingredients ?? []).Where(i => i.IsItem).Select(i => i.Id).ToHashSet();
        if (needs.Count == 0 || needs.Select(n => n.ItemId).ToHashSet().SetEquals(immediate))
            return (null, null);

        var names = await items.ResolveAsync(needs.Select(n => n.ItemId).ToList(), ct);

        var lines = needs
            .Select(need => Show(
                new CraftLine(
                    need.ItemId,
                    names.GetValueOrDefault(need.ItemId)?.Name ?? $"Item {need.ItemId}",
                    need.Count,
                    // These are what is still wanted after holdings were spent, so anything
                    // listed here is by definition not held.
                    known ? 0 : null),
                names.GetValueOrDefault(need.ItemId)?.Icon))
            .ToList();

        var note = deep
            ? "Stopped following the chain at six levels — the rest is listed as it stands."
            : known
                ? "What is left to gather or buy after what you are already holding."
                : "What one costs in raw materials. Open Find to have your own counted.";

        return (lines, note);

        RecipeShape? Lookup(int id) => shapes.GetValueOrDefault(id);
    }

    /// <summary>
    /// Walks outward from the item collecting the recipes behind it, one lookup at a time —
    /// the API has no bulk "what makes these" — and stops at a budget so a deep chain cannot
    /// turn one tap into a hundred requests.
    /// </summary>
    private async Task<Dictionary<int, RecipeShape>> GatherAsync(
        int itemId, Recipe recipe, CancellationToken ct)
    {
        var shapes = new Dictionary<int, RecipeShape> { [itemId] = Shape(recipe) };
        var seen = new HashSet<int> { itemId };
        var frontier = new Queue<int>((recipe.Ingredients ?? []).Where(i => i.IsItem).Select(i => i.Id));
        var spent = 0;

        while (frontier.Count > 0 && spent < LookupBudget)
        {
            var id = frontier.Dequeue();
            if (!seen.Add(id)) continue;

            spent++;

            var found = await client.FindRecipesForAsync(id, ct);
            if (found.Count == 0) continue;

            var made = (await client.GetRecipesAsync(found.Take(1), ct)).FirstOrDefault();
            if (made is null) continue;

            shapes[id] = Shape(made);

            foreach (var ingredient in (made.Ingredients ?? []).Where(i => i.IsItem))
                frontier.Enqueue(ingredient.Id);
        }

        return shapes;
    }

    private static RecipeShape Shape(Recipe recipe) =>
        new(recipe.OutputItemCount,
            (recipe.Ingredients ?? []).Where(i => i.IsItem).Select(i => (i.Id, i.Count)).ToList());

    /// <summary>
    /// What the materials for one batch cost at what sellers are asking.
    ///
    /// Returns zero if any ingredient is untraded: comparing a partial cost against a full
    /// purchase price would make crafting look cheaper than it is, every single time.
    /// </summary>
    private async Task<long> BatchCostAsync(
        IReadOnlyList<RecipeIngredient> ingredients, CancellationToken ct)
    {
        try
        {
            var prices = (await client.GetPricesAsync(ingredients.Select(i => i.Id), ct))
                .ToDictionary(p => p.Id);

            long total = 0;
            foreach (var ingredient in ingredients)
            {
                var ask = prices.GetValueOrDefault(ingredient.Id)?.Sells?.UnitPrice ?? 0;
                if (ask == 0) return 0;

                total += ask * ingredient.Count;
            }

            return total;
        }
        catch (Gw2ApiException)
        {
            return 0;
        }
    }

    private static CraftLineView Show(CraftLine line, string? icon) =>
        new(line.ItemId,
            line.Name,
            icon,
            $"{line.Needed:N0} needed",
            line.Held is null ? "not counted" : $"{line.Held.Value:N0} held",
            line.IsSatisfied);

    /// <summary>"Refinement · Artificer 75", plus a note when other disciplines make it too.</summary>
    private static string Heading(Recipe recipe, int recipeCount)
    {
        var discipline = recipe.Disciplines?.FirstOrDefault();
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(recipe.Type)) parts.Add(ItemFacts.ReadableType(recipe.Type));
        if (discipline is not null)
            parts.Add(recipe.MinRating > 0 ? $"{discipline} {recipe.MinRating}" : discipline);

        var heading = parts.Count > 0 ? string.Join(" · ", parts) : "Recipe";

        return recipeCount > 1 ? $"{heading} · {recipeCount - 1} other recipe(s)" : heading;
    }

    /// <summary>
    /// Where the held counts come from. The full snapshot when the Find screen has been
    /// opened, material storage alone otherwise — never a silent guess.
    /// </summary>
    private async Task<(Func<int, int?> Count, string Source)> CountsAsync(CancellationToken ct)
    {
        if (storage.HasSnapshot)
            return (storage.HeldCount, "everywhere you keep things");

        try
        {
            _materials ??= (await client.GetAccountMaterialsAsync(ct))
                .Where(material => material.Count > 0)
                .ToDictionary(material => material.Id, material => material.Count);

            return (id => _materials.GetValueOrDefault(id), "material storage");
        }
        catch (Gw2ApiException)
        {
            return (_ => null, string.Empty);
        }
    }

    /// <summary>
    /// What buying the rest would cost right now, at what sellers are asking — that is the
    /// price of not waiting, which is the decision being made.
    /// </summary>
    private async Task<string?> ShortfallCostAsync(
        IReadOnlyList<CraftLine> lines, CancellationToken ct)
    {
        var missing = CraftPlan.Missing(lines);
        if (missing.Count == 0) return null;

        try
        {
            var prices = (await client.GetPricesAsync(missing.Select(m => m.ItemId), ct))
                .ToDictionary(p => p.Id);

            long total = 0;
            foreach (var line in missing)
            {
                var ask = prices.GetValueOrDefault(line.ItemId)?.Sells?.UnitPrice ?? 0;
                if (ask == 0) return null;   // something here is not traded; a total would lie

                total += ask * line.Short!.Value;
            }

            return $"Buying the rest costs about {Coin.Format(total)}.";
        }
        catch (Gw2ApiException)
        {
            return null;
        }
    }

    /// <summary>"Legendary Sword · level 80", or as much of it as the item has.</summary>
    private static string Kind(ItemDetail item)
    {
        var words = new List<string>();

        if (!string.IsNullOrWhiteSpace(item.Rarity)) words.Add(item.Rarity);

        // The subtype is the useful half: "Sword" says more than "Weapon".
        var type = item.Details?.Type ?? item.Type;
        if (!string.IsNullOrWhiteSpace(type)) words.Add(ItemFacts.ReadableType(type));

        var kind = string.Join(" ", words);

        return item.Level > 0 ? $"{kind} · level {item.Level}" : kind;
    }

    /// <summary>
    /// Both sides of the market. An item with no listings is not worth nothing — it is not
    /// traded at all, and saying so is the difference between "free" and "unavailable".
    /// </summary>
    private async Task<(string Buy, string Sell, bool HasMarket, string Note, long BuyPrice, long SellPrice)>
        ReadMarketAsync(int id, CancellationToken ct)
    {
        try
        {
            var price = (await client.GetPricesAsync([id], ct)).FirstOrDefault();

            var buy = price?.Buys?.UnitPrice ?? 0;
            var sell = price?.Sells?.UnitPrice ?? 0;

            if (buy == 0 && sell == 0)
                return (string.Empty, string.Empty, false, "Not traded on the trading post.", 0, 0);

            return (
                buy > 0 ? Coin.Format(buy) : "—",
                sell > 0 ? Coin.Format(sell) : "—",
                true,
                $"You would keep {Coin.Format(PriceRules.AfterFees(buy))} selling one instantly.",
                buy,
                sell);
        }
        catch (Gw2ApiException)
        {
            return (string.Empty, string.Empty, false, "Prices need a connection.", 0, 0);
        }
    }
}
