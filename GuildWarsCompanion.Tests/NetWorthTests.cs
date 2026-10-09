using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Adding an account up into one number.
///
/// A total is the easiest thing in the app to get quietly wrong: every mistake still produces
/// a plausible amount of gold, and nobody can check it by eye.
/// </summary>
public class NetWorthTests
{
    private static IReadOnlyList<WorthPart> Parts(
        long coin = 0, long stored = 0, long listed = 0,
        long buyOrders = 0, long deliveryCoin = 0, long deliveryItems = 0) =>
        NetWorth.Parts(coin, stored, listed, buyOrders, deliveryCoin, deliveryItems);

    [Fact]
    public void Everything_counts_once()
    {
        var parts = Parts(coin: 100, stored: 200, listed: 30, buyOrders: 40,
            deliveryCoin: 5, deliveryItems: 6);

        Assert.Equal(381, NetWorth.Total(parts));
    }

    [Fact]
    public void The_delivery_box_is_one_line_of_coin_and_goods()
    {
        // Two numbers from the same place, and splitting them would push the pickup box down
        // the list twice while saying less.
        var parts = Parts(deliveryCoin: 5, deliveryItems: 6);

        Assert.Equal(11, Assert.Single(parts).Value);
    }

    [Fact]
    public void The_largest_holding_leads()
    {
        // The only reason to break a single number apart is to answer "where is my money".
        var parts = Parts(coin: 10, stored: 500, listed: 90);

        Assert.Equal(["Items", "Listed", "Coin"], parts.Select(part => part.Name));
    }

    [Fact]
    public void Empty_parts_are_left_out_rather_than_shown_as_nothing()
    {
        // A player with no buy orders does not need a line telling them so.
        var parts = Parts(coin: 100);

        Assert.Equal("Coin", Assert.Single(parts).Name);
    }

    [Fact]
    public void An_account_with_nothing_totals_nothing()
    {
        Assert.Empty(Parts());
        Assert.Equal(0, NetWorth.Total(Parts()));
    }

    [Fact]
    public void Coin_in_buy_orders_is_still_yours()
    {
        // Cancelling a buy order refunds it in full, so leaving it out understates the
        // account by however much is committed to the market.
        Assert.Equal(40, NetWorth.Total(Parts(buyOrders: 40)));
    }

    [Fact]
    public void Shares_are_rounded_but_never_to_nothing()
    {
        // "0%" next to a real figure says less than nothing.
        Assert.Equal("50%", NetWorth.Share(50, 100));
        Assert.Equal("<1%", NetWorth.Share(1, 1000));
        Assert.Equal("1%", NetWorth.Share(10, 1000));
    }

    [Fact]
    public void A_share_of_nothing_is_not_a_number()
    {
        Assert.Equal(string.Empty, NetWorth.Share(0, 100));
        Assert.Equal(string.Empty, NetWorth.Share(50, 0));
    }

    [Fact]
    public void Without_prices_the_total_says_it_is_coin_only()
    {
        // Presenting a coin-only figure as an account's worth would be a straight lie on the
        // one screen where the number is the whole point.
        Assert.Contains("coin only", NetWorth.Caveat(priced: false, []));
    }

    [Fact]
    public void The_caveat_names_what_is_not_counted()
    {
        var note = NetWorth.Caveat(priced: true, []);

        Assert.Contains("after the 15% fee", note);
        Assert.Contains("karma and gems are not counted", note);
    }

    [Fact]
    public void A_place_that_could_not_be_read_is_admitted()
    {
        // Otherwise a missing permission silently shrinks the account, and the number looks
        // like a loss rather than a gap.
        var note = NetWorth.Caveat(priced: true, ["the bank"]);

        Assert.Contains("Could not read the bank", note);
    }
}
