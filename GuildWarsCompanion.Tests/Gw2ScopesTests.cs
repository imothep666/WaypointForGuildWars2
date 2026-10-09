using GuildWarsCompanion.Gw2;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// What the app asks a key for, against what the key carries.
///
/// The case this exists for is the quiet one: <c>/v2/characters/{name}/heropoints</c> answers a
/// key without <c>progression</c> with 200 and an empty array, so nothing anywhere reports an
/// error and the compass simply shows every hero point as unfinished. Holding the two lists
/// against each other is the only way to see it.
/// </summary>
public class Gw2ScopesTests
{
    private static readonly string[] Everything =
        ["account", "inventories", "characters", "tradingpost", "wallet", "unlocks", "progression"];

    [Fact]
    public void A_full_key_is_missing_nothing()
    {
        Assert.All(Gw2Scopes.Compare(Everything), status => Assert.True(status.Granted));
        Assert.Equal("Your key has everything the app asks for.", Gw2Scopes.Summary(Everything));
    }

    [Fact]
    public void Every_needed_scope_is_reported_on()
    {
        Assert.Equal(Gw2Scopes.Used.Count, Gw2Scopes.Compare([]).Count);
        Assert.All(Gw2Scopes.Compare([]), status => Assert.False(status.Granted));
    }

    /// <summary>
    /// The API returns its scopes in lower case and the key form shows them in lower case, but a
    /// comparison that only worked for one casing would report a perfectly good key as broken.
    /// </summary>
    [Fact]
    public void Casing_does_not_decide_whether_a_scope_counts()
    {
        Assert.All(
            Gw2Scopes.Compare(["Account", "INVENTORIES", "Characters", "TradingPost",
                               "WALLET", "Unlocks", "Progression"]),
            status => Assert.True(status.Granted));
    }

    /// <summary>The list exists to be acted on, so what has to be acted on comes first.</summary>
    [Fact]
    public void Missing_scopes_are_listed_before_granted_ones()
    {
        var rows = Gw2Scopes.Compare(["account", "wallet"]).ToList();

        var lastMissing = rows.FindLastIndex(status => !status.Granted);
        var firstGranted = rows.FindIndex(status => status.Granted);

        Assert.True(lastMissing < firstGranted);
    }

    [Fact]
    public void The_summary_names_what_is_missing_rather_than_counting_it()
    {
        var summary = Gw2Scopes.Summary(Everything.Where(p => p != "progression"));

        Assert.Contains("progression", summary);
        Assert.DoesNotContain("wallet", summary);
    }

    /// <summary>
    /// Two missing scopes read as a sentence, not as a comma-separated dump. Three or more keep
    /// the commas but still end with "and", which is where a naive join gets it wrong.
    /// </summary>
    [Fact]
    public void Several_missing_scopes_are_joined_as_words()
    {
        Assert.Contains(
            "wallet and progression",
            Gw2Scopes.Summary(Everything.Where(p => p is not ("wallet" or "progression"))));

        // In the order the key form lists its tick boxes, not the order they went missing: the
        // sort is stable, so the summary can be read straight down beside account.arena.net.
        Assert.Contains(
            "wallet, unlocks and progression",
            Gw2Scopes.Summary(
                Everything.Where(p => p is not ("wallet" or "progression" or "unlocks"))));
    }

    [Fact]
    public void A_key_with_no_permissions_at_all_is_handled()
    {
        Assert.Equal(Gw2Scopes.Used.Count, Gw2Scopes.Compare(null).Count);
        Assert.Empty(Gw2Scopes.Spare(null));
        Assert.Contains("missing", Gw2Scopes.Summary(null));
    }

    [Fact]
    public void Scopes_the_app_never_calls_are_named_as_spare()
    {
        var spare = Gw2Scopes.Spare([.. Everything, "pvp", "guilds"]);

        Assert.Equal(["guilds", "pvp"], spare);
    }

    /// <summary>
    /// Every scope listed has to be one the API actually publishes, or the screen tells somebody
    /// to tick a box that is not on the form.
    /// </summary>
    [Fact]
    public void Every_listed_scope_is_a_real_one()
    {
        var known = typeof(Gw2Permissions)
            .GetFields()
            .Select(field => field.GetValue(null) as string)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var scope in Gw2Scopes.Used)
        {
            Assert.Contains(scope.Permission, known);
            Assert.False(string.IsNullOrWhiteSpace(scope.Powers));
        }
    }
}
