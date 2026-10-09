using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Expanding a recipe down to what you actually have to go and get.
///
/// This is the arithmetic a player would otherwise do on paper, and every mistake it can make
/// is expensive in the real world: too low and you get to the station and cannot finish, too
/// high and you buy materials you already own.
/// </summary>
public class CraftTreeTests
{
    // A small world: an ingot is two ore, a plate is three ingots, a component is a plate
    // plus a lump. Ore, lumps and dust are gathered.
    private const int Component = 1, Plate = 2, Ingot = 3, Ore = 4, Lump = 5, Dust = 6;

    private static RecipeShape? Recipes(int id) => id switch
    {
        Component => new RecipeShape(1, [(Plate, 1), (Lump, 2)]),
        Plate => new RecipeShape(1, [(Ingot, 3)]),
        Ingot => new RecipeShape(1, [(Ore, 2)]),
        _ => null
    };

    private static IReadOnlyList<CraftNeed> Materials(
        int item, int quantity, Dictionary<int, int>? held = null) =>
        CraftTree.Materials(item, quantity, Recipes, held ?? [], out _);

    private static int Needed(IReadOnlyList<CraftNeed> needs, int itemId) =>
        needs.FirstOrDefault(n => n.ItemId == itemId)?.Count ?? 0;

    [Fact]
    public void A_gathered_item_is_needed_as_itself()
    {
        Assert.Equal([new CraftNeed(Ore, 5)], Materials(Ore, 5));
    }

    [Fact]
    public void One_level_multiplies_through()
    {
        // One ingot is two ore.
        Assert.Equal(2, Needed(Materials(Ingot, 1), Ore));
    }

    [Fact]
    public void The_whole_chain_multiplies_through()
    {
        // A component needs a plate (3 ingots = 6 ore) and 2 lumps.
        var needs = Materials(Component, 1);

        Assert.Equal(6, Needed(needs, Ore));
        Assert.Equal(2, Needed(needs, Lump));
    }

    [Fact]
    public void Quantity_scales_the_whole_chain()
    {
        var needs = Materials(Component, 10);

        Assert.Equal(60, Needed(needs, Ore));
        Assert.Equal(20, Needed(needs, Lump));
    }

    // --- what you already hold ---------------------------------------------

    [Fact]
    public void Holding_the_finished_item_means_needing_nothing()
    {
        Assert.Empty(Materials(Component, 2, new Dictionary<int, int> { [Component] = 2 }));
    }

    [Fact]
    public void Holding_an_intermediate_stops_the_descent()
    {
        // The whole point: 3 ingots in the bank is not 6 ore to go and mine.
        var needs = Materials(Plate, 1, new Dictionary<int, int> { [Ingot] = 3 });

        Assert.Empty(needs);
    }

    [Fact]
    public void Only_the_shortfall_is_expanded()
    {
        // Two of the three ingots are held, so only the third becomes ore.
        var needs = Materials(Plate, 1, new Dictionary<int, int> { [Ingot] = 2 });

        Assert.Equal(2, Needed(needs, Ore));
    }

    [Fact]
    public void Held_materials_are_spent_not_reused()
    {
        // Both branches want ore, and there are only 4. The second branch must not be told
        // the same 4 are still available.
        var recipes = (Func<int, RecipeShape?>)(id => id switch
        {
            Component => new RecipeShape(1, [(Ingot, 1), (Plate, 1)]),
            Plate => new RecipeShape(1, [(Ingot, 1)]),
            Ingot => new RecipeShape(1, [(Ore, 4)]),
            _ => null
        });

        // Two ingots' worth of ore is 8; holding 4 leaves 4 to find.
        var needs = CraftTree.Materials(
            Component, 1, recipes, new Dictionary<int, int> { [Ore] = 4 }, out _);

        Assert.Equal(4, Needed(needs, Ore));
    }

    [Fact]
    public void Holding_more_than_enough_does_not_go_negative()
    {
        var needs = Materials(Ingot, 1, new Dictionary<int, int> { [Ore] = 500 });

        Assert.Empty(needs);
    }

    // --- batches ------------------------------------------------------------

    [Fact]
    public void A_recipe_that_yields_several_rounds_up_to_whole_batches()
    {
        // Five per batch; wanting six means two batches, so twelve ore, not ten.
        var recipes = (Func<int, RecipeShape?>)(id =>
            id == Ingot ? new RecipeShape(5, [(Ore, 10)]) : null);

        var needs = CraftTree.Materials(Ingot, 6, recipes, [], out _);

        Assert.Equal(20, Needed(needs, Ore));
    }

    [Fact]
    public void An_exact_multiple_does_not_round_up_an_extra_batch()
    {
        var recipes = (Func<int, RecipeShape?>)(id =>
            id == Ingot ? new RecipeShape(5, [(Ore, 10)]) : null);

        Assert.Equal(10, Needed(CraftTree.Materials(Ingot, 5, recipes, [], out _), Ore));
    }

    // --- shape of the answer ------------------------------------------------

    [Fact]
    public void The_same_material_from_two_branches_is_summed_once()
    {
        var recipes = (Func<int, RecipeShape?>)(id => id switch
        {
            Component => new RecipeShape(1, [(Ingot, 1), (Plate, 1)]),
            Plate => new RecipeShape(1, [(Ingot, 2)]),
            Ingot => new RecipeShape(1, [(Ore, 2)]),
            _ => null
        });

        var needs = CraftTree.Materials(Component, 1, recipes, [], out _);

        Assert.Single(needs);
        Assert.Equal(6, Needed(needs, Ore));
    }

    [Fact]
    public void The_biggest_pile_is_listed_first()
    {
        var needs = Materials(Component, 1);

        Assert.Equal(Ore, needs[0].ItemId);
    }

    [Fact]
    public void Asking_for_nothing_needs_nothing() => Assert.Empty(Materials(Component, 0));

    [Fact]
    public void A_cycle_in_the_data_is_stopped_and_declared()
    {
        // Two recipes that make each other would otherwise recurse until the stack gives out.
        var recipes = (Func<int, RecipeShape?>)(id => id switch
        {
            1 => new RecipeShape(1, [(2, 1)]),
            2 => new RecipeShape(1, [(1, 1)]),
            _ => null
        });

        var needs = CraftTree.Materials(1, 1, recipes, [], out var truncated);

        Assert.True(truncated, "a cycle must be reported, not silently trimmed");
        Assert.NotEmpty(needs);
    }
}
