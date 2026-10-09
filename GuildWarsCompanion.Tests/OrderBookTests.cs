using System.Globalization;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Where an open order stands in the queue.
///
/// Everything here is advice about money, and every mistake it can make is quiet: a queue
/// counted from the wrong side reads as reassurance, and a tie that forgets to exclude your
/// own listing invents competition that is not there.
/// </summary>
public class OrderBookTests
{
    /// <summary>
    /// Runs a check with the separator pinned. This machine is German, where a thousand
    /// prints as "1.000", so an unpinned assertion passes here and fails everywhere else.
    /// </summary>
    private static void Neutrally(Action check)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        try
        {
            check();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>An order book, cheapest ask first, as the API returns it.</summary>
    private static ListingTier[] Book(params (long Price, int Quantity)[] steps) =>
        steps.Select(step => new ListingTier(1, step.Price, step.Quantity)).ToArray();

    [Fact]
    public void Selling_counts_only_what_is_cheaper_than_you()
    {
        var book = Book((100, 5), (110, 20), (120, 40));

        Assert.Equal(25, OrderBook.Ahead(book, 120, selling: true));
    }

    [Fact]
    public void Buying_counts_what_offers_more_than_you()
    {
        // The mirror. Getting this backwards would tell a buyer at the back of the queue
        // that nobody is ahead of them.
        var book = Book((120, 40), (110, 20), (100, 5));

        Assert.Equal(60, OrderBook.Ahead(book, 100, selling: false));
    }

    [Fact]
    public void The_best_price_has_nothing_ahead_of_it()
    {
        var book = Book((100, 5), (110, 20));

        Assert.Equal(0, OrderBook.Ahead(book, 100, selling: true));
    }

    [Fact]
    public void Your_own_price_is_a_tie_rather_than_a_place_in_the_queue()
    {
        // Same-price orders fill oldest first and the API never says when the others were
        // placed. Counting them as ahead would be a guess dressed as a number.
        var book = Book((100, 5), (110, 200));

        Assert.Equal(5, OrderBook.Ahead(book, 110, selling: true));
    }

    [Fact]
    public void Your_own_listing_comes_out_of_the_tie()
    {
        // Your 40 are part of the 200 sitting at that price. Left in, an order with no
        // competition at all reports forty rivals.
        var book = Book((100, 5), (110, 200));

        Assert.Equal(160, OrderBook.Tied(book, 110, mine: 40));
    }

    [Fact]
    public void An_order_alone_at_its_price_has_no_ties()
    {
        var book = Book((100, 5), (110, 40));

        Assert.Equal(0, OrderBook.Tied(book, 110, mine: 40));
    }

    [Fact]
    public void A_book_that_lags_behind_your_listing_never_reports_negative_ties()
    {
        // Commerce responses are cached for five minutes, so the book can be older than the
        // order. A stale tier smaller than your own quantity must not read as -30 rivals.
        var book = Book((110, 10));

        Assert.Equal(0, OrderBook.Tied(book, 110, mine: 40));
    }

    [Fact]
    public void Being_undercut_is_someone_asking_less_than_you()
    {
        Assert.True(OrderBook.IsBeaten(120, best: 100, selling: true));
        Assert.False(OrderBook.IsBeaten(100, best: 100, selling: true));
        Assert.False(OrderBook.IsBeaten(100, best: 120, selling: true));
    }

    [Fact]
    public void Being_outbid_is_someone_offering_more_than_you()
    {
        Assert.True(OrderBook.IsBeaten(100, best: 120, selling: false));
        Assert.False(OrderBook.IsBeaten(120, best: 100, selling: false));
    }

    [Fact]
    public void An_unknown_market_beats_nothing()
    {
        // No price is not a low price. Offline, every order would otherwise read as fine.
        Assert.False(OrderBook.IsBeaten(100, best: null, selling: true));
        Assert.Null(OrderBook.Describe(true, 100, best: null));
    }

    [Fact]
    public void The_note_says_how_far_behind_and_how_long_the_queue_is()
    {
        Neutrally(() => Assert.Equal(
            "undercut by 50c · 8,000 ahead of you",
            OrderBook.Describe(selling: true, price: 1_200, best: 1_150, ahead: 8_000)));
    }

    [Fact]
    public void A_queue_that_was_never_fetched_is_left_unsaid()
    {
        // Depth costs a request per item, so most rows have none. Printing "0 ahead of you"
        // there would be a confident answer to a question nobody asked.
        var note = OrderBook.Describe(selling: true, price: 1_200, best: 1_150);

        Assert.Equal("undercut by 50c", note);
    }

    [Fact]
    public void Sitting_at_the_best_price_says_who_else_is_there()
    {
        // The answer to "why has this not sold when I am the cheapest".
        Neutrally(() => Assert.Equal("best ask · 2,036 more at your price",
            OrderBook.Describe(true, 1_000, 1_000, ahead: 0, tied: 2_036)));

        Assert.Equal("best ask · yours alone",
            OrderBook.Describe(true, 1_000, 1_000, ahead: 0, tied: 0));
    }

    [Fact]
    public void The_buying_side_is_worded_as_offers()
    {
        Assert.Equal("outbid by 1s 00c", OrderBook.Describe(false, 1_000, 1_100));
        Assert.Equal("best offer", OrderBook.Describe(false, 1_000, 1_000));
    }

    [Fact]
    public void The_heading_leads_with_what_needs_attention()
    {
        Assert.Equal("7 · 1g 20s 00c · 3 undercut", OrderBook.Summarise(7, 12_000, 3, selling: true));
        Assert.Equal("7 · 1g 20s 00c", OrderBook.Summarise(7, 12_000, 0, selling: true));
        Assert.Equal("2 · 50c · 1 outbid", OrderBook.Summarise(2, 50, 1, selling: false));
    }

    [Fact]
    public void A_rationed_screen_admits_the_ration()
    {
        // Order books are heavy enough that only the largest orders get one. A screen that
        // quietly checked half the list would read as though it had checked all of it.
        Assert.Equal("40 · 50c · 30 undercut · queue shown for the largest orders only",
            OrderBook.Summarise(40, 50, 30, selling: true, capped: true));
    }
}
