using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// What the account can make outright from what it is holding.
///
/// The wrong answers here send someone to a crafting station for nothing: a recipe no
/// character can make, or one whose materials do not actually cover a batch. Both look
/// entirely plausible on the screen.
/// </summary>
public class MakeableTests
{
    private static MakeableRow Row(
        string disciplines = "|Chef|", int rating = 0, int batches = 1, int output = 500) =>
        new(1, output, 1, rating, disciplines, batches);

    [Fact]
    public void A_recipe_needs_a_character_who_has_the_discipline()
    {
        // The materials being there is half the question; someone has to be able to make it.
        var plans = Makeable.Plan([Row("|Chef|")], [("Artificer", "Imosan", 400)]);

        Assert.Empty(plans);
    }

    [Fact]
    public void A_recipe_needs_a_character_with_the_rating()
    {
        var plans = Makeable.Plan([Row("|Chef|", rating: 300)], [("Chef", "Imosan", 250)]);

        Assert.Empty(plans);
    }

    [Fact]
    public void The_lowest_qualifying_character_is_the_one_named()
    {
        // Crafting below your rating earns nothing, so the character still levelling does it.
        var plans = Makeable.Plan(
            [Row("|Chef|", rating: 100)],
            [("Chef", "Capped", 500), ("Chef", "Levelling", 150)]);

        Assert.Equal("Levelling", Assert.Single(plans).Character);
    }

    [Fact]
    public void A_discipline_is_matched_whole_rather_than_as_a_substring()
    {
        // The cache stores them pipe-delimited. A bare Contains would match "Armorsmith"
        // inside "Leatherworker,Armorsmith" the day a name becomes part of another.
        Assert.True(Makeable.Requires(Row("|Leatherworker|Armorsmith|"), "Armorsmith"));
        Assert.False(Makeable.Requires(Row("|Leatherworker|"), "Leatherwork"));
        Assert.False(Makeable.Requires(new MakeableRow(1, 1, 1, 0, "", 1), "Chef"));
    }

    [Fact]
    public void A_recipe_the_materials_do_not_cover_is_not_on_the_list()
    {
        // The query reports zero batches when a stack is short; showing it anyway would be a
        // trip to the station for nothing.
        var plans = Makeable.Plan([Row(batches: 0)], [("Chef", "Imosan", 400)]);

        Assert.Empty(plans);
    }

    [Fact]
    public void What_there_are_most_materials_for_leads()
    {
        // Those are the stacks worth clearing.
        var rows = new[]
        {
            new MakeableRow(1, 100, 1, 0, "|Chef|", 2),
            new MakeableRow(2, 200, 1, 0, "|Chef|", 40),
            new MakeableRow(3, 300, 1, 0, "|Chef|", 9)
        };

        var plans = Makeable.Plan(rows, [("Chef", "Imosan", 400)]);

        Assert.Equal([200, 300, 100], plans.Select(p => p.OutputItemId));
    }

    [Fact]
    public void One_batch_is_worded_as_once()
    {
        var plan = Assert.Single(Makeable.Plan([Row(batches: 1)], [("Chef", "Imosan", 400)]));

        Assert.Equal("Chef · Imosan · once", Makeable.Describe(plan));
    }

    [Fact]
    public void Several_batches_are_counted()
    {
        var plan = Assert.Single(Makeable.Plan([Row(batches: 12)], [("Chef", "Imosan", 400)]));

        Assert.Equal("Chef · Imosan · 12×", Makeable.Describe(plan));
    }

    [Fact]
    public void Nothing_makeable_is_said_plainly()
    {
        // An empty list with no line above it reads as a screen that failed to load.
        Assert.Equal("Nothing can be made outright from what you are holding.",
            Makeable.Summarise(0));

        Assert.Equal("One recipe can be made from what you are holding.", Makeable.Summarise(1));
        Assert.Equal("14 recipes can be made from what you are holding.", Makeable.Summarise(14));
    }

    [Fact]
    public void A_character_with_two_disciplines_can_be_named_for_either()
    {
        var rows = new[]
        {
            new MakeableRow(1, 100, 1, 0, "|Chef|", 1),
            new MakeableRow(2, 200, 1, 0, "|Artificer|", 1)
        };

        var plans = Makeable.Plan(rows, [("Chef", "Imosan", 400), ("Artificer", "Imosan", 400)]);

        // Equal batches, so the tie breaks on the discipline name rather than on the order
        // the recipes happened to come out of the database.
        Assert.Equal(["Artificer", "Chef"], plans.Select(p => p.Discipline));
    }
}
