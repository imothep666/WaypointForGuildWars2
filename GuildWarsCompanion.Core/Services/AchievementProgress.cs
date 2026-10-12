namespace GuildWarsCompanion.Services;

/// <summary>One achievement's standing, stripped to what ranking needs.</summary>
public sealed record Tracked(int Id, int Current, int Max, bool Done)
{
    public int Remaining => Math.Max(0, Max - Current);

    public double Fraction => Max <= 0 ? 0 : Math.Clamp((double)Current / Max, 0, 1);
}

/// <summary>
/// Picking the achievements worth showing out of the thousands an account has touched.
///
/// The whole value of the screen is the ordering. "You have 14,000 achievement points" is a
/// number; "two more kills finishes this one" is something you can act on this evening.
/// </summary>
public static class AchievementProgress
{
    /// <summary>
    /// The nearest to finishing, closest first.
    ///
    /// Excluded: anything already done, anything with no measurable target, and anything not
    /// started. That last one matters — most of the eight thousand achievements sit at zero,
    /// and "closest to completion" cannot mean an untouched achievement that happens to need
    /// only one of something.
    /// </summary>
    public static IReadOnlyList<Tracked> Closest(IEnumerable<Tracked> tracked, int take) =>
        tracked
            .Where(a => !a.Done && a.Max > 0 && a.Current > 0 && a.Current < a.Max)
            .OrderByDescending(a => a.Fraction)
            // A tie on proportion goes to the one with less left in absolute terms: two kills
            // is an evening, two hundred is a project.
            .ThenBy(a => a.Remaining)
            .ThenBy(a => a.Id)
            .Take(take)
            .ToList();

    /// <summary>"8 of 10" — the raw counts, because a percentage hides how much work is left.</summary>
    public static string ProgressText(Tracked achievement) =>
        L.F("{0:N0} of {1:N0}", achievement.Current, achievement.Max);

    /// <summary>"2 to go", or the plural, which is most of the time.</summary>
    public static string RemainingText(Tracked achievement) =>
        achievement.Remaining == 1 ? L.T("1 to go") : L.F("{0:N0} to go", achievement.Remaining);

    /// <summary>
    /// The requirement with this tier's target spliced into it.
    ///
    /// The API leaves a gap where the number belongs — "Kill  centaurs.", "Collect all  items
    /// and skins" — because one string serves every tier of the achievement. Printed as it
    /// arrives it reads like a typo; with the number in it, it is the instruction the screen
    /// was missing.
    /// </summary>
    public static string? Requirement(string? requirement, int target)
    {
        if (string.IsNullOrWhiteSpace(requirement)) return null;

        const string gap = "  ";

        if (!requirement.Contains(gap)) return requirement.Trim();

        // No target to splice: close the gap rather than leaving the tell-tale double space.
        var replacement = target > 0 ? $" {target:N0} " : " ";
        var index = requirement.IndexOf(gap, StringComparison.Ordinal);

        return (requirement[..index] + replacement + requirement[(index + gap.Length)..]).Trim();
    }

    /// <summary>
    /// What finishing the current tier pays.
    ///
    /// The account reports the tier's target as <c>max</c>, so the matching tier is the one
    /// being worked on. A tier list that does not contain it — which happens when ArenaNet
    /// retunes an achievement — yields nothing rather than a guess.
    /// </summary>
    public static int? PointsForTier(IEnumerable<(int Count, int Points)>? tiers, int max) =>
        tiers?.Where(tier => tier.Count == max)
            .Select(tier => (int?)tier.Points)
            .FirstOrDefault();
}
