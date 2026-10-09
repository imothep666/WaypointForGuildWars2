using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// What an item turns into.
///
/// The app could always say what makes an item and never what an item makes. Both directions
/// are in the same local table, so this costs nothing — but a list capped without saying so
/// would read as the whole answer, and a count of one is a different prospect from a count of
/// fifty.
/// </summary>
public class UsedInTests
{
    private static UsedInRow Row(int needed = 1, int rating = 0, string disciplines = "|Tailor|") =>
        new(500, "Bolt of Jute", needed, 1, rating, disciplines);

    [Fact]
    public void How_many_it_takes_is_the_point()
    {
        // One at a time and fifty at a time are different prospects, and the recipe name
        // alone does not say which.
        Assert.StartsWith("2 needed", UsedIn.Describe(Row(needed: 2)));
    }

    [Fact]
    public void The_discipline_and_rating_say_who_can_make_it()
    {
        Assert.Equal("1 needed · Tailor 75", UsedIn.Describe(Row(rating: 75)));
    }

    [Fact]
    public void A_recipe_at_no_rating_does_not_pretend_to_have_one()
    {
        // "Tailor 0" would read as a rating rather than as none.
        Assert.Equal("1 needed · Tailor", UsedIn.Describe(Row(rating: 0)));
    }

    [Fact]
    public void A_recipe_with_no_discipline_says_only_what_it_takes()
    {
        Assert.Equal("3 needed", UsedIn.Describe(Row(needed: 3, disciplines: "")));
    }

    [Fact]
    public void Several_disciplines_are_all_named()
    {
        // The cache stores them pipe-delimited; a recipe open to two crafters is worth knowing.
        Assert.Equal("Leatherworker · Armorsmith", UsedIn.Names("|Leatherworker|Armorsmith|"));
        Assert.Equal(string.Empty, UsedIn.Names(null));
    }

    [Fact]
    public void A_capped_list_admits_the_cap()
    {
        // Otherwise fifteen of two hundred reads as the whole answer.
        Assert.Equal("Used in 208 recipes — the 15 soonest to reach.", UsedIn.Summarise(15, 208));
    }

    [Fact]
    public void A_complete_list_says_nothing_about_caps()
    {
        Assert.Equal("Used in 4 recipes.", UsedIn.Summarise(4, 4));
        Assert.Equal("Used in one recipe.", UsedIn.Summarise(1, 1));
    }

    [Fact]
    public void An_item_nothing_uses_says_so()
    {
        // Most items are not ingredients, and silence there reads as a screen that failed.
        Assert.Equal("Nothing in the catalogue uses this.", UsedIn.Summarise(0, 0));
    }
}
