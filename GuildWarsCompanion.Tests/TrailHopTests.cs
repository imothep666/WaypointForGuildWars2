using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Which waypoint to port to when a trail leads to the objective.
///
/// The fixture is TrailGuideTests' L around a wall: east 300 from the origin, then north 300 to
/// the objective at its end. A waypoint behind the wall is the nearest to the objective in a
/// straight line and the wrong place to arrive, because the trail is not there.
/// </summary>
public class TrailHopTests
{
    private const int Map = 1015;

    private static readonly RouteLine Around =
        new(Map, "legs.map.barefoot", "Around the wall",
            [(0, 0), (100, 0), (200, 0), (300, 0), (300, -100), (300, -200), (300, -300)]);

    private static readonly CompassTarget Chamber =
        new("Hidden chamber", TargetKind.Landmark, Map, "Silverwastes", 305, -300);

    private static CompassTarget Waypoint(string name, double x, double y, int map = Map) =>
        new(name, TargetKind.Waypoint, map, "Silverwastes", x, y);

    private static TrailGuide Guide() => TrailGuide.Find([Around], Chamber.X, Chamber.Y)!;

    /// <summary>Inside the L, 206 from the objective, but 206 from the trail too and with a wall
    /// in the way.</summary>
    private static readonly CompassTarget BehindTheWall = Waypoint("Behind the Wall Waypoint", 100, -250);

    /// <summary>Twice as far from the objective in a straight line, and twenty from the trail's start.</summary>
    private static readonly CompassTarget AtTheStart = Waypoint("Trailhead Waypoint", 0, -20);

    [Fact]
    public void The_waypoint_nearest_the_trail_beats_the_one_nearest_the_objective()
    {
        // The straight line's choice, for contrast.
        Assert.Same(BehindTheWall, PortRoute.To(Chamber, [BehindTheWall, AtTheStart])!.Waypoint);

        var hop = PortRoute.ToTrail(Guide(), Chamber, [BehindTheWall, AtTheStart]);

        Assert.NotNull(hop);
        Assert.Same(AtTheStart, hop.Waypoint);
        Assert.Equal(20, hop.ToTrail, 0.001);
        Assert.Equal(600, hop.Along, 0.001);
    }

    [Fact]
    public void Only_waypoints_on_the_trails_map_count()
    {
        var elsewhere = Waypoint("Other Map Waypoint", 300, -300, map: 15);

        Assert.Null(PortRoute.ToTrail(Guide(), Chamber, [elsewhere]));
    }

    [Fact]
    public void An_objective_that_is_a_waypoint_needs_no_advice()
    {
        var waypoint = Waypoint("Chamber Waypoint", 305, -300);

        Assert.Null(PortRoute.ToTrail(Guide(), waypoint, [AtTheStart]));
    }

    /// <summary>
    /// The case it was asked for: 925 to the trail in a straight line, from Camp Resolve towards
    /// Charnel Grounds. Under the porting threshold at face value, but a straight line across ground
    /// nobody walked is not a distance that can be taken at its word.
    /// </summary>
    [Fact]
    public void A_long_straight_line_onto_the_trail_is_worth_porting_to_avoid()
    {
        var hop = new TrailHop(AtTheStart, 20, 600);

        Assert.True(hop.BeatsWalking(new TrailLeg(0, 0, Along: 500, OffTrail: 925, Climb: null, At: 0)));
    }

    [Fact]
    public void A_short_walk_along_the_trail_is_not()
    {
        var hop = new TrailHop(AtTheStart, 0, 100);

        Assert.False(hop.BeatsWalking(new TrailLeg(0, 0, Along: 1900, OffTrail: 0, Climb: null, At: 0)));
        Assert.True(hop.BeatsWalking(new TrailLeg(0, 0, Along: 2000, OffTrail: 0, Climb: null, At: 0)));
    }

    [Fact]
    public void Standing_at_the_waypoint_suggests_nothing()
    {
        var hop = new TrailHop(AtTheStart, 20, 600);

        Assert.False(hop.BeatsWalking(new TrailLeg(0, 0, Along: 600, OffTrail: 20, Climb: null, At: 0)));
    }

    [Fact]
    public void The_line_says_how_far_to_the_trail_and_along_it()
    {
        Assert.Equal("via Trailhead Waypoint · then 120 to the trail, 600 along it",
            PortRoute.Line(new TrailHop(AtTheStart, 120.4, 600.2)));
    }

    /// <summary>A waypoint as near the trail as a character counts as on it: no "20 to the trail".</summary>
    [Fact]
    public void A_waypoint_on_the_trail_says_only_how_far_along()
    {
        Assert.Equal("via Trailhead Waypoint · then 600 along the trail",
            PortRoute.Line(new TrailHop(AtTheStart, 20, 600)));
    }
}
