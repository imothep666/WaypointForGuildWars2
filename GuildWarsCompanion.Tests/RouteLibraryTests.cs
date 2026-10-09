using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Keeping routes, thinning them, and putting them on a map.
/// </summary>
public class RouteLibraryTests
{
    private static StoredRoute Route(params (double X, double Z)[] points) =>
        new(15, "tt.mc.mt", points);

    [Fact]
    public void Writes_and_reads_a_route_unchanged()
    {
        var written = RouteLibrary.Write([Route((10.5, -20.25), (30, 40), (50.125, 60))]);

        var route = Assert.Single(RouteLibrary.Read(written));

        Assert.Equal(15, route.MapId);
        Assert.Equal("tt.mc.mt", route.Type);
        Assert.Equal([(10.5, -20.25), (30.0, 40.0), (50.125, 60.0)], route.Points);
    }

    /// <summary>
    /// The same rule the marker file has: a file from a newer format is refused rather than
    /// half-read into something that looks like data.
    /// </summary>
    [Fact]
    public void Refuses_a_file_it_does_not_recognise()
    {
        Assert.Empty(RouteLibrary.Read("routes 99\n15\ttt\t1,2 3,4\n"));
        Assert.Empty(RouteLibrary.Read(string.Empty));
    }

    [Fact]
    public void Skips_a_line_with_no_line_in_it()
    {
        var text = RouteLibrary.Version + "\n15\ttt\t0\t1,2\n15\ttt\t0\t3,4 5,6\n";

        var route = Assert.Single(RouteLibrary.Read(text));

        Assert.Equal(2, route.Points.Count);
    }

    /// <summary>
    /// The colour column, which arrived with the format's second version. A file from the first
    /// is refused by the header rather than read a field short — see <see cref="RouteLibrary"/>.
    /// </summary>
    [Fact]
    public void Writes_and_reads_the_colour_a_pack_asked_for()
    {
        var written = RouteLibrary.Write(
            [new StoredRoute(15, "mm.a", [(0, 0), (10, 10)], unchecked((int)0xFF009E73))]);

        Assert.Equal(unchecked((int)0xFF009E73), Assert.Single(RouteLibrary.Read(written)).Argb);
    }

    /// <summary>
    /// A file written before the colour column is read, not refused: an unchanged pack answers
    /// 304, so refusing it would empty the routes of every pack already on the device with no way
    /// back short of removing and re-adding each one.
    /// </summary>
    [Fact]
    public void Reads_the_formats_first_version_as_having_no_colour()
    {
        var route = Assert.Single(RouteLibrary.Read("routes 1\n15\ttt\t1,2 3,4\n"));

        Assert.Equal(2, route.Points.Count);
        Assert.Equal(RouteColour.None, route.Argb);
    }

    [Fact]
    public void Still_refuses_a_version_it_does_not_know()
    {
        Assert.Empty(RouteLibrary.Read("routes 99\n15\ttt\t0\t1,2 3,4\n"));
    }

    [Fact]
    public void Carries_the_colour_onto_the_map()
    {
        var lines = RouteLibrary.Lines(
            [new StoredRoute(15, "mm.a", [(0, 0), (10, 10)], unchecked((int)0xFF009E73))],
            Queensdale(), _ => "Route");

        Assert.Equal(unchecked((int)0xFF009E73), Assert.Single(lines).Argb);
    }

    /// <summary>
    /// A route is written by somebody walking, so its points arrive a metre apart. On a phone at
    /// the zoom where a whole map fits, that is far under a pixel.
    /// </summary>
    [Fact]
    public void Thins_points_a_screen_could_not_tell_apart()
    {
        var walked = new List<(double X, double Z)>();

        for (var i = 0; i < 100; i++) walked.Add((i, 0));

        var thinned = RouteLibrary.Thin(walked, apart: 15);

        Assert.True(thinned.Count < 10);
        Assert.Equal((0.0, 0.0), thinned[0]);
    }

    /// <summary>A route that stops short of its end lies about where it goes.</summary>
    [Fact]
    public void Always_keeps_the_last_point()
    {
        var thinned = RouteLibrary.Thin([(0, 0), (100, 0), (101, 0)], apart: 15);

        Assert.Equal((101.0, 0.0), thinned[^1]);
    }

    [Fact]
    public void Leaves_a_two_point_line_alone()
    {
        Assert.Equal([(0.0, 0.0), (1.0, 0.0)], RouteLibrary.Thin([(0, 0), (1, 0)]));
    }

    // --- onto a map --------------------------------------------------------

    /// <summary>Queensdale's own rectangles, as /v2/maps publishes them.</summary>
    private static MapInfo Queensdale() =>
        new(15, "Queensdale", 4, 1, 1,
            [[42624, 28032], [46208, 30464]],
            [[-43008, -27648], [43008, 27648]]);

    [Fact]
    public void Puts_a_route_into_continent_coordinates()
    {
        var lines = RouteLibrary.Lines(
            [new StoredRoute(15, "tt.mc", [(0, 0), (100, 100)])], Queensdale(), _ => "Route");

        var line = Assert.Single(lines);

        Assert.Equal(2, line.Points.Count);

        // The middle of the map, because avatar space has its origin there.
        Assert.Equal(44416, line.Points[0].X, 0);
        Assert.Equal(29248, line.Points[0].Y, 0);
    }

    [Fact]
    public void Leaves_another_maps_routes_alone()
    {
        var lines = RouteLibrary.Lines(
            [new StoredRoute(30, "tt.mc", [(0, 0), (100, 100)])], Queensdale(), _ => "Route");

        Assert.Empty(lines);
    }

    /// <summary>
    /// Unlike a marker, a stray point does not discard the route. A marker outside its map is
    /// junk; a route runs to the edge of one by design, and dropping the whole line over one
    /// point would lose the route somebody asked for.
    /// </summary>
    [Fact]
    public void Keeps_a_route_that_runs_past_the_edge()
    {
        var lines = RouteLibrary.Lines(
            [new StoredRoute(15, "tt.mc", [(0, 0), (999999, 999999)])], Queensdale(), _ => "Route");

        Assert.Equal(2, Assert.Single(lines).Points.Count);
    }

    [Fact]
    public void Names_a_route_through_the_namer_it_is_given()
    {
        var lines = RouteLibrary.Lines(
            [new StoredRoute(15, "tt.mc", [(0, 0), (10, 10)])], Queensdale(),
            type => type == "tt.mc" ? "Map Completion" : "wrong");

        Assert.Equal("Map Completion", Assert.Single(lines).Name);
    }
}
