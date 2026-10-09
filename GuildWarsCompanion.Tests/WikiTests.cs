using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The hand-off to the wiki. A malformed URL fails in the player's browser rather than in the
/// app, which is the worst place for it to fail — nothing here would look broken.
/// </summary>
public class WikiTests
{
    [Fact]
    public void A_plain_name_becomes_a_search_url() =>
        Assert.Equal(
            "https://wiki.guildwars2.com/index.php?search=Glob%20of%20Ectoplasm",
            Wiki.UrlFor("Glob of Ectoplasm"));

    [Theory]
    [InlineData("Zho's Mask", "Zho%27s%20Mask")]
    [InlineData("Sunrise & Twilight", "Sunrise%20%26%20Twilight")]
    [InlineData("Superior Rune of the Pack", "Superior%20Rune%20of%20the%20Pack")]
    [InlineData("+5 Agony Infusion", "%2B5%20Agony%20Infusion")]
    public void Punctuation_that_would_break_a_url_is_escaped(string name, string expected) =>
        Assert.Equal($"https://wiki.guildwars2.com/index.php?search={expected}", Wiki.UrlFor(name));

    [Fact]
    public void Surrounding_whitespace_is_not_carried_into_the_url() =>
        Assert.Equal(
            "https://wiki.guildwars2.com/index.php?search=Bolt",
            Wiki.UrlFor("  Bolt\n"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void With_no_name_the_wiki_front_page_is_still_a_useful_place_to_land(string? name) =>
        Assert.Equal("https://wiki.guildwars2.com/", Wiki.UrlFor(name));

    [Fact]
    public void Every_url_is_absolute_and_https()
    {
        // A relative or http URL either fails to launch or gets blocked by cleartext policy.
        foreach (var name in new[] { "Bolt", "Zho's Mask", "", "  " })
        {
            var url = Wiki.UrlFor(name);

            Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var uri), $"not absolute: {url}");
            Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
        }
    }
}

/// <summary>
/// Which of the API's item flags are worth putting in front of a player, and in what words.
/// </summary>
public class ItemFactsTests
{
    [Fact]
    public void Binding_is_reported_because_it_decides_what_you_can_do_with_the_thing() =>
        Assert.Equal("Account bound", ItemFacts.Summarise(["AccountBound"]));

    [Fact]
    public void Engine_noise_is_left_out()
    {
        // A real legendary's flags. "HideSuffix" and "DeleteWarning" mean nothing to a player.
        var facts = ItemFacts.Describe(
            ["HideSuffix", "NoSalvage", "NoSell", "AccountBindOnUse", "DeleteWarning"]);

        Assert.Equal(["Account bound on use", "Cannot be sold", "Cannot be salvaged"], facts);
    }

    [Fact]
    public void Binding_leads_because_it_is_the_fact_that_matters_most()
    {
        var facts = ItemFacts.Describe(["NoSell", "SoulbindOnAcquire"]);

        Assert.Equal("Soulbound", facts[0]);
    }

    [Fact]
    public void Nothing_worth_saying_says_nothing()
    {
        // A plain trading material has no flags, and an empty line under its name would read
        // as something failing to load.
        Assert.Null(ItemFacts.Summarise([]));
        Assert.Null(ItemFacts.Summarise(null));
        Assert.Null(ItemFacts.Summarise(["HideSuffix"]));
    }

    [Fact]
    public void Unknown_flags_are_ignored_rather_than_shown_raw()
    {
        // ArenaNet adds flags; a future one must not leak engine vocabulary onto the screen.
        Assert.Null(ItemFacts.Summarise(["SomeFutureFlag"]));
    }

    [Theory]
    [InlineData("CraftingMaterial", "Crafting Material")]
    [InlineData("UpgradeComponent", "Upgrade Component")]
    [InlineData("Trophy", "Trophy")]
    [InlineData("Sword", "Sword")]
    [InlineData("MiniPet", "Mini Pet")]
    [InlineData("HelmAquatic", "Helm Aquatic")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Pascal_case_types_are_split_into_words(string? type, string expected) =>
        Assert.Equal(expected, ItemFacts.ReadableType(type));

    [Fact]
    public void Flag_casing_from_the_api_is_not_trusted() =>
        Assert.Equal("Soulbound", ItemFacts.Summarise(["soulbindonacquire"]));

    [Fact]
    public void A_map_prefix_is_dropped_before_searching()
    {
        // Collection entries for exploration read "Sparkfly Fen: Toade's Head". The wiki
        // indexes the area under its own name, and searching the whole string lands on
        // "Explorer" instead — checked against the wiki's own search API.
        Assert.Equal("Toade's Head", Wiki.SearchTerm("Sparkfly Fen: Toade's Head"));
    }

    [Fact]
    public void Only_the_last_prefix_goes()
    {
        Assert.Equal("Deep", Wiki.SearchTerm("Region: Map: Deep"));
    }

    [Fact]
    public void A_plain_name_is_left_alone()
    {
        Assert.Equal("Glob of Ectoplasm", Wiki.SearchTerm("Glob of Ectoplasm"));
        Assert.Equal("Scale Legs", Wiki.SearchTerm("Scale Legs"));
    }

    [Fact]
    public void A_name_that_only_ends_in_a_colon_keeps_itself()
    {
        // The split is on a colon *and a space*, so nothing here looks like a prefix and
        // there is nothing to drop. Dropping the tail would search for an empty string.
        Assert.Equal("Mysteries:", Wiki.SearchTerm("Mysteries:"));
        Assert.Equal("Mysteries:", Wiki.SearchTerm("  Mysteries: "));
    }
}
