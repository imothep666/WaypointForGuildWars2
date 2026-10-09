namespace GuildWarsCompanion.Services;

/// <summary>
/// A recipe reduced to what the tree walk needs: how many one batch yields, and what it eats.
/// </summary>
public sealed record RecipeShape(int Output, IReadOnlyList<(int ItemId, int Count)> Ingredients);

/// <summary>How much of one thing the whole job still needs.</summary>
public sealed record CraftNeed(int ItemId, int Count);

/// <summary>
/// Expanding a recipe all the way down to the things you have to go and get.
///
/// Three rules make this match how crafting actually works, and all three are easy to get
/// subtly wrong:
///
/// 1. <b>What you already hold stops the descent.</b> Holding fifty Bronze Ingots means you do
///    not need five hundred Copper Ore. Only the shortfall is expanded.
/// 2. <b>Held materials are spent, not reused.</b> The same ore feeding two branches can only
///    be counted once, so the walk carries a budget and draws it down.
/// 3. <b>Batches round up.</b> A recipe yielding five when you need six means crafting twice,
///    and therefore buying for two batches.
/// </summary>
public static class CraftTree
{
    /// <summary>
    /// How deep to follow a recipe before giving up.
    ///
    /// Real chains run three or four levels — ore to ingot to plated ingot to a component.
    /// The cap is a guard against a cycle in the data rather than a real limit, and the caller
    /// is told when it bites.
    /// </summary>
    public const int MaxDepth = 6;

    /// <summary>
    /// The raw materials still needed to make <paramref name="quantity"/> of an item, after
    /// what the account already holds is taken into account.
    ///
    /// <paramref name="held"/> is consumed by the walk, so pass a copy if the caller needs it
    /// afterwards. An item with no recipe, or one past the depth cap, is a leaf: something to
    /// gather or buy.
    /// </summary>
    public static IReadOnlyList<CraftNeed> Materials(
        int itemId,
        int quantity,
        Func<int, RecipeShape?> recipeOf,
        Dictionary<int, int> held,
        out bool truncated)
    {
        var needs = new Dictionary<int, int>();
        var depthReached = false;

        Walk(itemId, quantity, recipeOf, held, needs, 0, ref depthReached);

        truncated = depthReached;

        return needs
            .Where(pair => pair.Value > 0)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Select(pair => new CraftNeed(pair.Key, pair.Value))
            .ToList();
    }

    private static void Walk(
        int itemId,
        int quantity,
        Func<int, RecipeShape?> recipeOf,
        Dictionary<int, int> held,
        Dictionary<int, int> needs,
        int depth,
        ref bool truncated)
    {
        if (quantity <= 0) return;

        // Spend what is already in the cupboard first, and remember that it is spent — the
        // same stack cannot satisfy two different branches.
        if (held.TryGetValue(itemId, out var have) && have > 0)
        {
            var used = Math.Min(have, quantity);
            held[itemId] = have - used;
            quantity -= used;

            if (quantity == 0) return;
        }

        var recipe = depth < MaxDepth ? recipeOf(itemId) : null;

        if (depth >= MaxDepth && recipeOf(itemId) is not null)
            truncated = true;

        // Nothing makes it, or we stopped descending: this is something to go and get.
        if (recipe is null || recipe.Ingredients.Count == 0)
        {
            needs[itemId] = needs.GetValueOrDefault(itemId) + quantity;
            return;
        }

        // A recipe yielding five when six are wanted has to be run twice.
        var perBatch = Math.Max(recipe.Output, 1);
        var batches = (quantity + perBatch - 1) / perBatch;

        foreach (var (ingredient, count) in recipe.Ingredients)
            Walk(ingredient, batches * count, recipeOf, held, needs, depth + 1, ref truncated);
    }
}
