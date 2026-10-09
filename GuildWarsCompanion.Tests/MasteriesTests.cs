using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Mastery progress.
///
/// Two ways to be quietly wrong, both of which produce a screen that looks entirely reasonable:
/// reading the account's 0-indexed level as a count, which overstates nothing and understates
/// every trained track by one; and treating points as one pool, when they are earned and spent
/// per region and cannot cross.
/// </summary>
public class MasteriesTests
{
    private static (int, string, string?, IReadOnlyList<(string, int)>) Track(
        int id, string name, string region, params int[] costs) =>
        (id, name, region, costs.Select((cost, i) => ($"Level {i + 1}", cost)).ToList());

    [Fact]
    public void The_accounts_level_is_a_position_not_a_count()
    {
        // Documented on the endpoint: a 0-indexed reference into the track's levels. A track
        // reported at 4 has five levels done.
        Assert.Equal(5, Masteries.LevelsDone(4));
        Assert.Equal(1, Masteries.LevelsDone(0));
    }

    [Fact]
    public void An_untouched_track_has_nothing_done()
    {
        // Untrained tracks are absent from the account list rather than reported as level 0.
        Assert.Equal(0, Masteries.LevelsDone(null));
    }

    [Fact]
    public void The_next_level_is_the_one_after_what_is_done()
    {
        var steps = Masteries.Plan(
            [Track(1, "Gliding", "Maguuma", 1, 2, 3)],
            new Dictionary<int, int> { [1] = 0 },   // level 0 = one level done
            new Dictionary<string, int> { ["Heart of Thorns"] = 5 });

        var step = Assert.Single(steps);

        Assert.Equal(1, step.Done);
        Assert.Equal("Level 2", step.NextLevel);
        Assert.Equal(2, step.NextCost);
    }

    [Fact]
    public void A_finished_track_has_no_next_level()
    {
        var steps = Masteries.Plan(
            [Track(1, "Gliding", "Maguuma", 1, 2)],
            new Dictionary<int, int> { [1] = 1 },   // level 1 = both done
            new Dictionary<string, int> { ["Heart of Thorns"] = 5 });

        var step = Assert.Single(steps);

        Assert.True(step.IsComplete);
        Assert.Null(step.NextLevel);
        Assert.Equal("Trained", Masteries.Describe(step));
    }

    [Fact]
    public void A_level_beyond_the_tracks_length_does_not_run_off_the_end()
    {
        // A track shortened by a game update against an account that had finished it.
        var steps = Masteries.Plan(
            [Track(1, "Gliding", "Maguuma", 1)],
            new Dictionary<int, int> { [1] = 7 },
            new Dictionary<string, int> { ["Heart of Thorns"] = 5 });

        var step = Assert.Single(steps);

        Assert.True(step.IsComplete);

        // And it has to read as finished rather than as "8 of 1", which is what an unclamped
        // count would put on the screen.
        Assert.Equal("1 of 1", step.ProgressText);
    }

    [Fact]
    public void Points_are_only_spendable_in_their_own_region()
    {
        // The whole reason a single account-wide total says nothing: nine Central Tyria points
        // train no part of a Heart of Thorns track.
        var steps = Masteries.Plan(
            [Track(1, "Exalted Lore", "Maguuma", 1)],
            new Dictionary<int, int>(),
            new Dictionary<string, int> { ["Central Tyria"] = 9 });

        var step = Assert.Single(steps);

        Assert.False(step.Affordable);
        Assert.Contains("not enough in Heart of Thorns", Masteries.Describe(step));
    }

    [Fact]
    public void The_two_endpoints_name_the_same_region_differently()
    {
        // Tracks name a region by the map; the points totals name it by the expansion that
        // sells it. Matching the strings directly fails on every region at once, which reads
        // as an account that cannot afford anything anywhere — which is how this was found.
        Assert.Equal("Central Tyria", Masteries.PointsRegion("Tyria"));
        Assert.Equal("Path of Fire", Masteries.PointsRegion("Desert"));
        Assert.Equal("End of Dragons", Masteries.PointsRegion("Jade"));
        Assert.Equal("Janthir Wilds", Masteries.PointsRegion("Wild"));
    }

    [Fact]
    public void A_region_this_build_has_not_heard_of_keeps_its_own_name()
    {
        // If the two endpoints ever agree on a new one, that alone makes it work.
        Assert.Equal("Abyss", Masteries.PointsRegion("Abyss"));
        Assert.Equal("Unknown", Masteries.PointsRegion(null));
    }

    [Fact]
    public void A_track_is_afforded_from_its_own_regions_pool()
    {
        // The case that was broken on the phone: Central Tyria points against a Tyria track.
        var steps = Masteries.Plan(
            [Track(1, "Pact Commander", "Tyria", 1)],
            new Dictionary<int, int>(),
            new Dictionary<string, int> { ["Central Tyria"] = 6 });

        Assert.True(Assert.Single(steps).Affordable);
    }

    [Fact]
    public void What_can_be_trained_today_leads()
    {
        var steps = Masteries.Plan(
        [
            Track(1, "Costly", "Tyria", 9),
            Track(2, "Cheap", "Tyria", 1)
        ],
            new Dictionary<int, int>(),
            new Dictionary<string, int> { ["Central Tyria"] = 2 });

        Assert.Equal(["Cheap", "Costly"], steps.Select(s => s.Name));
        Assert.True(steps[0].Affordable);
    }

    [Fact]
    public void A_finished_track_sinks_below_an_unaffordable_one()
    {
        // A record, not a plan.
        var steps = Masteries.Plan(
        [
            Track(1, "Done", "Tyria", 1),
            Track(2, "Out of reach", "Tyria", 9)
        ],
            new Dictionary<int, int> { [1] = 0 },
            new Dictionary<string, int> { ["Central Tyria"] = 2 });

        Assert.Equal(["Out of reach", "Done"], steps.Select(s => s.Name));
    }

    [Fact]
    public void A_track_with_no_levels_is_not_a_track()
    {
        Assert.Empty(Masteries.Plan(
            [(1, "Empty", "Tyria", new List<(string, int)>())],
            new Dictionary<int, int>(),
            new Dictionary<string, int>()));
    }

    [Fact]
    public void What_is_left_to_spend_is_earned_minus_spent()
    {
        // The account this was built against: nine earned, three spent.
        var region = new RegionPoints("Central Tyria", Earned: 9, Spent: 3);

        Assert.Equal(6, region.Available);
        Assert.Equal("6 to spend · 9 earned", region.Line);
    }

    [Fact]
    public void Spending_more_than_was_earned_is_not_a_negative_pool()
    {
        Assert.Equal(0, new RegionPoints("Central Tyria", 3, 9).Available);
    }

    [Fact]
    public void The_heading_counts_what_is_waiting_across_every_region()
    {
        var regions = new[]
        {
            new RegionPoints("Central Tyria", 9, 3),
            new RegionPoints("Maguuma", 4, 4)
        };

        Assert.Equal("6 mastery points waiting to be spent.", Masteries.Summarise(regions));
        Assert.Equal("No mastery points waiting to be spent.",
            Masteries.Summarise([new RegionPoints("Central Tyria", 3, 3)]));
    }
}
