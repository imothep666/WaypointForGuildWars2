using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Make it or buy it, and whether making it to sell is worth the materials.
///
/// The arithmetic is small and the consequences are not: getting the fee or the batch size
/// wrong turns a loss into an apparent profit, and the player finds out after spending the
/// materials.
/// </summary>
public class CraftValueTests
{
    // --- cost per item ------------------------------------------------------

    [Fact]
    public void A_batch_of_one_costs_what_the_batch_costs() =>
        Assert.Equal(500, CraftValue.PerItem(500, 1));

    [Fact]
    public void A_batch_that_yields_five_is_five_times_cheaper_each() =>
        Assert.Equal(100, CraftValue.PerItem(500, 5));

    [Fact]
    public void A_yield_of_zero_is_treated_as_one_rather_than_dividing_by_it() =>
        Assert.Equal(500, CraftValue.PerItem(500, 0));

    // --- make or buy --------------------------------------------------------

    [Fact]
    public void Cheaper_to_make_names_the_saving()
    {
        var verdict = CraftValue.AgainstBuying(batchCost: 100, output: 1, buyOutright: 160);

        Assert.StartsWith("Cheaper to make", verdict);
        Assert.Contains("60c", verdict);
    }

    [Fact]
    public void Cheaper_to_buy_says_so_plainly()
    {
        var verdict = CraftValue.AgainstBuying(batchCost: 200, output: 1, buyOutright: 150);

        Assert.StartsWith("Cheaper to buy", verdict);
    }

    [Fact]
    public void The_batch_size_decides_which_is_cheaper()
    {
        // 500 for five is 100 each, which beats buying at 150 — the opposite conclusion from
        // comparing the batch price against one item.
        Assert.StartsWith("Cheaper to make",
            CraftValue.AgainstBuying(batchCost: 500, output: 5, buyOutright: 150));

        Assert.StartsWith("Cheaper to buy",
            CraftValue.AgainstBuying(batchCost: 500, output: 1, buyOutright: 150));
    }

    [Fact]
    public void An_exact_match_is_not_dressed_up_as_a_saving() =>
        Assert.Equal("Making one costs the same as buying it.",
            CraftValue.AgainstBuying(150, 1, 150));

    [Fact]
    public void Something_nobody_sells_cannot_be_compared()
    {
        // Making it is not "cheaper" — it is the only way, and saying otherwise is a lie.
        Assert.Null(CraftValue.AgainstBuying(batchCost: 100, output: 1, buyOutright: 0));
    }

    [Fact]
    public void An_unpriced_recipe_cannot_be_compared_either() =>
        Assert.Null(CraftValue.AgainstBuying(batchCost: 0, output: 1, buyOutright: 100));

    // --- making to sell ------------------------------------------------------

    [Fact]
    public void Profit_is_after_the_trading_post_takes_its_cut()
    {
        // Sells for 1000, so 850 is kept; costing 500 leaves 350 — not 500.
        var verdict = CraftValue.AsProfit(batchCost: 500, output: 1, sellsInstantlyFor: 1000);

        Assert.Contains("3s 50c", verdict);
        Assert.Contains("nets", verdict);
    }

    [Fact]
    public void A_loss_is_reported_as_a_loss()
    {
        // Sells for 500 (keep 425) but costs 600. Silence here would be the expensive kind.
        var verdict = CraftValue.AsProfit(batchCost: 600, output: 1, sellsInstantlyFor: 500);

        Assert.Contains("loses", verdict);
        Assert.Contains("1s 75c", verdict);
    }

    [Fact]
    public void Breaking_even_says_so()
    {
        // 1000 kept at 85% is 850, against 850 to make.
        Assert.Contains("breaks even", CraftValue.AsProfit(850, 1, 1000));
    }

    [Fact]
    public void A_yield_of_several_is_taken_into_account()
    {
        // 500 for five is 100 each; selling at 200 keeps 170, so 70 profit each.
        var verdict = CraftValue.AsProfit(batchCost: 500, output: 5, sellsInstantlyFor: 200);

        Assert.Contains("nets", verdict);
        Assert.Contains("70c", verdict);
    }

    [Fact]
    public void Something_with_no_buyer_has_no_profit_to_report() =>
        Assert.Null(CraftValue.AsProfit(batchCost: 500, output: 1, sellsInstantlyFor: 0));
}
