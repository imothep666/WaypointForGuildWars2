using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The two rules that turn a list of recipes into levelling advice.
///
/// Both come from the wiki rather than the API, and both are the kind of thing that produces
/// confident nonsense when wrong: recommending a grey recipe wastes an evening's materials
/// for no progress, and hiding a recipe that still teaches wastes the advice entirely.
/// </summary>
public class CraftingLevelsTests
{
    // --- when a recipe goes grey --------------------------------------------

    [Fact]
    public void A_recipe_at_your_exact_rating_teaches()
    {
        Assert.True(CraftingLevels.StillTeaches(rating: 75, minRating: 75, "Refinement"));
    }

    [Fact]
    public void A_recipe_above_your_rating_cannot_be_made_at_all()
    {
        Assert.False(CraftingLevels.StillTeaches(rating: 74, minRating: 75, "Refinement"));
    }

    [Fact]
    public void Refinements_go_grey_after_twenty_five_levels()
    {
        // Cheap and endlessly repeatable, so the game closes them off quickly.
        Assert.True(CraftingLevels.StillTeaches(99, 75, "Refinement"));
        Assert.False(CraftingLevels.StillTeaches(100, 75, "Refinement"));
    }

    [Fact]
    public void Components_share_the_short_span()
    {
        Assert.True(CraftingLevels.StillTeaches(99, 75, "Component"));
        Assert.False(CraftingLevels.StillTeaches(100, 75, "Component"));
    }

    [Fact]
    public void Equipment_and_food_stay_useful_for_forty()
    {
        // Anything that is not a refinement or a component: insignias, inscriptions, boots.
        Assert.True(CraftingLevels.StillTeaches(114, 75, "Insignia"));
        Assert.False(CraftingLevels.StillTeaches(115, 75, "Insignia"));

        Assert.True(CraftingLevels.StillTeaches(114, 75, "Boots"));
        Assert.True(CraftingLevels.StillTeaches(114, 75, null));
    }

    [Fact]
    public void Legendary_components_almost_never_go_grey() =>
        Assert.True(CraftingLevels.StillTeaches(400, 400 - 249, "LegendaryComponent"));

    [Theory]
    [InlineData("Refinement", 25)]
    [InlineData("Component", 25)]
    [InlineData("Insignia", 40)]
    [InlineData("LegendaryComponent", 250)]
    [InlineData(null, 40)]
    public void The_span_depends_on_what_is_being_made(string? type, int span) =>
        Assert.Equal(span, CraftingLevels.SpanFor(type));

    // --- tiers ---------------------------------------------------------------

    [Theory]
    [InlineData(0, 75)]
    [InlineData(74, 75)]
    [InlineData(75, 150)]
    [InlineData(300, 400)]
    [InlineData(400, 500)]
    public void The_next_tier_is_the_first_boundary_above_you(int rating, int expected) =>
        Assert.Equal(expected, CraftingLevels.NextTier(rating));

    [Fact]
    public void At_the_cap_there_is_no_next_tier()
    {
        Assert.Null(CraftingLevels.NextTier(500));
        Assert.Null(CraftingLevels.ToNextTier(500));
    }

    [Fact]
    public void What_is_left_is_the_gap_not_the_boundary() =>
        Assert.Equal(38, CraftingLevels.ToNextTier(112));

    [Fact]
    public void A_rating_reads_with_its_target() =>
        Assert.Equal("112 · 38 to the next tier", CraftingLevels.Describe(112));

    [Fact]
    public void The_cap_says_so_rather_than_showing_a_gap_of_nothing() =>
        Assert.Equal("500 — at the cap", CraftingLevels.Describe(500));

    [Fact]
    public void Every_rating_from_zero_to_the_cap_describes_itself()
    {
        // Guards the tier walk against a rating that falls in no band.
        for (var rating = 0; rating <= CraftingLevels.Max; rating++)
        {
            var text = CraftingLevels.Describe(rating);

            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("-", text.Replace("—", ""));
        }
    }
}
