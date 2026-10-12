using System.Text;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Turning a pack's dotted category path into something a person can choose from.
///
/// A trail is filed under <c>tt.mc.mt.th.t</c>, which says nothing to anybody. The pack's own
/// <c>MarkerCategory</c> elements carry a <c>DisplayName</c> per segment — <c>tt</c> is "Teh's
/// Trails - Map Completion", <c>mc</c> is "Map Completion", <c>mt</c> is "Main Trail" — so the
/// path can be written out in the author's own words.
///
/// <b>The names come from a stranger's file and are treated as such.</b> Authors put decoration in
/// them: Teh's Trails has a category displayed as <c>     |=====Core Tyria=====|     </c>, marked
/// as a separator. Separators are dropped from a path rather than shown, padding is trimmed, and a
/// segment with no name at all falls back to the segment itself so the row is never blank.
/// </summary>
public static class RouteNames
{
    /// <summary>
    /// A lookup from path segment to display name.
    ///
    /// Flat rather than a tree, because a type path already carries its own ancestry. Where two
    /// branches use the same segment name — packs do this, <c>t</c> under several parents — the
    /// first wins, which is at least stable: a second meaning for the same two letters is not
    /// something this can resolve without the tree, and the wrong one of two synonyms is a much
    /// smaller error than a blank row.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Lookup(IEnumerable<PackCategory> categories)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in categories)
        {
            if (category.IsSeparator) continue;

            if (category.DisplayName.Trim() is not { Length: > 0 } shown) continue;

            names.TryAdd(category.Name, shown);
        }

        return names;
    }

    /// <summary>The version this file's first line carries, so a later format can refuse an
    /// older file rather than misread it.</summary>
    public const string Version = "names 1";

    /// <summary>
    /// The names a pack gave its categories, kept beside its routes.
    ///
    /// Stored rather than re-derived because the pack itself is deleted once read — see
    /// <see cref="MarkerStore"/> — and without these every route on the filter list would read
    /// <c>tt.mc.mt.th.t</c>. Separators and unnamed categories are dropped at
    /// <see cref="Lookup"/>, so what is written is only what a screen can use.
    /// </summary>
    public static string Write(IEnumerable<PackCategory> categories)
    {
        var text = new StringBuilder().AppendLine(Version);

        foreach (var (name, shown) in Lookup(categories))
            text.Append(Clean(name)).Append('\t').AppendLine(Clean(shown));

        return text.ToString();
    }

    public static IReadOnlyDictionary<string, string> Read(string text)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var lines = new StringReader(text);

        if (lines.ReadLine() is not { } header || header.Trim() != Version) return names;

        while (lines.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;

            var parts = line.Split('\t');

            if (parts.Length < 2 || parts[0].Length == 0) continue;

            names.TryAdd(parts[0], parts[1]);
        }

        return names;
    }

    private static string Clean(string value) =>
        value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>
    /// Names for a set of category paths, lengthened only where two of them would read the same.
    ///
    /// <b>Found on the phone.</b> Shortening every path to its last two segments put two rows on
    /// one map reading "Main Trail · Normal Trail (Disable if using trail highlighting)", with 16
    /// runs and 12 — two different branches of a pack that happen to end the same way. A chooser
    /// with two identical rows is a chooser that cannot be used: whichever is tapped, the other
    /// is the one that was wanted half the time.
    ///
    /// So the short name is the default and the long one is the exception, applied only to the
    /// paths that clash and only as deep as it takes to tell them apart. Paths that are genuinely
    /// the same category keep one row between them, which is what the caller's grouping expects.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Distinguish(
        IEnumerable<string> types, IReadOnlyDictionary<string, string> names, int deepest = 8)
    {
        var said = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var type in types) said[type] = Describe(type, names);

        for (var deep = 3; deep <= deepest; deep++)
        {
            var clashing = said
                .GroupBy(entry => entry.Value, StringComparer.CurrentCultureIgnoreCase)
                .Where(group => group.Count() > 1)
                .SelectMany(group => group.Select(entry => entry.Key))
                .ToList();

            if (clashing.Count == 0) break;

            foreach (var type in clashing) said[type] = Describe(type, names, deep);
        }

        return said;
    }

    /// <summary>
    /// The end of the path in words: "Hearts · Heart Trails".
    ///
    /// <b>The tail, not the whole thing, and that was decided on the phone.</b> Written out in
    /// full, every row of Teh's Trails began "Teh's Trails - Map Completion · Map Completion ·",
    /// so six categories differed only in their last thirty characters — which is where the
    /// truncation fell. The last two segments are what actually tell them apart; the branch
    /// above them is the same for everything on the list precisely because a pack groups by it.
    ///
    /// Segments that resolve to nothing keep their raw name, so an unnamed branch reads oddly
    /// rather than disappearing — an author's omission should look like one.
    /// </summary>
    /// <param name="deep">How many trailing segments to say.</param>
    public static string Describe(
        string type, IReadOnlyDictionary<string, string> names, int deep = 2)
    {
        if (string.IsNullOrWhiteSpace(type)) return L.T("Unfiled route");

        var segments = type.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0) return L.T("Unfiled route");

        var said = new StringBuilder();

        foreach (var segment in segments.Skip(Math.Max(0, segments.Length - deep)))
        {
            var word = names.TryGetValue(segment, out var shown) ? shown : segment;

            if (said.Length > 0) said.Append(" · ");

            said.Append(word);
        }

        return said.ToString();
    }
}
