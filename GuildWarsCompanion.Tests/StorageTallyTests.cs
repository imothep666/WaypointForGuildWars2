using System.Globalization;
using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Turning slot-by-slot readings into "where is my X?".
///
/// Every mistake this code can make is a quiet one: a total that is plausible but wrong, or
/// a location silently dropped. Nothing on screen would look broken.
/// </summary>
public class StorageTallyTests
{
    private const int Ectoplasm = 19721;

    private static Holding Materials(int count) =>
        new(StoragePlace.Materials, string.Empty, Ectoplasm, count);

    private static Holding Bank(int count) =>
        new(StoragePlace.Bank, string.Empty, Ectoplasm, count);

    private static Holding OnCharacter(string who, int count) =>
        new(StoragePlace.Character, who, Ectoplasm, count);

    [Fact]
    public void Stacks_in_the_same_place_are_summed()
    {
        // The bank reports one slot at a time, so a big pile arrives as several readings.
        var collated = StorageTally.Collate([Bank(250), Bank(250), Bank(12)]);

        Assert.Single(collated);
        Assert.Equal(512, collated[0].Count);
    }

    [Fact]
    public void Different_characters_stay_separate()
    {
        // "12 somewhere on your characters" is not an answer to where it is.
        var collated = StorageTally.Collate([OnCharacter("Imothep", 7), OnCharacter("Imosan", 7)]);

        Assert.Equal(2, collated.Count);

        // Equal piles fall back to name order, so the list cannot shuffle between refreshes.
        Assert.Equal(["Imosan", "Imothep"], collated.Select(h => h.Where));
    }

    [Fact]
    public void Different_items_are_never_merged()
    {
        var collated = StorageTally.Collate([
            Bank(3),
            new Holding(StoragePlace.Bank, string.Empty, 24295, 4)
        ]);

        Assert.Equal(2, collated.Count);
    }

    [Fact]
    public void Empty_slots_never_become_a_location()
    {
        // A present-but-empty reading must not turn into "0 in the bank".
        Assert.Empty(StorageTally.Collate([Bank(0), Bank(-1)]));
    }

    [Fact]
    public void Places_are_reported_in_a_fixed_order_regardless_of_arrival()
    {
        var collated = StorageTally.Collate([
            OnCharacter("Imothep", 12),
            Materials(250),
            new Holding(StoragePlace.Shared, string.Empty, Ectoplasm, 1),
            Bank(3)
        ]);

        Assert.Equal(
            [StoragePlace.Materials, StoragePlace.Bank, StoragePlace.Shared, StoragePlace.Character],
            collated.Select(h => h.Place));
    }

    [Fact]
    public void The_biggest_pile_comes_first_within_a_place()
    {
        var collated = StorageTally.Collate([OnCharacter("Small", 2), OnCharacter("Large", 40)]);

        Assert.Equal("Large", collated[0].Where);
    }

    // --- the sentence ------------------------------------------------------

    [Fact]
    public void One_place_reads_as_a_bare_phrase() =>
        Assert.Equal("250 in material storage", StorageTally.Describe([Materials(250)]));

    [Fact]
    public void Two_places_are_joined_with_and() =>
        Assert.Equal(
            "250 in material storage and 3 in the bank",
            StorageTally.Describe([Materials(250), Bank(3)]));

    [Fact]
    public void Three_places_read_as_a_sentence() =>
        Assert.Equal(
            "250 in material storage, 3 in the bank and 12 on Imothep",
            StorageTally.Describe([Materials(250), Bank(3), OnCharacter("Imothep", 12)]));

    [Fact]
    public void Large_counts_are_grouped_for_reading()
    {
        // The app formats numbers in the device's culture on purpose — a German phone should
        // read "1.250". So the separator is pinned under a stated culture rather than
        // asserted as a literal, which would fail on the developer's own machine.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal("1,250 in the bank", StorageTally.Describe([Bank(1250)]));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Nothing_held_says_so_rather_than_returning_an_empty_line()
    {
        // A blank caption would read as a rendering failure, not as an answer.
        Assert.Equal("None anywhere the app can see.", StorageTally.Describe([]));
        Assert.Equal("None anywhere the app can see.", StorageTally.Describe([Bank(0)]));
    }

    [Fact]
    public void Worn_gear_is_described_as_worn()
    {
        // Searching for something you are wearing must not answer "none anywhere".
        var equipped = new Holding(StoragePlace.Equipped, "Sweet Child O Night", 12345, 1);

        Assert.Equal("1 equipped on Sweet Child O Night", StorageTally.Describe([equipped]));
    }

    [Fact]
    public void Every_place_has_a_phrase_and_a_heading()
    {
        // A new StoragePlace member that nobody phrased would silently read "somewhere".
        foreach (var place in Enum.GetValues<StoragePlace>())
        {
            Assert.DoesNotContain("somewhere", StorageTally.PlaceName(place, "Imothep"));
            Assert.DoesNotContain("Elsewhere", StorageTally.PlaceHeading(place, "Imothep"));
        }
    }

    [Fact]
    public void Total_ignores_empty_readings() =>
        Assert.Equal(265, StorageTally.Total([Materials(250), Bank(3), OnCharacter("X", 12), Bank(0)]));
}

/// <summary>
/// The trading post's cut. Every valuation in the app passes through this, so an error here
/// is an error everywhere money is shown.
/// </summary>
public class TradingPostFeeTests
{
    [Theory]
    [InlineData(100, 85)]
    [InlineData(10_000, 8_500)]
    [InlineData(1, 0)]          // rounds down: the fee is never in the player's favour
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    public void A_sale_keeps_eighty_five_percent(long gross, long net) =>
        Assert.Equal(net, PriceRules.AfterFees(gross));

    [Fact]
    public void An_account_sized_valuation_does_not_overflow()
    {
        // 100,000 gold in copper: inside long, far outside int. A whole account's storage
        // valued at once reaches this range, and an int would have wrapped to nonsense.
        Assert.Equal(850_000_000L, PriceRules.AfterFees(1_000_000_000L));
    }
}
