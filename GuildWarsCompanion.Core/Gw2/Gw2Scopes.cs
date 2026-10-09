namespace GuildWarsCompanion.Gw2;

/// <summary>One permission this app asks a key for, and what goes dark without it.</summary>
/// <param name="Permission">The name exactly as account.arena.net spells it on the key form and
/// as /v2/tokeninfo returns it. Shown to the user unaltered for that reason — a tidier label
/// would be one more thing to match against the tick box by eye.</param>
/// <param name="Powers">The screens that stop working, named as the app names them.</param>
public sealed record Gw2Scope(string Permission, string Powers);

/// <summary>A scope beside the answer /v2/tokeninfo gave about it.</summary>
public sealed record ScopeStatus(Gw2Scope Scope, bool Granted);

/// <summary>
/// What the app needs from a key, next to what the key actually carries.
///
/// Exists because the failure it describes is silent. A key missing a scope does not always
/// produce a 403 that anyone sees: <c>/v2/characters/{name}/heropoints</c> answers 200 with an
/// empty list, which reads as "this character has finished nothing" and sends somebody looking
/// for a bug in the screen that drew it. The only way to tell the two apart is to hold the
/// granted list against the needed one, which is what this is for.
///
/// Pure, and in its own file, so the comparison can be tested without a key or a network.
/// </summary>
public static class Gw2Scopes
{
    /// <summary>
    /// Every scope the app calls an endpoint for, in the order the key form on
    /// account.arena.net lists its tick boxes — so the screen can be read straight down beside
    /// the browser rather than hunted through.
    /// </summary>
    public static IReadOnlyList<Gw2Scope> Used { get; } =
    [
        new(Gw2Permissions.Account, "Account, and the name shown above"),
        new(Gw2Permissions.Inventories, "Materials and bank"),
        new(Gw2Permissions.Characters, "Characters, Crafting, and the compass's finished hero points"),
        new(Gw2Permissions.TradingPost, "Trading Post orders and deliveries"),
        new(Gw2Permissions.Wallet, "Wallet"),
        new(Gw2Permissions.Unlocks, "Home instance, dyes and skins"),
        new(Gw2Permissions.Progression, "Today, Achievements, Masteries, world bosses, and the compass's finished hero points")
    ];

    /// <summary>
    /// Each needed scope with whether the key has it.
    ///
    /// Case-insensitive against the API's own spelling. Missing first: the list exists to be
    /// acted on, and what has to be acted on should not be at the bottom of seven rows.
    /// </summary>
    public static IReadOnlyList<ScopeStatus> Compare(IEnumerable<string>? granted)
    {
        var has = new HashSet<string>(granted ?? [], StringComparer.OrdinalIgnoreCase);

        return Used
            .Select(scope => new ScopeStatus(scope, has.Contains(scope.Permission)))
            .OrderBy(status => status.Granted)
            .ToList();
    }

    /// <summary>
    /// Scopes the key carries that this app never calls.
    ///
    /// Worth naming rather than hiding. Somebody comparing two lists that do not match wants to
    /// know which side the difference is on, and "the app does not use it" is a complete answer
    /// where silence is not.
    /// </summary>
    public static IReadOnlyList<string> Spare(IEnumerable<string>? granted)
    {
        var needed = new HashSet<string>(
            Used.Select(scope => scope.Permission), StringComparer.OrdinalIgnoreCase);

        return (granted ?? [])
            .Where(permission => !needed.Contains(permission))
            .OrderBy(permission => permission, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The one line above the list: whether there is anything to do, and what.
    ///
    /// Names the missing scopes rather than counting them. "2 missing" sends somebody back
    /// through the list to find out which, and the list is right there either way.
    /// </summary>
    public static string Summary(IEnumerable<string>? granted)
    {
        var missing = Compare(granted)
            .Where(status => !status.Granted)
            .Select(status => status.Scope.Permission)
            .ToList();

        return missing.Count == 0
            ? "Your key has everything the app asks for."
            : $"Your key is missing {Join(missing)}. Tick it on account.arena.net, make a new " +
              "key, and scan it again — a key's permissions cannot be changed after it is made.";
    }

    /// <summary>"wallet", "wallet and unlocks", "wallet, unlocks and progression".</summary>
    private static string Join(IReadOnlyList<string> names) =>
        names.Count switch
        {
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}"
        };
}
