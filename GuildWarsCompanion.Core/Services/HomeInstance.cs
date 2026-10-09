namespace GuildWarsCompanion.Services;

/// <summary>
/// One thing in the home instance, and whether it is there yet.
///
/// Deliberately free of anything that needs the UI framework, so the logic below is tested on
/// its own terms rather than through a screen. The colour lives on the row that wraps this.
/// </summary>
public sealed record HomeThing(string Name, string Detail, bool Unlocked)
{
    public bool HasDetail => Detail.Length > 0;
}

/// <summary>
/// The home instance: gathering nodes and cats.
///
/// Unusually cheap for what it gives. Both catalogues are small — 57 nodes, 35 cats — and the
/// ids are self-describing slugs, so nothing needs a name lookup at all. The account side is
/// one short array each.
///
/// The answer is deliberately two-sided. What is missing is a shopping list, since almost
/// every node is bought; what is present is a daily gathering round. A single count would
/// serve neither.
/// </summary>
public static class HomeInstance
{
    /// <summary>
    /// A node's name, with the trailing "node" dropped — under a heading that already says
    /// nodes, "Elder Wood Node" is a stutter — and numbered plots read as plain numbers
    /// rather than as "01".
    /// </summary>
    public static string NodeName(string id)
    {
        var words = id.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Where((word, index) => !(index > 0 && word.Equals("node", StringComparison.OrdinalIgnoreCase)))
            .Select(word => word.Length > 1 && word.All(char.IsDigit)
                ? int.Parse(word).ToString()
                : word);

        return Ids.ToDisplayName(string.Join('_', words));
    }

    /// <summary>
    /// What a cat wants. The API never names the cats — it gives only the hint, which is the
    /// food you leave out — so the hint is the whole answer rather than a clue to it.
    /// </summary>
    public static string CatLure(string? hint) =>
        string.IsNullOrWhiteSpace(hint) ? "Unknown" : Ids.ToDisplayName(hint);

    /// <summary>
    /// Everything in the catalogue, what is missing first, each side keeping the order the
    /// catalogue gave — which is alphabetical for nodes and release order for cats.
    /// </summary>
    public static IReadOnlyList<HomeThing> Checklist(
        IEnumerable<(string Id, string Name, string Detail)> all, IEnumerable<string>? unlocked)
    {
        var have = (unlocked ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return all
            .Select(thing => new HomeThing(thing.Name, thing.Detail, have.Contains(thing.Id)))
            .OrderBy(thing => thing.Unlocked)
            .ToList();
    }

    /// <summary>
    /// "45 of 57 nodes · 12 still to unlock", or the whole of it when there is nothing left.
    ///
    /// The remainder is the point of the line, so it is said as a number rather than left to
    /// be worked out from the other two.
    ///
    /// <paramref name="unlocked"/> is null when the account side could not be read — a key
    /// without the scopes, or a request that failed. That is not the same as owning none of
    /// them, and printing "0 of 57" for it would report an empty home instance to someone
    /// whose instance is full.
    /// </summary>
    public static string Summarise(int total, int? unlocked, string what)
    {
        if (total <= 0) return string.Empty;

        if (unlocked is not { } have) return $"{total:N0} {what} in the game";

        var missing = Math.Max(0, total - have);

        return missing == 0
            ? $"All {total:N0} {what}"
            : $"{have:N0} of {total:N0} {what} · {missing:N0} still to unlock";
    }
}
