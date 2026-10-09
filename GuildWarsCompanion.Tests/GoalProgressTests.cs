using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// A pinned goal's progress: the same walk as the item page's "all the way down", measured from
/// nothing and from what is held.
/// </summary>
public class GoalProgressTests
{
    // A spearhead from two ingots, an ingot from two ore and one coal. 4 ore + 2 coal from nothing.
    private const int Spearhead = 1, Ingot = 2, Ore = 3, Coal = 4, Gift = 5;

    private static readonly Dictionary<int, RecipeShape> Recipes = new()
    {
        [Spearhead] = new RecipeShape(1, [(Ingot, 2)]),
        [Ingot] = new RecipeShape(1, [(Ore, 2), (Coal, 1)])
    };

    private static GoalCount Count(int item, Dictionary<int, int> held, int quantity = 1) =>
        GoalProgress.Of(item, quantity, Recipes.GetValueOrDefault, held);

    [Fact]
    public void From_nothing_everything_is_missing()
    {
        var count = Count(Spearhead, []);

        Assert.Equal(6, count.Total);
        Assert.Equal(6, count.Missing);
        Assert.Equal(0, count.InHand);
    }

    [Fact]
    public void Raw_materials_held_count_towards_it()
    {
        var count = Count(Spearhead, new() { [Ore] = 3 });

        Assert.Equal(3, count.InHand);
        Assert.Equal([new CraftNeed(Coal, 2), new CraftNeed(Ore, 1)], count.Short);
    }

    [Fact]
    public void A_held_intermediate_stands_for_everything_under_it()
    {
        // One ingot already made is two ore and a coal not needed.
        var count = Count(Spearhead, new() { [Ingot] = 1 });

        Assert.Equal(3, count.InHand);
        Assert.Equal(3, count.Missing);
    }

    [Fact]
    public void Holding_the_goal_itself_is_done()
    {
        var count = Count(Spearhead, new() { [Spearhead] = 1 });

        Assert.True(count.IsReady);
        Assert.Equal(6, count.InHand);
    }

    [Fact]
    public void Something_nothing_makes_counts_only_itself()
    {
        // A Mystic Forge gift: the API publishes no recipe, so it is its own raw material.
        var count = Count(Gift, new() { [Gift] = 0 }, quantity: 2);

        Assert.Equal(2, count.Total);
        Assert.Equal(2, count.Missing);
    }

    [Fact]
    public void Quantity_multiplies_the_whole_chain()
    {
        Assert.Equal(12, Count(Spearhead, [], quantity: 2).Total);
    }

    [Fact]
    public void The_shortfall_is_priced_only_when_all_of_it_is_on_the_market()
    {
        CraftNeed[] needs = [new(Ore, 10), new(Coal, 5)];

        Assert.Equal("about 1s 50c to buy", GoalPrice.Describe(needs, id => id == Ore ? 10 : 10));
        Assert.Equal("some of it cannot be bought", GoalPrice.Describe(needs, id => id == Ore ? 10 : 0));
    }
}
