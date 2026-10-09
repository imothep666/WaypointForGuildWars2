using System.Text.RegularExpressions;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Cleans the prose the API hands back.
///
/// Some backstory journal entries arrive with raw markup in them — literal &lt;br&gt; tags
/// and HTML entities — because the game's own client renders them as markup. Anything that
/// puts API text on screen has to strip that, or the player reads the tags.
/// </summary>
public static partial class Prose
{
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Line breaks first, so the paragraph structure survives tag removal.
        var cleaned = LineBreaks().Replace(text, "\n");
        cleaned = Tags().Replace(cleaned, string.Empty);

        cleaned = cleaned
            .Replace("&nbsp;", " ")
            .Replace("&amp;", "&")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">");

        // The tags often bookend the text, leaving it starting on a blank line.
        cleaned = Blanks().Replace(cleaned, "\n\n");

        return cleaned.Trim();
    }

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreaks();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex Blanks();
}
