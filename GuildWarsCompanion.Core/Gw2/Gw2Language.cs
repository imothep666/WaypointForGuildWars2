using System.Globalization;

namespace GuildWarsCompanion.Gw2;

/// <summary>
/// Which language the API is asked to answer in.
///
/// The device's, when the API speaks it: English, German, French, Spanish or Chinese. Anything
/// else is English, which is also what the API does by itself with a code it does not know —
/// asking for that explicitly keeps the cache's record of what it holds honest.
///
/// Only what the API writes changes. Its enums (rarity, type, flags), its ids and its error text
/// stay English whatever is asked for, so nothing that reads those has to care.
/// </summary>
public static class Gw2Language
{
    public const string English = "en";

    private static readonly string[] Spoken = [English, "de", "fr", "es", "zh"];

    /// <summary>The language for this device, read from its UI culture.</summary>
    public static string Current => For(CultureInfo.CurrentUICulture);

    public static string For(CultureInfo culture)
    {
        var language = culture.TwoLetterISOLanguageName.ToLowerInvariant();

        return Spoken.Contains(language) ? language : English;
    }
}
