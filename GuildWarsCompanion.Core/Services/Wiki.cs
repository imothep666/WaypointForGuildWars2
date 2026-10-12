using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Sending a player to the wiki, which is where the answers the API cannot give actually
/// live — where a material drops, what a recipe needs, whether a skin is worth chasing.
/// </summary>
public static class Wiki
{
    /// <summary>
    /// The search entry point rather than a direct article link.
    ///
    /// `/wiki/Glob_of_Ectoplasm` works right up until a name has punctuation, a disambiguation
    /// page, or simply is not an article — and then it is a 404 in the player's browser.
    /// `index.php?search=` redirects to the article when the name matches exactly, and falls
    /// back to search results when it does not, so a tap is never a dead end.
    /// </summary>
    /// <summary>
    /// What to search for when the name carries a place in front of it.
    ///
    /// Collection entries for map exploration read "Sparkfly Fen: Toade's Head", and the wiki
    /// indexes the area under its own name alone — searching the whole string lands on
    /// "Explorer" instead, which is nobody's answer. Checked against the wiki's search API.
    ///
    /// The separator is a colon followed by a space, so a name that merely ends in a colon —
    /// trailing whitespace having already gone — keeps its whole self.
    /// </summary>
    public static string SearchTerm(string? name)
    {
        var whole = (name ?? string.Empty).Trim();
        var mark = whole.LastIndexOf(": ", StringComparison.Ordinal);

        return mark < 0 ? whole : whole[(mark + 2)..].Trim();
    }

    /// <param name="language">
    /// The language the name is in, as <see cref="Gw2Client.Language"/>. German, French and
    /// Spanish each have a wiki of their own, which is where a name in those languages finds its
    /// page; checked by following the search redirect on each. There is no Chinese one.
    /// </param>
    public static string UrlFor(string? itemName, string language = Gw2Language.English)
    {
        var term = (itemName ?? string.Empty).Trim();
        var root = HasOwnWiki(language)
            ? $"https://wiki-{language}.guildwars2.com/"
            : "https://wiki.guildwars2.com/";

        return term.Length == 0
            ? root
            : $"{root}index.php?search={Uri.EscapeDataString(term)}";
    }

    /// <summary>Whether a language has a wiki of its own, rather than only the English one.</summary>
    public static bool HasOwnWiki(string language) => language is "de" or "fr" or "es";
}

/// <summary>
/// Turning the API's item flags into the two or three facts a player actually acts on.
///
/// The flags are engine vocabulary — "SoulBindOnUse", "NoSell" — and there are dozens. Most
/// say nothing useful. These are the ones that change what you can do with the thing.
/// </summary>
public static class ItemFacts
{
    private static readonly Dictionary<string, string> Meaningful = new()
    {
        ["AccountBound"] = "Account bound",
        ["AccountBindOnUse"] = "Account bound on use",
        ["SoulbindOnAcquire"] = "Soulbound",
        ["SoulBindOnUse"] = "Soulbound on use",
        ["NoSell"] = "Cannot be sold",
        ["NoSalvage"] = "Cannot be salvaged",
        ["Unique"] = "Unique",
        ["NotUpgradeable"] = "No upgrade slot"
    };

    /// <summary>
    /// The flags worth reading, in the order listed above so the binding rules come first —
    /// they are what decides whether an item can be traded at all.
    /// </summary>
    public static IReadOnlyList<string> Describe(IEnumerable<string>? flags)
    {
        if (flags is null) return [];

        var set = flags.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Meaningful
            .Where(pair => set.Contains(pair.Key))
            .Select(pair => L.T(pair.Value))
            .ToList();
    }

    /// <summary>
    /// "CraftingMaterial" reads as "Crafting Material".
    ///
    /// Item types come back in PascalCase, unlike the snake_case ids everywhere else in the
    /// API, so <see cref="Ids.ToDisplayName"/> leaves them run together — it splits on
    /// underscores and there are none.
    ///
    /// English, since it only respaces the API's word; a screen puts it through <see cref="L.T"/>,
    /// where the types the API uses have their translations.
    /// </summary>
    public static string ReadableType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return string.Empty;

        var words = new System.Text.StringBuilder(type.Length + 4);

        for (var i = 0; i < type.Length; i++)
        {
            // A capital after a lower-case letter starts a new word. Every item type the
            // API uses is plain PascalCase — CraftingMaterial, UpgradeComponent, MiniPet,
            // HelmAquatic — so there is no acronym case to handle here.
            if (i > 0 && char.IsUpper(type[i]) && !char.IsUpper(type[i - 1]))
                words.Append(' ');

            words.Append(type[i]);
        }

        return words.ToString();
    }

    /// <summary>One line, or nothing at all when there is nothing worth saying.</summary>
    public static string? Summarise(IEnumerable<string>? flags)
    {
        var facts = Describe(flags);
        return facts.Count == 0 ? null : string.Join(" · ", facts);
    }
}
