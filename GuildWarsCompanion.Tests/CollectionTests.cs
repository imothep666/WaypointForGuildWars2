using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Reading a collection's checklist.
///
/// The account reports progress as indices into the achievement's bit array rather than as
/// ids. Confusing the two produces a checklist that looks right and ticks off the wrong
/// entries — the sort of wrong that sends someone hunting for a skin they already own.
/// </summary>
public class CollectionTests
{
    [Fact]
    public void Completion_is_by_position_not_by_id()
    {
        // Bits 0 and 2 are done. Nothing here is an item id.
        var list = Collection.Checklist(4, [0, 2]);

        Assert.True(list.Single(e => e.Index == 0).Done);
        Assert.False(list.Single(e => e.Index == 1).Done);
        Assert.True(list.Single(e => e.Index == 2).Done);
        Assert.False(list.Single(e => e.Index == 3).Done);
    }

    [Fact]
    public void What_is_still_needed_comes_first()
    {
        // The reason to open the screen is what is missing.
        var list = Collection.Checklist(4, [0, 1]);

        Assert.Equal([2, 3, 0, 1], list.Select(e => e.Index));
    }

    [Fact]
    public void Within_a_group_the_games_own_order_is_kept()
    {
        var list = Collection.Checklist(5, [1]);

        Assert.Equal([0, 2, 3, 4, 1], list.Select(e => e.Index));
    }

    [Fact]
    public void Nothing_collected_yet_lists_everything()
    {
        Assert.Equal(3, Collection.Outstanding(3, null));
        Assert.Equal(3, Collection.Outstanding(3, []));
    }

    [Fact]
    public void A_finished_collection_has_nothing_outstanding() =>
        Assert.Equal(0, Collection.Outstanding(3, [0, 1, 2]));

    [Fact]
    public void An_index_beyond_the_collection_is_ignored()
    {
        // Defensive: a retuned achievement can leave stale indices on the account. Index 0 is
        // real and counts; 7 and 9 point past the end and must neither crash nor tick
        // anything off.
        Assert.Equal(1, Collection.Outstanding(2, [0, 7, 9]));
        Assert.Single(Collection.Checklist(2, [0, 7]), entry => entry.Done);
    }

    [Fact]
    public void An_achievement_with_no_bits_has_no_checklist() =>
        Assert.Empty(Collection.Checklist(0, [0, 1]));

    [Fact]
    public void The_summary_counts_what_is_held() =>
        Assert.Equal("3 of 7 collected", Collection.Summarise(7, [0, 1, 2]));

    [Fact]
    public void The_summary_of_an_untouched_collection_is_zero() =>
        Assert.Equal("0 of 7 collected", Collection.Summarise(7, null));

    [Fact]
    public void Sifted_entries_are_counted_as_they_are_shown()
    {
        // Maguuma Explorer's shape: 170 entries in the array, three of them blank, and an
        // achievement that only asks for 167. Once the blanks are dropped the screen's total
        // has to follow them out, or it contradicts the row it came from.
        // Deliberately lopsided: with equal numbers done and outstanding, counting the wrong
        // side of the list reads exactly the same and the test proves nothing.
        var entries = Collection.Checklist(6, [0, 2, 4])
            .Where(entry => entry.Index != 4)
            .ToList();

        Assert.Equal("2 of 5 collected", Collection.Summarise(entries));
    }

    [Fact]
    public void Dropping_blanks_before_matching_would_tick_the_wrong_entries()
    {
        // The guard for the whole ordering: the account's indices count the blanks, so
        // sifting first shifts every later entry up one and ticks its neighbour instead.
        var done = new[] { 3 };

        var matchedFirst = Collection.Checklist(5, done)
            .Where(entry => entry.Index != 1)
            .Single(entry => entry.Done);

        Assert.Equal(3, matchedFirst.Index);
    }
}
