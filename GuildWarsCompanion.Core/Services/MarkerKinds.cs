namespace GuildWarsCompanion.Services;

/// <summary>
/// Deciding what a pack's category path means, out of the two things this app wants from a pack.
///
/// A pack's tree is whatever its author chose — <c>tw.chests.hot.chestone</c>,
/// <c>reactif.jp.dryTop</c>, <c>mypack.jumpingpuzzles.urmaug</c>. There is no standard, no
/// registry, and nothing in the file that says "this category is a chest" except the words the
/// author used. So this matches on the words, and it does it on the whole dotted path because the
/// meaningful part is rarely the last segment.
///
/// <b>Everything it cannot place is dropped, deliberately.</b> A pack carries hero points, vistas,
/// waypoints, guild bounties, festival markers and a great deal else; the first four the API
/// already publishes properly, and the rest is not what was asked for. Importing markers this app
/// has no opinion about would fill the dial with things it cannot label — and a target whose kind
/// is a guess is worse than no target.
/// </summary>
public static class MarkerKinds
{
    /// <summary>
    /// Words that mean a chest, longest first so the more specific reading wins.
    ///
    /// "chest" alone is the common case and the reason the exclusions below exist.
    /// </summary>
    private static readonly string[] Chests = ["chest"];

    /// <summary>Words that mean a jumping puzzle. <c>jp</c> is matched as a whole segment only —
    /// it is two letters and appears inside ordinary words.</summary>
    private static readonly string[] Puzzles = ["jumpingpuzzle", "jumping_puzzle", "jumppuzzle"];

    /// <summary>
    /// Categories that contain a wanted word and are not the thing.
    ///
    /// A pack's "chest" branch is usually accompanied by a "chestopener", a "chestsolution" or a
    /// trail category describing the way to one. These are not places to point at, and without
    /// this they arrive as chests sitting in the middle of a wall.
    /// </summary>
    private static readonly string[] Exclusions =
        ["trail", "path", "route", "solution", "opener", "guide", "marker_only", "hint"];

    /// <summary>
    /// What this app will call a marker, or null when it will not carry it at all.
    /// </summary>
    public static TargetKind? Of(string? type)
    {
        if (type is not { Length: > 0 }) return null;

        var path = type.ToLowerInvariant();

        if (path.StartsWith(TekkitRoot, StringComparison.Ordinal)) return Tekkit(path);

        foreach (var word in Exclusions)
            if (path.Contains(word, StringComparison.Ordinal))
                return null;

        foreach (var word in Puzzles)
            if (path.Contains(word, StringComparison.Ordinal))
                return TargetKind.JumpingPuzzle;

        // Whole segment, because "jp" is two letters and turns up inside words that are not it.
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            if (segment is "jp" or "jps")
                return TargetKind.JumpingPuzzle;

        foreach (var word in Chests)
            if (path.Contains(word, StringComparison.Ordinal))
                return TargetKind.Chest;

        return null;
    }

    private const string TekkitRoot = "tw_guides.";

    /// <summary>
    /// Tekkit's All-In-One, read by its own layout, because the words above get it wrong twice.
    ///
    /// Every path starts <c>tw_guides</c>, and "guide" is an exclusion, so the whole pack — the best
    /// chest and jumping puzzle coverage there is — imported as nothing at all (2026-10-06, 1837
    /// chests and 63 puzzles in its 56,000 markers). And it names things in the last segment: a
    /// chest run is <c>…_jadetechchests</c> holding <c>…_jadetechchest</c> beside <c>…_startsign</c>,
    /// <c>…_finish</c> and <c>…_toggletrail</c>, which the path-wide "chest" would all call chests.
    ///
    /// So: inside a jumping puzzle (a segment word <c>jp</c>, as in <c>tw_jp_kryta_trollsrevenge</c>)
    /// only the <c>startsign</c> is the puzzle — where it begins, which is where to go; its
    /// <c>trail_jump</c> steps and <c>trail_chest</c> hints are the way through. Elsewhere a chest
    /// is a last word with "chest" in it, unless the leaf is a trail. Puzzles whose path never says
    /// <c>jp</c> (Abaddon's Ascent under <c>tw_lws3</c>) are missed rather than guessed at.
    /// </summary>
    private static TargetKind? Tekkit(string path)
    {
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var leaf = segments[^1].Split('_', StringSplitOptions.RemoveEmptyEntries);

        if (leaf.Length == 0) return null;

        if (segments.Any(segment => segment.Split('_').Contains("jp")))
            return leaf[^1] == "startsign" ? TargetKind.JumpingPuzzle : null;

        return leaf[^1].Contains("chest", StringComparison.Ordinal) && !leaf.Any(word => word.Contains("trail"))
            ? TargetKind.Chest
            : null;
    }
}
