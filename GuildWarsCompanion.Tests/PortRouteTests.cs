using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Choosing the waypoint to port to, and deciding when saying so is advice rather than noise.
///
/// The arithmetic is one distance; what is worth pinning is every case where the honest answer
/// is to say nothing at all, because each of those is a line the screen would otherwise show
/// that reads as true and is not.
/// </summary>
public class PortRouteTests
{
    private const int Map = 15;

    private static CompassTarget Waypoint(string name, double x, double y) =>
        new(name, TargetKind.Waypoint, Map, "Queensdale", x, y);

    private static CompassTarget Heart(double x, double y) =>
        new("Help the farmers", TargetKind.Heart, Map, "Queensdale", x, y);

    [Fact]
    public void Picks_the_waypoint_nearest_the_objective()
    {
        var hop = PortRoute.To(
            Heart(1000, 1000),
            [Waypoint("Far", 0, 0), Waypoint("Near", 1100, 1000), Waypoint("Middling", 500, 500)]);

        Assert.NotNull(hop);
        Assert.Equal("Near", hop.Waypoint.Name);
        Assert.Equal(100, hop.Walk, 6);
    }

    /// <summary>
    /// Nearest to the objective, not to the character. Porting costs the same loading screen
    /// from anywhere, so where the character happens to stand cannot change the answer — and a
    /// route computed from their position would change it every step they took.
    /// </summary>
    [Fact]
    public void Does_not_depend_on_where_the_character_is()
    {
        CompassTarget[] waypoints = [Waypoint("Far", 0, 0), Waypoint("Near", 1100, 1000)];

        var hop = PortRoute.To(Heart(1000, 1000), waypoints);

        Assert.Equal("Near", hop!.Waypoint.Name);
    }

    [Fact]
    public void Ignores_everything_that_is_not_a_waypoint()
    {
        var hop = PortRoute.To(
            Heart(1000, 1000),
            [
                new("Vista", TargetKind.Vista, Map, "Queensdale", 1000, 1000),
                Waypoint("Only one", 1500, 1000)
            ]);

        Assert.Equal("Only one", hop!.Waypoint.Name);
    }

    /// <summary>A waypoint on the map under the character says nothing about a walk that starts
    /// on another map.</summary>
    [Fact]
    public void Ignores_waypoints_on_another_map()
    {
        var hop = PortRoute.To(
            Heart(1000, 1000),
            [new("Elsewhere", TargetKind.Waypoint, 28, "Wayfarer Foothills", 1000, 1000)]);

        Assert.Null(hop);
    }

    [Fact]
    public void Says_nothing_about_a_waypoint_objective()
    {
        var hop = PortRoute.To(Waypoint("Chosen", 1000, 1000), [Waypoint("Other", 1100, 1000)]);

        Assert.Null(hop);
    }

    /// <summary>
    /// A world boss is placed at the middle of its map, and the nearest waypoint to a map centre
    /// is a precise answer about an imprecise point — it would read as though the arena position
    /// were known.
    /// </summary>
    [Fact]
    public void Says_nothing_about_a_map_centre()
    {
        var boss = new CompassTarget(
            "Shadow Behemoth", TargetKind.WorldBoss, Map, "Queensdale", 1000, 1000, Precise: false);

        Assert.Null(PortRoute.To(boss, [Waypoint("Near", 1100, 1000)]));
    }

    [Fact]
    public void Says_nothing_when_the_map_has_no_waypoints()
    {
        Assert.Null(PortRoute.To(Heart(1000, 1000), []));
    }

    /// <summary>Inside the third ring the whole neighbourhood is walkable, and a loading screen
    /// costs more than the walk it saves.</summary>
    [Fact]
    public void A_short_walk_is_not_worth_porting()
    {
        var hop = new PortHop(Waypoint("Near", 0, 0), 10);

        Assert.False(hop.BeatsWalking(1999));
        Assert.True(hop.BeatsWalking(2000));
    }

    /// <summary>"Port, then walk almost as far" is worse than silence.</summary>
    [Fact]
    public void A_waypoint_that_saves_little_is_not_worth_porting()
    {
        var hop = new PortHop(Waypoint("Near", 0, 0), 2600);

        Assert.False(hop.BeatsWalking(5000));
        Assert.True(new PortHop(Waypoint("Near", 0, 0), 2500).BeatsWalking(5000));
    }

    /// <summary>
    /// The case that needs no rule of its own: standing at the nearest waypoint already. The
    /// remaining walk is the whole walk, so the saving test fails and nothing is suggested.
    /// </summary>
    [Fact]
    public void Standing_at_the_waypoint_suggests_nothing()
    {
        var hop = new PortHop(Waypoint("Here", 0, 0), 4000);

        Assert.False(hop.BeatsWalking(4000));
    }

    /// <summary>
    /// The line names the waypoint as the map does and gives the walk in continent units, and
    /// claims nothing else. Cost and contested state are not in the API; see PortRoute.
    /// </summary>
    [Fact]
    public void The_line_is_the_waypoint_and_what_is_left()
    {
        var line = PortRoute.Line(new PortHop(Waypoint("Altar Brook Waypoint", 0, 0), 183.4));

        Assert.Equal("via Altar Brook Waypoint · then 183", line);
    }
}
