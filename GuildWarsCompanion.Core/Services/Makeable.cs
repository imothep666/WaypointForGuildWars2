namespace GuildWarsCompanion.Services;

/// <summary>
/// One recipe the account holds every ingredient for, as the catalogue reports it.
///
/// <paramref name="Disciplines"/> arrives as the pipe-delimited string the cache stores, and
/// <paramref name="Batches"/> is how many times the materials cover the recipe.
/// </summary>
public sealed record MakeableRow(
    int RecipeId, int OutputItemId, int OutputCount, int MinRating,
    string Disciplines, int Batches);

/// <summary>What can be made, and by whom.</summary>
public sealed record MakeablePlan(
    int OutputItemId, string Discipline, string Character, int Batches, int OutputCount,
    int MinRating);

/// <summary>
/// Reading "what can I make right now" out of the catalogue and the account's materials.
///
/// The API answers neither half of this — it has no item search and no notion of what you are
/// holding — so both halves are already local: the recipe catalogue on disk, and the storage
/// scan that counts every stack across bags, bank and material storage. Nothing here costs a
/// request; it is a join nobody had written.
///
/// The discipline check is the part that has to be right. A recipe within reach of the
/// materials is useless if no character can actually make it, and a rating that is high enough
/// on one character says nothing about another.
/// </summary>
public static class Makeable
{
    /// <summary>
    /// Pairs each makeable recipe with the character best placed to make it.
    ///
    /// "Best placed" is the lowest rating that still qualifies: crafting a recipe well below a
    /// character's rating earns them nothing, so the one who has least to lose does it — and
    /// on an account where two characters share a discipline, that is the one still levelling.
    /// </summary>
    public static IReadOnlyList<MakeablePlan> Plan(
        IEnumerable<MakeableRow> rows,
        IEnumerable<(string Discipline, string Character, int Rating)> disciplines)
    {
        var known = disciplines.ToList();
        var plans = new List<MakeablePlan>();

        foreach (var row in rows)
        {
            if (row.Batches < 1) continue;

            var maker = known
                .Where(d => Requires(row, d.Discipline) && d.Rating >= row.MinRating)
                .OrderBy(d => d.Rating)
                .FirstOrDefault();

            if (maker.Character is null) continue;

            plans.Add(new MakeablePlan(
                row.OutputItemId, maker.Discipline, maker.Character,
                row.Batches, row.OutputCount, row.MinRating));
        }

        return plans
            // The ones there are most materials for lead: those are the stacks worth clearing.
            .OrderByDescending(plan => plan.Batches)
            .ThenBy(plan => plan.Discipline, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    /// Whether a recipe wants this discipline.
    ///
    /// The cache stores them pipe-delimited — "|Chef|Artificer|" — so the delimiters are part
    /// of the match: a bare Contains would find "Chef" inside nothing else today, but it would
    /// find "Armorsmith" inside "Leatherworker,Armorsmith" the moment a name becomes a
    /// substring of another.
    /// </summary>
    public static bool Requires(MakeableRow row, string discipline) =>
        (row.Disciplines ?? string.Empty).Contains($"|{discipline}|", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// "Chef · Sweet Child O Night · 12 batches", or the singular where it matters. The
    /// character is named because the recipe cannot be made without logging into them.
    /// </summary>
    public static string Describe(MakeablePlan plan)
    {
        var batches = plan.Batches == 1
            ? L.T("once")
            : $"{plan.Batches:N0}×";

        return $"{CraftingLevels.DisciplineName(plan.Discipline)} · {plan.Character} · {batches}";
    }

    /// <summary>
    /// The heading: how much is actually on the list, and the limit of what it knows.
    /// </summary>
    public static string Summarise(int count) => count switch
    {
        0 => L.T("Nothing can be made outright from what you are holding."),
        1 => L.T("One recipe can be made from what you are holding."),
        _ => L.F("{0:N0} recipes can be made from what you are holding.", count)
    };
}
