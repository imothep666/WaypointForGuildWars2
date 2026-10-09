using GuildWarsCompanion.Bridge.Protocol;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>The stretch of trail the module draws: from the character, a short way on.</summary>
public class TrailAheadTests
{
    // A straight road east, a point every thirty metres, then a turn north.
    private static readonly (double X, double Up, double Z)[] Road =
        [(0, 0, 0), (30, 0, 0), (60, 0, 0), (90, 0, 0), (90, 0, 30), (90, 0, 60)];

    private static List<(double X, double Up, double Z)> Cut((double X, double Up, double Z) at, double metres, out bool more)
    {
        var into = new List<(double X, double Up, double Z)>();
        more = TrailAhead.Cut(Road, at, metres, into);
        return into;
    }

    [Fact]
    public void It_starts_underfoot_and_stops_the_distance_on()
    {
        var ahead = Cut((10, 0, 0), 35, out var more);

        Assert.True(more);
        Assert.Equal([(10.0, 0.0, 0.0), (30.0, 0.0, 0.0), (45.0, 0.0, 0.0)], ahead);
    }

    [Fact]
    public void Beside_the_trail_it_starts_on_the_trail()
    {
        var ahead = Cut((40, 5, 12), 20, out _);

        Assert.Equal((40.0, 0.0, 0.0), ahead[0]);
        Assert.Equal((60.0, 0.0, 0.0), ahead[^1]);
    }

    [Fact]
    public void The_distance_is_walked_round_corners_not_across_them()
    {
        var ahead = Cut((80, 0, 0), 25, out _);

        Assert.Equal([(80.0, 0.0, 0.0), (90.0, 0.0, 0.0), (90.0, 0.0, 15.0)], ahead);
    }

    [Fact]
    public void Near_the_end_it_runs_to_the_end_and_says_there_is_no_more()
    {
        var ahead = Cut((90, 0, 20), 75, out var more);

        Assert.False(more);
        Assert.Equal([(90.0, 0.0, 20.0), (90.0, 0.0, 30.0), (90.0, 0.0, 60.0)], ahead);
    }

    [Fact]
    public void Where_the_trail_passes_itself_the_character_is_further_along()
    {
        // Out east and back the same way: every point is on both legs.
        var thereAndBack = new (double X, double Up, double Z)[] { (0, 0, 0), (100, 0, 0), (0, 0, 0) };
        var into = new List<(double X, double Up, double Z)>();

        TrailAhead.Cut(thereAndBack, (50, 0, 0), 10, into);

        Assert.Equal([(50.0, 0.0, 0.0), (40.0, 0.0, 0.0)], into);
    }

    [Fact]
    public void It_says_how_far_along_the_whole_trail_it_starts()
    {
        var into = new List<(double X, double Up, double Z)>();

        TrailAhead.Cut(Road, (90, 0, 15), 0, 10, into, out var start);

        Assert.Equal(105, start, 6);
    }

    [Fact]
    public void A_skip_leaves_the_ground_underfoot_clear_and_starts_further_on()
    {
        var into = new List<(double X, double Up, double Z)>();

        // Twenty-five on from 80 is round the corner: 10 to it, 15 up the next leg.
        var more = TrailAhead.Cut(Road, (80, 0, 0), 25, 15, into, out var start);

        Assert.True(more);
        Assert.Equal(105, start, 6);
        Assert.Equal([(90.0, 0.0, 15.0), (90.0, 0.0, 30.0)], into);
    }

    [Fact]
    public void A_skip_past_the_end_leaves_only_the_end()
    {
        var into = new List<(double X, double Up, double Z)>();

        Assert.False(TrailAhead.Cut(Road, (90, 0, 50), 30, 75, into, out _));
        Assert.Equal([(90.0, 0.0, 60.0)], into);
    }
}
