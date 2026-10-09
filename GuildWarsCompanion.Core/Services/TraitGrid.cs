namespace GuildWarsCompanion.Services;

/// <summary>
/// Splitting a specialization into the shape the game draws.
///
/// A specialization is three tiers. Each tier grants one minor trait and offers three majors,
/// of which one is taken. The API hands them over as two flat lists — three minors and nine
/// majors, both in tier order — and the grid only means anything once they are paired back up.
///
/// The guard matters more than the arithmetic: half a grid, or a grid whose tiers have slipped
/// by one, would still draw perfectly well and would be showing the wrong traits.
/// </summary>
public static class TraitGrid
{
    public const int Tiers = 3;

    public const int MajorsPerTier = 3;

    /// <summary>
    /// Whether the two lists are the shape a specialization is supposed to be.
    ///
    /// Anything shorter is a specialization the API has changed out from under this build,
    /// and drawing part of one is worse than drawing none.
    /// </summary>
    public static bool IsComplete(IReadOnlyList<int>? minors, IReadOnlyList<int>? majors) =>
        minors is { Count: >= Tiers } && majors is { Count: >= Tiers * MajorsPerTier };

    /// <summary>
    /// The tiers, each with its minor and the three majors that compete for it.
    ///
    /// Extra entries are ignored rather than reshuffled: if a future specialization carries a
    /// fourth tier, three correct tiers beat four guessed ones.
    /// </summary>
    public static IReadOnlyList<(int Minor, IReadOnlyList<int> Majors)> Split(
        IReadOnlyList<int>? minors, IReadOnlyList<int>? majors)
    {
        if (!IsComplete(minors, majors)) return [];

        return Enumerable.Range(0, Tiers)
            .Select(tier => (
                minors![tier],
                (IReadOnlyList<int>)majors!
                    .Skip(tier * MajorsPerTier)
                    .Take(MajorsPerTier)
                    .ToList()))
            .ToList();
    }
}
