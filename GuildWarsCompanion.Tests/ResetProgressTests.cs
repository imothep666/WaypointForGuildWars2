using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The rule every reset tracker on the Today screen counts by.
///
/// Three of the five trackers compare two lists that use the same ids. Raids and dungeons do
/// not, and that mismatch is the reason this is a thing of its own.
/// </summary>
public class ResetProgressTests
{
    [Fact]
    public void Nothing_done_leaves_everything_remaining()
    {
        var count = ResetProgress.Of(["vale_guardian", "gorseval", "sabetha"], []);

        Assert.Equal(0, count.Done);
        Assert.Equal(3, count.Total);
        Assert.Equal(["Vale Guardian", "Gorseval", "Sabetha"], count.Remaining);
    }

    [Fact]
    public void Everything_done_leaves_nothing_remaining()
    {
        var count = ResetProgress.Of(["vale_guardian", "gorseval"], ["vale_guardian", "gorseval"]);

        Assert.Equal(2, count.Done);
        Assert.Equal(2, count.Total);
        Assert.Empty(count.Remaining);
    }

    [Fact]
    public void Completions_outside_the_tracked_set_are_ignored()
    {
        // The real case this exists for: /v2/account/raids reports checkpoints (Spirit Woods,
        // Twisted Castle, Gate, Camp) alongside encounters, but only bosses are counted.
        // Trusting the completed list's length would report 3 of 2 bosses killed.
        var count = ResetProgress.Of(
            ["vale_guardian", "gorseval"],
            ["vale_guardian", "spirit_woods", "twisted_castle"]);

        Assert.Equal(1, count.Done);
        Assert.Equal(2, count.Total);
        Assert.Equal(["Gorseval"], count.Remaining);
    }

    [Fact]
    public void A_completion_can_never_push_progress_past_the_total()
    {
        // Whatever the account endpoint says, the denominator is what is being tracked.
        var count = ResetProgress.Of(["a"], ["a", "b", "c", "d", "e"]);

        Assert.Equal(1, count.Done);
        Assert.Equal(1, count.Total);
    }

    [Fact]
    public void A_repeated_id_is_one_thing_to_do()
    {
        // Dungeon paths are flattened out of eight separate dungeons; a duplicate would
        // quietly raise the denominator and make a finished day read as unfinished.
        var count = ResetProgress.Of(["hodgins", "hodgins", "detha"], ["hodgins"]);

        Assert.Equal(1, count.Done);
        Assert.Equal(2, count.Total);
        Assert.Equal(["Detha"], count.Remaining);
    }

    [Fact]
    public void Remaining_keeps_the_order_the_api_gave()
    {
        // The API lists wings in the order the game presents them, which is the order a
        // player expects to read them in.
        var count = ResetProgress.Of(["sabetha", "vale_guardian", "gorseval"], []);

        Assert.Equal(["Sabetha", "Vale Guardian", "Gorseval"], count.Remaining);
    }

    [Fact]
    public void An_empty_tracker_is_not_a_finished_one()
    {
        // The screen drops rows with Total 0 — a failed fetch must land here rather than
        // presenting itself as "all clear".
        var count = ResetProgress.Of([], []);

        Assert.Equal(0, count.Total);
        Assert.Empty(count.Remaining);
    }

    [Fact]
    public void A_caller_can_name_things_its_own_way()
    {
        // Dungeons show the dungeon's name rather than the path id, because "Ac Story" is
        // not something a player would recognise.
        var count = ResetProgress.Of(["ac_story"], [], _ => "Ascalonian Catacombs");

        Assert.Equal(["Ascalonian Catacombs"], count.Remaining);
    }
}
