namespace GuildWarsCompanion.Services;

/// <summary>How far a goal has come, counted in the raw materials it takes.</summary>
/// <param name="Total">Every raw material the goal takes from nothing, in units.</param>
/// <param name="Missing">What is still to gather or buy after what is held, in units.</param>
/// <param name="Short">What is missing, largest first.</param>
/// <param name="Truncated">Whether the chain was cut at <see cref="CraftTree.MaxDepth"/>.</param>
public sealed record GoalCount(int Total, int Missing, IReadOnlyList<CraftNeed> Short, bool Truncated)
{
    public int InHand => Total - Missing;

    public bool IsReady => Missing == 0;
}

/// <summary>What buying the shortfall costs, in words.</summary>
public static class GoalPrice
{
    /// <param name="ask">What sellers ask for one, in copper; zero when it is not on the market.</param>
    public static string Describe(IReadOnlyList<CraftNeed> needs, Func<int, long> ask)
    {
        long total = 0;

        foreach (var need in needs)
        {
            var each = ask(need.ItemId);
            if (each <= 0) return L.T("some of it cannot be bought");

            total += each * need.Count;
        }

        return L.F("about {0} to buy", Coin.Format(total));
    }
}

/// <summary>
/// A goal's progress, from the same walk as the item page's "all the way down".
///
/// Counted in units of raw material, which is a crude measure — a stack of 250 ore weighs as much
/// as 250 of anything — and an honest one: it is what the account can see. Weighting by price
/// would make every untradable component weightless, and those are exactly the parts of a long
/// goal that take longest.
/// </summary>
public static class GoalProgress
{
    public static GoalCount Of(
        int itemId, int quantity, Func<int, RecipeShape?> recipeOf, IReadOnlyDictionary<int, int> held)
    {
        // From nothing: what the whole thing takes.
        var all = CraftTree.Materials(itemId, quantity, recipeOf, new Dictionary<int, int>(), out _);

        // From what is held. The walk spends its budget, so it gets a copy.
        var budget = new Dictionary<int, int>(held);
        var missing = CraftTree.Materials(itemId, quantity, recipeOf, budget, out var truncated);

        var total = all.Sum(need => need.Count);

        // Held items only ever stop the descent, so missing cannot exceed the total; the clamp is
        // a guard against a recipe cycle in the data, not a case that is expected.
        return new GoalCount(total, Math.Min(missing.Sum(need => need.Count), total), missing, truncated);
    }
}
