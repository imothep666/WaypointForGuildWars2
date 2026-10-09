using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// How the Today screen reports a tracker it could not read.
///
/// These exist because of a real failure: a rate limit made Map bonus chests disappear from a
/// five-row list, and the screen said "4 of 4 unfinished" as though that were the whole
/// picture. Nothing looked wrong, which is exactly the problem.
/// </summary>
public class TodayRowsTests
{
    private static TodayRow Row(string name, int done, int total) =>
        new(name, done, total, null);

    private static TodayRow Unread(string name) =>
        new(name, 0, 0, null, Unread: true);

    // --- a tracker that failed stays on screen ------------------------------

    [Fact]
    public void An_unreadable_tracker_is_kept_rather_than_dropped()
    {
        var worth = TodaySummary.Worth([Row("World bosses", 0, 15), Unread("Map bonus chests")]);

        Assert.Equal(2, worth.Count);
    }

    [Fact]
    public void A_tracker_with_no_target_is_still_dropped()
    {
        // Total 0 and readable means the endpoint genuinely lists nothing — there is no row
        // to show. Only failure earns a placeholder.
        Assert.Single(TodaySummary.Worth([Row("Real", 0, 15), Row("Empty", 0, 0)]));
    }

    [Fact]
    public void An_unreadable_tracker_says_what_happened() =>
        Assert.Equal(
            "Could not be read just now — pull down to try again.",
            Unread("Map bonus chests").Caption);

    [Fact]
    public void An_unreadable_tracker_shows_no_progress()
    {
        // "0 / 0" would read as a real, finished tracker.
        var row = Unread("Map bonus chests");

        Assert.Equal("—", row.Progress);
        Assert.Equal(0, row.Fraction);
    }

    [Fact]
    public void An_unreadable_tracker_is_never_complete()
    {
        // Done >= Total is trivially true at 0/0, and would tick it off as finished.
        Assert.False(Unread("Map bonus chests").IsComplete);
    }

    [Fact]
    public void An_unreadable_tracker_is_quiet_rather_than_demanding()
    {
        // Brass means "go and do this". Unknown is not that.
        Assert.True(Unread("Map bonus chests").IsQuiet);
        Assert.True(Row("Done", 5, 5).IsQuiet);
        Assert.False(Row("Outstanding", 1, 5).IsQuiet);
    }

    // --- the summary ---------------------------------------------------------

    [Fact]
    public void The_summary_counts_only_what_could_be_read()
    {
        // The exact failure: four readable, one not. Saying "4 of 4" hides the fifth.
        var summary = TodaySummary.Of([
            Row("a", 0, 5), Row("b", 0, 5), Row("c", 0, 5), Row("d", 0, 5), Unread("e")
        ]);

        Assert.Equal("4 of 4 unfinished · 1 unread", summary);
    }

    [Fact]
    public void With_everything_readable_nothing_extra_is_said() =>
        Assert.Equal("2 of 2 unfinished", TodaySummary.Of([Row("a", 0, 5), Row("b", 1, 5)]));

    [Fact]
    public void Everything_finished_reads_as_all_clear() =>
        Assert.Equal("All clear", TodaySummary.Of([Row("a", 5, 5), Row("b", 5, 5)]));

    [Fact]
    public void All_clear_still_admits_what_it_could_not_see()
    {
        // "All clear" on its own would be a lie while a tracker is unread.
        Assert.Equal("All clear · 1 unread", TodaySummary.Of([Row("a", 5, 5), Unread("b")]));
    }

    [Fact]
    public void When_nothing_could_be_read_it_says_so_rather_than_all_clear()
    {
        Assert.Equal("Nothing could be read · 2 unread",
            TodaySummary.Of([Unread("a"), Unread("b")]));
    }

    [Fact]
    public void An_unread_tracker_is_never_counted_as_outstanding()
    {
        // Counting it as work to do would be a guess dressed up as a total.
        var summary = TodaySummary.Of([Row("done", 5, 5), Unread("unknown")]);

        Assert.DoesNotContain("1 of", summary);
    }
}
