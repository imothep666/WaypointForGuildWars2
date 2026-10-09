using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// What the account owns, read off the access list.
///
/// The failure to guard against is omission: expansions keep coming, and a list that quietly
/// drops a token it does not recognise tells someone they do not own what they bought.
/// </summary>
public class AccessTests
{
    private static IReadOnlyList<string> Names(IEnumerable<string>? access) =>
        Access.Expansions(access).Select(owned => owned.Name).ToList();

    [Fact]
    public void Path_of_fire_brings_heart_of_thorns_with_it()
    {
        // Found on a real account: the app listed one expansion where the account page showed
        // two. From the API's own documentation — an account that received Heart of Thorns by
        // buying Path of Fire "will not have this flag set", and PathOfFire "also implies that
        // the account has access to Heart of Thorns". Reading the array literally tells such
        // an account it does not own something it plainly does.
        var owned = Access.Expansions(["GuildWars2", "PathOfFire"]);

        Assert.Equal(["Heart of Thorns", "Path of Fire"], owned.Select(x => x.Name));
        Assert.Equal("included with Path of Fire", owned[0].Note);
    }

    [Fact]
    public void An_account_that_bought_both_is_not_told_twice()
    {
        var owned = Access.Expansions(["GuildWars2", "HeartOfThorns", "PathOfFire"]);

        Assert.Equal(["Heart of Thorns", "Path of Fire"], owned.Select(x => x.Name));

        // Bought outright, so there is nothing to explain.
        Assert.All(owned, expansion => Assert.False(expansion.HasNote));
    }

    [Fact]
    public void Heart_of_thorns_is_only_implied_by_path_of_fire()
    {
        // No other expansion carries it: End of Dragons and everything after were sold
        // standalone. Inventing ownership would be the same failure in the other direction.
        Assert.Equal(["End of Dragons"], Names(["GuildWars2", "EndOfDragons"]));
    }

    [Fact]
    public void Every_expansion_carries_its_own_emblem()
    {
        // The API has artwork for professions and none for expansions, so these are cut for
        // this app and shipped with it — which also means they are there with no connection.
        var owned = Access.Expansions(["GuildWars2", "HeartOfThorns", "PathOfFire"]);

        Assert.Equal("expansion_hot.png", owned[0].Icon);
        Assert.Equal("expansion_pof.png", owned[1].Icon);
    }

    [Fact]
    public void An_unknown_expansion_gets_the_plain_mark()
    {
        // Same rule as the names: shown imperfectly rather than not at all. A blank space
        // where an emblem belongs reads as a picture that failed to load.
        Assert.Equal("expansion_other.png",
            Assert.Single(Access.Expansions(["GuildWars2", "RealmOfDreams"])).Icon);
    }

    [Fact]
    public void The_implied_expansion_is_not_left_blank()
    {
        // Heart of Thorns arrives by inference rather than by token, and still needs its mark.
        Assert.Equal("expansion_hot.png",
            Access.Expansions(["GuildWars2", "PathOfFire"])[0].Icon);
    }

    [Fact]
    public void Expansions_are_named_the_way_they_are_written()
    {
        // Splitting the token gives "Heart Of Thorns" and "Secrets Of The Obscure".
        string[] access = ["GuildWars2", "HeartOfThorns", "PathOfFire", "SecretsOfTheObscure"];

        Assert.Equal(
            ["Heart of Thorns", "Path of Fire", "Secrets of the Obscure"],
            Names(access));
    }

    [Fact]
    public void An_expansion_this_build_has_never_heard_of_still_shows_up()
    {
        // The whole point: a new expansion should be named badly rather than not at all.
        Assert.Equal(["Realm Of Dreams"], Names(["GuildWars2", "RealmOfDreams"]));
    }

    [Fact]
    public void The_base_game_is_not_an_expansion()
    {
        Assert.Empty(Access.Expansions(["GuildWars2"]));
        Assert.Empty(Access.Expansions(["PlayForFree"]));
        Assert.Empty(Access.Expansions(["None"]));
        Assert.Empty(Access.Expansions(null));
    }

    [Fact]
    public void Release_order_is_kept_rather_than_alphabetical()
    {
        // The tokens arrive in release order, which is the order that means something.
        // Sorting by name would put End of Dragons first.
        string[] access = ["GuildWars2", "HeartOfThorns", "PathOfFire", "EndOfDragons"];

        Assert.Equal(["Heart of Thorns", "Path of Fire", "End of Dragons"], Names(access));
    }

    [Fact]
    public void A_free_account_is_one_with_nothing_else_on_it()
    {
        // The flag alone is not the test: the documentation says PlayForFree "also appears on
        // accounts who have purchased the base game or any of the expansions", and only means
        // a free account when it stands without any other flag.
        Assert.True(Access.IsFree(["PlayForFree"]));

        Assert.False(Access.IsFree(["PlayForFree", "GuildWars2"]));
        Assert.False(Access.IsFree(["PlayForFree", "PathOfFire"]));
        Assert.False(Access.IsFree(["GuildWars2", "JanthirWilds"]));
    }

    [Fact]
    public void The_summary_says_how_the_game_is_held_and_how_much_of_it()
    {
        Assert.Equal("Guild Wars 2 · 2 expansions",
            Access.Summarise(["GuildWars2", "HeartOfThorns", "PathOfFire"]));

        Assert.Equal("Guild Wars 2 · 1 expansion",
            Access.Summarise(["GuildWars2", "HeartOfThorns"]));
    }

    [Fact]
    public void An_account_with_no_expansions_says_so_rather_than_nothing()
    {
        // An empty space reads as something that failed to load.
        Assert.Equal("Guild Wars 2 · No expansions", Access.Summarise(["GuildWars2"]));
        Assert.Equal("Free account · No expansions", Access.Summarise(["PlayForFree"]));
    }
}
