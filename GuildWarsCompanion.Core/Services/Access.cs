namespace GuildWarsCompanion.Services;

/// <summary>One thing the account owns, and why it is on the list when it needs saying.</summary>
public sealed record Owned(string Name, string? Note, string Icon)
{
    public bool HasNote => Note is not null;
}

/// <summary>
/// What the account owns, from the `access` array on /v2/account.
///
/// Already arriving with every account read and thrown away until now — the same kind of
/// missing feature as fractal level was.
///
/// The one thing this must not do is drop a token it does not recognise. Expansions keep
/// coming, and an account that owns a new one should see it named badly rather than not at
/// all: a list that quietly omits what you bought is worse than one that spells it oddly.
/// </summary>
public static class Access
{
    /// <summary>
    /// The names as ArenaNet writes them, since splitting the token gives "Heart Of Thorns"
    /// and "Secrets Of The Obscure".
    /// </summary>
    private static readonly Dictionary<string, string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HeartOfThorns"] = "Heart of Thorns",
        ["PathOfFire"] = "Path of Fire",
        ["EndOfDragons"] = "End of Dragons",
        ["SecretsOfTheObscure"] = "Secrets of the Obscure",
        ["JanthirWilds"] = "Janthir Wilds",
        ["VisionsOfEternity"] = "Visions of Eternity"
    };

    /// <summary>
    /// An emblem for each expansion.
    ///
    /// The API has no expansion artwork — professions carry an icon, expansions do not — so
    /// these are the game's own emblems, taken from the Guild Wars 2 Wiki at 128px and
    /// shipped with the app, which also means they are there with no connection. An expansion
    /// released after this build has no file to ship, so it falls back to a plain mark cut for
    /// the app.
    /// </summary>
    private static readonly Dictionary<string, string> Emblems = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HeartOfThorns"] = "expansion_hot.png",
        ["PathOfFire"] = "expansion_pof.png",
        ["EndOfDragons"] = "expansion_eod.png",
        ["SecretsOfTheObscure"] = "expansion_soto.png",
        ["JanthirWilds"] = "expansion_jw.png",
        ["VisionsOfEternity"] = "expansion_voe.png"
    };

    /// <summary>
    /// The emblem for a token, or a plain mark for one this build has never heard of — the
    /// same rule as the names: shown imperfectly rather than not at all.
    /// </summary>
    public static string Emblem(string token) =>
        Emblems.GetValueOrDefault(token, "expansion_other.png");

    /// <summary>
    /// How the account holds the base game, which is not an expansion and does not belong in
    /// the same list.
    /// </summary>
    private static readonly HashSet<string> BaseGame =
        new(StringComparer.OrdinalIgnoreCase) { "None", "PlayForFree", "GuildWars2" };

    /// <summary>
    /// The expansions owned, in release order — which is the order the tokens arrive in, but
    /// sorting by name would scramble it.
    ///
    /// One flag is not the whole truth. From the API documentation: an account that received
    /// Heart of Thorns <em>by buying Path of Fire</em> never gets the `HeartOfThorns` flag,
    /// and `PathOfFire` "also implies that the account has access to Heart of Thorns". Listing
    /// only what the array says tells such an account it does not own an expansion it plainly
    /// does — which is exactly how this was found.
    /// </summary>
    public static IReadOnlyList<Owned> Expansions(IEnumerable<string>? access)
    {
        var tokens = (access ?? []).Where(token => !BaseGame.Contains(token)).ToList();

        var owned = new List<Owned>();

        foreach (var token in tokens)
        {
            // Slotted in front of Path of Fire, where its release belongs.
            if (token.Equals("PathOfFire", StringComparison.OrdinalIgnoreCase) &&
                !tokens.Contains("HeartOfThorns", StringComparer.OrdinalIgnoreCase))
                owned.Add(new Owned(
                    "Heart of Thorns", L.T("included with Path of Fire"), Emblem("HeartOfThorns")));

            owned.Add(new Owned(Name(token), null, Emblem(token)));
        }

        return owned;
    }

    /// <summary>
    /// A token's readable name. An unrecognised one is split on its capitals rather than
    /// dropped, so a new expansion shows up the day it ships.
    /// </summary>
    public static string Name(string token) =>
        Named.TryGetValue(token, out var known) ? known : ItemFacts.ReadableType(token);

    /// <summary>
    /// Whether the account is a free one. Worth saying plainly: a free account is barred from
    /// the trading post, which explains a screen that would otherwise look broken.
    ///
    /// `PlayForFree` alone is not the test — the documentation is explicit that the flag also
    /// appears on accounts that have bought the game or an expansion. It only means a free
    /// account "without any other flags".
    /// </summary>
    public static bool IsFree(IEnumerable<string>? access)
    {
        var tokens = (access ?? []).ToList();

        return tokens.Contains("PlayForFree", StringComparer.OrdinalIgnoreCase) &&
               tokens.All(token => token.Equals("PlayForFree", StringComparison.OrdinalIgnoreCase) ||
                                   token.Equals("None", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// One line for the base game, and one for the expansions — "None yet" rather than an
    /// empty space, which reads as something that failed to load.
    /// </summary>
    public static string Summarise(IEnumerable<string>? access)
    {
        var tokens = (access ?? []).ToList();
        var expansions = Expansions(tokens);

        var owned = expansions.Count switch
        {
            0 => L.T("No expansions"),
            1 => L.T("1 expansion"),
            _ => L.F("{0} expansions", expansions.Count)
        };

        return IsFree(tokens) ? L.F("Free account · {0}", owned) : $"Guild Wars 2 · {owned}";
    }
}
