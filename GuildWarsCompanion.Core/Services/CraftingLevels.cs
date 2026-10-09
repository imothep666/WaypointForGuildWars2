namespace GuildWarsCompanion.Services;

/// <summary>
/// The rules that decide whether crafting something still teaches you anything.
///
/// Two pieces of game knowledge live here, both from the wiki rather than the API, because
/// the API describes recipes and ratings but never the relationship between them:
///
/// 1. Disciplines advance in tiers, each gated behind a tier-up cost.
/// 2. A recipe stops granting experience once your rating is far enough above its
///    requirement — the "grey recipe" everyone recognises. How far depends on what is being
///    made, which the recipe's own type tells us.
///
/// Both are stable rules of the game rather than opinions about what is good, which is the
/// same bar the build check holds itself to.
/// </summary>
public static class CraftingLevels
{
    /// <summary>Where each tier begins. 400 is the base cap; 500 needs an expansion.</summary>
    private static readonly int[] Tiers = [0, 75, 150, 225, 300, 400, 500];

    /// <summary>The highest rating any discipline reaches.</summary>
    public const int Max = 500;

    /// <summary>
    /// How far above a recipe's requirement you can be and still learn from making it.
    ///
    /// Refinements and components go grey quickly — they are cheap and repeatable, and the
    /// game does not want them carrying you to 500. Equipment and food stay useful longer,
    /// and exotic-tier recipes almost never go grey.
    /// </summary>
    public static int SpanFor(string? recipeType) => recipeType switch
    {
        "Refinement" or "Component" => 25,
        "LegendaryComponent" => 250,
        _ => 40
    };

    /// <summary>
    /// Whether making this still grants crafting experience.
    ///
    /// Below the requirement you cannot make it at all; too far above and it is grey. Note
    /// the boundary is exclusive: at exactly span levels above, the recipe has gone grey.
    /// </summary>
    public static bool StillTeaches(int rating, int minRating, string? recipeType) =>
        rating >= minRating && rating - minRating < SpanFor(recipeType);

    /// <summary>The next tier boundary above the current rating, or null at the cap.</summary>
    public static int? NextTier(int rating) =>
        Tiers.FirstOrDefault(tier => tier > rating) is var next && next > rating ? next : null;

    /// <summary>"38 to go" toward the next tier, or null when there is nothing left to reach.</summary>
    public static int? ToNextTier(int rating) =>
        NextTier(rating) is { } next ? next - rating : null;

    /// <summary>
    /// How a rating reads on screen: the number, and what it is short of.
    /// </summary>
    public static string Describe(int rating) => ToNextTier(rating) switch
    {
        null => $"{rating} — at the cap",
        var toGo => $"{rating} · {toGo} to the next tier"
    };
}
