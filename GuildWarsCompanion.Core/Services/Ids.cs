namespace GuildWarsCompanion.Services;

/// <summary>
/// Turns API ids into something readable.
///
/// Several daily-reset endpoints return bare ids — "charged_quartz_crystal",
/// "claw_of_jormag" — and the API has no display names for them anywhere. The id has to
/// become the label, so that conversion lives here where it can be tested on its own rather
/// than behind an HTTP client.
/// </summary>
public static class Ids
{
    /// <summary>Joining words stay lower case unless they lead, so an id reads as a title.</summary>
    private static readonly HashSet<string> Minor =
        new(StringComparer.OrdinalIgnoreCase) { "of", "the", "and", "in", "a", "to", "for" };

    /// <summary>
    /// "charged_quartz_crystal" reads as "Charged Quartz Crystal", "claw_of_jormag" as
    /// "Claw of Jormag". Apostrophes are unrecoverable — "heros_choice_chest" cannot be told
    /// from a plural — so those stay as the id spells them.
    /// </summary>
    public static string ToDisplayName(string id)
    {
        var words = id.Split('_', StringSplitOptions.RemoveEmptyEntries);

        return string.Join(' ', words.Select((word, index) =>
            index > 0 && Minor.Contains(word)
                ? word.ToLowerInvariant()
                : word.Length == 1
                    ? word.ToUpperInvariant()
                    : char.ToUpperInvariant(word[0]) + word[1..]));
    }
}
