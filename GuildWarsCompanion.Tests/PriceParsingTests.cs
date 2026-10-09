
using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Typed prices and the threshold comparison. A watch that silently misreads "1g 20s" as
/// 120 copper would never fire, and the user would never learn why.
/// </summary>
public class PriceParsingTests
{
    [Theory]
    [InlineData("1g", 10_000)]
    [InlineData("20s", 2_000)]
    [InlineData("45c", 45)]
    [InlineData("1g20s", 12_000)]
    [InlineData("1g 20s", 12_000)]
    [InlineData("1g 20s 5c", 12_005)]
    [InlineData("2G 3S 4C", 20_304)]
    [InlineData("450", 450)]          // bare numbers are copper, as the API talks about money
    [InlineData("0", 0)]
    public void Reads_prices_the_way_players_write_them(string text, long expected) =>
        Assert.Equal(expected, PriceRules.ParsePrice(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("cheap")]
    [InlineData("1g20")]      // trailing digits with no unit: ambiguous, so refused
    [InlineData("g")]
    [InlineData("1x")]
    [InlineData("-5")]
    [InlineData("1.5g")]
    public void Refuses_what_it_cannot_read_rather_than_guessing(string? text) =>
        Assert.Null(PriceRules.ParsePrice(text));

    [Fact]
    public void Large_values_survive_the_multiplication()
    {
        // A legendary can run to five figures of gold; overflowing into a small number would
        // fire every watch at once.
        Assert.Equal(12_345 * 10_000L, PriceRules.ParsePrice("12345g"));
    }

    private static PriceWatch Watch(bool below, long threshold, bool sellSide = true) =>
        new(1, 19721, "Glob of Ectoplasm", null, sellSide, below, threshold, false);

    [Theory]
    [InlineData(2_000, true)]     // under the threshold
    [InlineData(2_500, true)]     // exactly on it counts
    [InlineData(2_501, false)]
    public void A_falling_watch_is_met_at_or_below_its_price(long current, bool met) =>
        Assert.Equal(met, PriceRules.IsMet(Watch(below: true, 2_500), current));

    [Theory]
    [InlineData(3_000, true)]
    [InlineData(2_500, true)]
    [InlineData(2_499, false)]
    public void A_rising_watch_is_met_at_or_above_its_price(long current, bool met) =>
        Assert.Equal(met, PriceRules.IsMet(Watch(below: false, 2_500), current));

    [Fact]
    public void An_absent_price_is_never_a_met_watch()
    {
        // Items that have never traded return nothing. Treating a missing price as zero would
        // fire every "below" watch the moment the market went quiet.
        Assert.False(PriceRules.IsMet(Watch(below: true, 2_500), null));
        Assert.False(PriceRules.IsMet(Watch(below: true, 2_500), 0));
    }

    [Fact]
    public void Describe_says_which_side_of_the_market_it_watches()
    {
        Assert.Equal(
            "Sell price at or below 25s 00c",
            PriceRules.Describe(Watch(below: true, 2_500)));

        Assert.Equal(
            "Buy price at or above 25s 00c",
            PriceRules.Describe(Watch(below: false, 2_500, sellSide: false)));
    }
}
