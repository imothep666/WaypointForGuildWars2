namespace GuildWarsCompanion.Services;

/// <summary>
/// One line of a recipe measured against what the account holds.
/// <paramref name="Held"/> is null when storage has not been read, which is different from
/// holding none.
/// </summary>
public sealed record CraftLine(int ItemId, string Name, int Needed, int? Held)
{
    /// <summary>How many are still wanted for a single craft, or null while Held is unknown.</summary>
    public int? Short => Held is null ? null : Math.Max(0, Needed - Held.Value);

    public bool IsSatisfied => Held is not null && Held >= Needed;
}

/// <summary>
/// Turning a recipe and a cupboard into "can I make this".
///
/// The API knows what a recipe needs and, separately, what the account holds. It never joins
/// the two, and that join is the only thing a player actually wants to know while standing at
/// a crafting station. Kept pure so the arithmetic can be tested without an account.
/// </summary>
public static class CraftPlan
{
    /// <summary>
    /// How many batches the held materials allow, or null if any line's holding is unknown.
    ///
    /// The limit is whichever ingredient runs out first, so this is the smallest whole number
    /// of batches across every line — not an average, and never a partial batch.
    /// </summary>
    public static int? Batches(IEnumerable<CraftLine> lines)
    {
        var possible = int.MaxValue;

        foreach (var line in lines)
        {
            // A line needing nothing cannot limit anything, and must not divide by zero.
            if (line.Needed <= 0) continue;
            if (line.Held is null) return null;

            possible = Math.Min(possible, line.Held.Value / line.Needed);
        }

        return possible == int.MaxValue ? 0 : possible;
    }

    /// <summary>The lines standing between you and one craft, largest shortfall first.</summary>
    public static IReadOnlyList<CraftLine> Missing(IEnumerable<CraftLine> lines) =>
        lines
            .Where(line => line.Held is not null && line.Held < line.Needed)
            .OrderByDescending(line => line.Needed - line.Held!.Value)
            .ThenBy(line => line.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The sentence the screen leads with.
    ///
    /// <paramref name="output"/> is how many one batch yields, so a recipe that makes five at
    /// a time says so rather than counting batches at the player.
    /// </summary>
    public static string Verdict(IReadOnlyList<CraftLine> lines, int output, bool storageKnown)
    {
        if (!storageKnown)
            return "Open Find once to have the app count what you are holding.";

        var batches = Batches(lines);

        if (batches is null or 0)
        {
            var missing = Missing(lines);

            return missing.Count switch
            {
                0 => "You have none of what this needs.",
                1 => $"Short {missing[0].Short:N0} {missing[0].Name}.",
                _ => $"Short {missing.Count} of the {lines.Count} ingredients."
            };
        }

        var made = batches.Value * Math.Max(output, 1);

        return made == 1
            ? "You can make one right now."
            : $"You can make {made:N0} right now.";
    }
}
