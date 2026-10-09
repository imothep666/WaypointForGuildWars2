using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Coin arithmetic. These are the boundaries that fail quietly: an off-by-one in the divisors
/// still prints something that looks like money.
/// </summary>
public class CoinTests
{
    [Theory]
    [InlineData(0, "0c")]
    [InlineData(1, "1c")]
    [InlineData(99, "99c")]
    [InlineData(100, "1s 00c")]
    [InlineData(101, "1s 01c")]
    [InlineData(9_999, "99s 99c")]
    [InlineData(10_000, "1g 00s 00c")]
    [InlineData(10_001, "1g 00s 01c")]
    [InlineData(123_456, "12g 34s 56c")]
    public void Format_writes_copper_the_way_the_game_does(long copper, string expected) =>
        Assert.Equal(expected, Coin.Format(copper));

    [Fact]
    public void Format_omits_leading_units_but_pads_trailing_ones()
    {
        // 45 copper is "45c", never "0g 00s 45c" — but once gold appears, the rest pads so
        // that a column of prices lines up.
        Assert.Equal("45c", Coin.Format(45));
        Assert.Equal("2s 05c", Coin.Format(205));
        Assert.Equal("3g 00s 07c", Coin.Format(30_007));
    }

    [Fact]
    public void Format_groups_thousands_of_gold()
    {
        // A wallet can hold five figures of gold; an ungrouped number is unreadable.
        var formatted = Coin.Format(12_345 * 10_000);
        Assert.StartsWith("12", formatted);
        Assert.Contains("g", formatted);
        Assert.Contains("345", formatted.Replace(".", string.Empty).Replace(",", string.Empty));
    }

    [Fact]
    public void FormatAmount_treats_only_coin_as_money()
    {
        Assert.Equal("1g 00s 00c", Coin.FormatAmount(Coin.CurrencyId, 10_000));

        // Karma, and everything else, is a count and must not be divided into metals.
        Assert.Equal("10.000", Coin.FormatAmount(2, 10_000).Replace(",", "."));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(10_000)]
    [InlineData(987_654_321)]
    public void Purse_splits_without_losing_or_inventing_copper(long copper)
    {
        var purse = new CoinPurse(copper);

        var gold = long.Parse(purse.Gold.Replace(".", string.Empty).Replace(",", string.Empty));
        var recombined = gold * 10_000 + long.Parse(purse.Silver) * 100 + long.Parse(purse.Copper);

        Assert.Equal(copper, recombined);
    }

    [Fact]
    public void Purse_pads_silver_and_copper_to_two_digits()
    {
        // These are rendered as separate labels beside a large gold figure; unpadded values
        // make "1g 5s 7c" read as 1g 50s 70c at a glance.
        var purse = new CoinPurse(10_507);

        Assert.Equal("1", purse.Gold);
        Assert.Equal("05", purse.Silver);
        Assert.Equal("07", purse.Copper);
    }
}
