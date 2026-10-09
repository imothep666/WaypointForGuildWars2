using System.Globalization;
using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Reading the gem exchange.
///
/// Everything here is a claim about someone's money, and the exchange is unusual in two ways
/// that make a plausible-looking wrong answer easy: it prices a trade rather than publishing a
/// rate, and it refuses a trade that is too small on a 200 rather than an error.
/// </summary>
public class GemExchangeTests
{
    [Fact]
    public void The_round_trip_is_what_comes_back_as_a_fraction_of_what_went_in()
    {
        // Measured on a real account: 7g 79s 70c bought 18 gems, and those 18 gems sold back
        // for 4g 77s 59c. The exchange keeps the difference and never shows it.
        Assert.Equal("61%", GemExchange.RoundTrip(77_970, 47_759));
    }

    [Fact]
    public void A_round_trip_needs_both_halves()
    {
        // A missing quote must read as nothing rather than as a total loss.
        Assert.Null(GemExchange.RoundTrip(77_970, 0));
        Assert.Null(GemExchange.RoundTrip(0, 47_759));
    }

    [Fact]
    public void A_trade_too_small_to_make_is_not_a_quote()
    {
        // The API answers "too few coins" with a 200 and no quantity. Read as a quote, that
        // is an offer of zero gems, which reads as a broken screen rather than a refusal.
        Assert.False(GemExchange.IsQuoted(0, "too few coins"));
        Assert.False(GemExchange.IsQuoted(0, null));
        Assert.True(GemExchange.IsQuoted(18, null));
    }

    [Fact]
    public void A_message_beside_a_quantity_still_counts_as_a_refusal()
    {
        // Not a shape the exchange has been seen to return, and deliberately guarded anyway:
        // a message means it is telling you something is wrong, and a number sitting next to
        // it is not worth betting someone's gold on.
        Assert.False(GemExchange.IsQuoted(18, "too few coins"));
    }

    [Fact]
    public void The_refusal_is_repeated_back_as_a_sentence()
    {
        Assert.Equal("Too few coins to convert.", GemExchange.TooSmall("too few coins"));
        Assert.Equal("Not enough coin to convert.", GemExchange.TooSmall(null));
    }

    [Fact]
    public void One_gem_is_not_pluralised()
    {
        Assert.Equal("1s 00c buys 1 gem", GemExchange.Buys(100, 1));
    }

    [Fact]
    public void The_trade_is_stated_as_the_exchange_priced_it()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        try
        {
            Assert.Equal("7g 79s 70c buys 18 gems", GemExchange.Buys(77_970, 18));
            Assert.Equal("18 gems sell back for 4g 77s 59c — 61% of what went in",
                GemExchange.SellsBack(18, 47_759, "61%"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Without_a_round_trip_figure_the_sale_is_stated_plainly()
    {
        Assert.Equal("18 gems sell for 4g 77s 59c", GemExchange.SellsBack(18, 47_759, null));
    }
}
