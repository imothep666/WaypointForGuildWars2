using System.Globalization;
using System.Resources;

namespace GuildWarsCompanion.Localization;

/// <summary>
/// The app's own words in the phone's language.
///
/// Keyed by the English text itself, gettext-style, rather than by invented names: the code still
/// reads as what the screen says, and a sentence nobody has translated yet simply stays English
/// instead of showing a key. The translations are <c>Strings.de.resx</c>, one entry per English
/// sentence; Rider's resource editor shows them side by side.
///
/// Looked up without regard to case, because a resx cannot hold "TODAY" beside "Today": the build
/// drops the second as a duplicate, with only a warning. A heading in capitals is the same words
/// as its tab label, so it is filed once and comes back in capitals; see <see cref="T"/>.
///
/// What the API writes is not here. It arrives in the phone's language already; see
/// <see cref="Gw2.Gw2Language"/>. This is only what the app says around it.
/// </summary>
public static class L
{
    private static readonly ResourceManager Strings =
        new("GuildWarsCompanion.Localization.Strings", typeof(L).Assembly) { IgnoreCase = true };

    /// <summary>
    /// The text in the phone's language, or as written when there is no translation. English in
    /// capitals, a heading, comes back in capitals.
    /// </summary>
    public static string T(string english) => Look(english, english);

    /// <summary>
    /// A sentence with values in it: <c>L.F("{0} days left", days)</c>. The translation keeps the
    /// placeholders and may move them, since German does not always put the number where English
    /// does. The values are formatted in the phone's culture, so 27,997 reads 27.997 in German.
    /// </summary>
    public static string F(string english, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, T(english), values);

    /// <summary>
    /// The same English that needs different German in different places: "on {0}" is "bei
    /// Imothep" for a character's bags and "in Königintal" for a map. The context names the
    /// place, and the translation is filed as "context|english".
    /// </summary>
    public static string TIn(string context, string english) => Look($"{context}|{english}", english);

    /// <inheritdoc cref="TIn"/>
    public static string FIn(string context, string english, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, TIn(context, english), values);

    private static string Look(string key, string english)
    {
        if (Strings.GetString(key, CultureInfo.CurrentUICulture) is not { } found) return english;

        return IsShouted(english) ? found.ToUpper(CultureInfo.CurrentUICulture) : found;
    }

    /// <summary>Letters, and none of them lower case: "TODAY", "PRICE WATCH".</summary>
    private static bool IsShouted(string text) =>
        text.Any(char.IsLetter) && !text.Any(char.IsLower);
}
