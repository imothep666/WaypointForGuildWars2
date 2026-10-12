namespace GuildWarsCompanion.Services;

/// <summary>
/// Whether making a thing beats buying it, and whether making it to sell is worth the bother.
///
/// Both are arithmetic the API supports completely — ingredient prices on one side, the item's
/// own price on the other — and both are questions the game never answers. Neither is an
/// opinion about what is good: the numbers say what they say.
/// </summary>
public static class CraftValue
{
    /// <summary>
    /// Cost of one, given what a batch costs and how many it yields.
    /// A recipe making five for the price of one is five times cheaper per item.
    /// </summary>
    public static long PerItem(long batchCost, int output) =>
        output <= 1 ? batchCost : batchCost / output;

    /// <summary>
    /// "Cheaper to buy" or "cheaper to make", with the gap.
    ///
    /// Null when there is nothing to compare — an item nobody sells cannot be bought, so
    /// making it is not cheaper, it is the only way.
    /// </summary>
    public static string? AgainstBuying(long batchCost, int output, long buyOutright)
    {
        if (buyOutright <= 0 || batchCost <= 0) return null;

        var make = PerItem(batchCost, output);
        var difference = Math.Abs(make - buyOutright);

        // Within a copper either way, saying one is cheaper is noise.
        if (difference == 0) return L.T("Making one costs the same as buying it.");

        return make < buyOutright
            ? L.F("Cheaper to make: {0} against {1} to buy, saving {2} each.",
                Coin.Format(make), Coin.Format(buyOutright), Coin.Format(difference))
            : L.F("Cheaper to buy: {0} against {1} to make.", Coin.Format(buyOutright), Coin.Format(make));
    }

    /// <summary>
    /// What making one to sell would leave you, after the trading post's cut.
    ///
    /// Sold into the standing buy order, because that is the price you can actually get today
    /// rather than one you might wait days for. A loss is reported as a loss — the whole point
    /// is to know before spending the materials.
    /// </summary>
    public static string? AsProfit(long batchCost, int output, long sellsInstantlyFor)
    {
        if (sellsInstantlyFor <= 0 || batchCost <= 0) return null;

        var make = PerItem(batchCost, output);
        var keep = PriceRules.AfterFees(sellsInstantlyFor);
        var margin = keep - make;

        return margin switch
        {
            > 0 => L.F("Making one to sell nets {0} after the trading post's cut.", Coin.Format(margin)),
            0 => L.T("Making one to sell breaks even after the trading post's cut."),
            _ => L.F("Making one to sell loses {0} after the trading post's cut.", Coin.Format(-margin))
        };
    }
}
