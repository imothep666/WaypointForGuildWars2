using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// "Can I make this?" — a recipe measured against the cupboard.
///
/// Every error here sends a player to the trading post for something they already have, or
/// to a crafting station for something they cannot make. Both look entirely plausible on
/// screen, which is exactly why the arithmetic is pinned here.
/// </summary>
public class CraftPlanTests
{
    private static CraftLine Line(string name, int needed, int? held) =>
        new(1, name, needed, held);

    // --- how many batches ---------------------------------------------------

    [Fact]
    public void The_scarcest_ingredient_sets_the_limit()
    {
        // 250 ore makes 125 batches, but 3 dust only makes 3 — the answer is 3.
        var lines = new[] { Line("Iron Ore", 2, 250), Line("Glittering Dust", 1, 3) };

        Assert.Equal(3, CraftPlan.Batches(lines));
    }

    [Fact]
    public void Partial_batches_do_not_count()
    {
        // Five of something needing two is two batches, not two and a half.
        Assert.Equal(2, CraftPlan.Batches([Line("Ore", 2, 5)]));
    }

    [Fact]
    public void Holding_none_is_zero_batches_not_unknown() =>
        Assert.Equal(0, CraftPlan.Batches([Line("Ore", 2, 0)]));

    [Fact]
    public void One_unknown_holding_makes_the_whole_answer_unknown()
    {
        // Storage has not been read. Reporting "0" would tell the player they cannot craft
        // something they may well have the materials for.
        var lines = new[] { Line("Ore", 2, 250), Line("Dust", 1, null) };

        Assert.Null(CraftPlan.Batches(lines));
    }

    [Fact]
    public void An_ingredient_needing_nothing_cannot_limit_anything()
    {
        // Guards the division. A zero count would otherwise throw or report infinity.
        var lines = new[] { Line("Odd", 0, 0), Line("Ore", 2, 10) };

        Assert.Equal(5, CraftPlan.Batches(lines));
    }

    [Fact]
    public void A_recipe_with_no_countable_lines_is_zero_rather_than_unbounded() =>
        Assert.Equal(0, CraftPlan.Batches([]));

    // --- what is missing ----------------------------------------------------

    [Fact]
    public void Missing_lines_lead_with_the_largest_gap()
    {
        var lines = new[]
        {
            Line("Dust", 10, 8),      // short 2
            Line("Ore", 50, 5),       // short 45
            Line("Wood", 4, 4)        // satisfied
        };

        Assert.Equal(["Ore", "Dust"], CraftPlan.Missing(lines).Select(l => l.Name));
    }

    [Fact]
    public void An_unknown_holding_is_not_reported_as_missing()
    {
        // It might be sitting in the bank; claiming it is short would be a guess.
        Assert.Empty(CraftPlan.Missing([Line("Ore", 5, null)]));
    }

    [Fact]
    public void Shortfall_is_what_is_still_wanted_not_the_whole_requirement() =>
        Assert.Equal(2, Line("Dust", 10, 8).Short);

    [Fact]
    public void Holding_more_than_enough_is_short_nothing_rather_than_negative()
    {
        Assert.Equal(0, Line("Dust", 2, 250).Short);
        Assert.True(Line("Dust", 2, 250).IsSatisfied);
    }

    // --- the sentence -------------------------------------------------------

    [Fact]
    public void Without_storage_it_asks_rather_than_guesses() =>
        Assert.Equal(
            "Open Find once to have the app count what you are holding.",
            CraftPlan.Verdict([Line("Ore", 2, null)], 1, storageKnown: false));

    [Fact]
    public void A_recipe_that_yields_several_counts_the_items_not_the_batches()
    {
        // Five batches of a recipe making two at a time is ten items, and ten is the number
        // the player cares about.
        var verdict = CraftPlan.Verdict([Line("Ore", 2, 10)], output: 2, storageKnown: true);

        Assert.Equal("You can make 10 right now.", verdict);
    }

    [Fact]
    public void One_is_spelled_out_rather_than_left_as_a_digit() =>
        Assert.Equal(
            "You can make one right now.",
            CraftPlan.Verdict([Line("Ore", 2, 2)], 1, storageKnown: true));

    [Fact]
    public void A_single_gap_is_named_because_it_is_actionable() =>
        Assert.Equal(
            "Short 42 Mithril Ore.",
            CraftPlan.Verdict([Line("Mithril Ore", 50, 8)], 1, storageKnown: true));

    [Fact]
    public void Several_gaps_are_counted_rather_than_listed_in_one_line()
    {
        var verdict = CraftPlan.Verdict(
            [Line("Ore", 50, 8), Line("Dust", 10, 2), Line("Wood", 4, 4)], 1, storageKnown: true);

        Assert.Equal("Short 2 of the 3 ingredients.", verdict);
    }

    [Fact]
    public void Large_shortfalls_are_grouped_for_reading()
    {
        var verdict = CraftPlan.Verdict([Line("Mithril Ore", 5000, 0)], 1, storageKnown: true);

        Assert.Contains("5,000", verdict.Replace(".", ","));
    }
}
