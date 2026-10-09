using System.Buffers.Binary;

using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Following a trail somebody walked, instead of the straight line.
///
/// The fixture is the shape that prompted it: a target behind a wall, reached by a trail that goes
/// round. Walking straight at it runs into the wall; the guide has to send the character the long
/// way, say how long the long way is, and say how far down it goes.
/// </summary>
public class TrailGuideTests
{
    /// <summary>
    /// East 300, then north 300: an L around a wall. The target sits at the end of the L, so from
    /// the start it is 424 away in a straight line and 600 along the trail. It is 25 metres lower.
    /// </summary>
    private static RouteLine Around(string name = "Around the wall") =>
        new(1015, "legs.map.barefoot", name,
            [(0, 0), (100, 0), (200, 0), (300, 0), (300, -100), (300, -200), (300, -300)],
            Heights: [400, 400, 400, 398, 390, 380, 375]);

    private const double TargetX = 305;
    private const double TargetY = -300;

    [Fact]
    public void A_trail_that_passes_the_target_leads_there()
    {
        var guide = TrailGuide.Find([Around()], TargetX, TargetY);

        Assert.NotNull(guide);
        Assert.Equal(6, guide.TargetIndex);
        Assert.Equal(5, guide.Offset, 0.001);
    }

    [Fact]
    public void A_trail_that_only_comes_near_does_not()
    {
        // Sixty units off: close on a map, and still not a trail that goes there.
        Assert.Null(TrailGuide.Find([Around()], TargetX + 60, TargetY));
    }

    [Fact]
    public void The_needle_follows_the_trail_not_the_straight_line()
    {
        var guide = TrailGuide.Find([Around()], TargetX, TargetY)!;

        var leg = guide.Leg(0, 0);

        // The target is north-east; the trail goes east first. The aim is on the trail, a little
        // way along it, and nowhere near north.
        Assert.Equal(TrailGuide.LookAhead, leg.AimX, 0.001);
        Assert.Equal(0, leg.AimY, 0.001);
        Assert.Equal(600, leg.Along, 0.001);
        Assert.True(leg.IsOnTrail);
    }

    [Fact]
    public void The_aim_turns_the_corner_before_the_character_does()
    {
        var guide = TrailGuide.Find([Around()], TargetX, TargetY)!;

        // Twenty short of the corner: the look-ahead is already round it.
        var leg = guide.Leg(300, 0);

        Assert.Equal(300, leg.AimX, 0.001);
        Assert.Equal(-TrailGuide.LookAhead, leg.AimY, 0.001);
    }

    [Fact]
    public void How_far_down_the_way_goes_is_said()
    {
        var guide = TrailGuide.Find([Around()], TargetX, TargetY)!;

        var leg = guide.Leg(0, 0);

        Assert.Equal(-25, leg.Climb!.Value, 0.001);
        Assert.Equal("25 m lower", TrailGuide.Height(leg.Climb));
    }

    [Fact]
    public void Off_the_trail_the_needle_points_back_to_it()
    {
        var guide = TrailGuide.Find([Around()], TargetX, TargetY)!;

        // Sixty north of the first leg, over the wall: the nearest of the trail is behind them.
        var leg = guide.Leg(150, -80);

        Assert.False(leg.IsOnTrail);
        Assert.Equal(0, leg.AimY, 0.001);
    }

    [Fact]
    public void Close_to_the_end_it_points_at_the_target_itself()
    {
        var guide = TrailGuide.Find([Around()], TargetX, TargetY)!;

        var leg = guide.Leg(300, -290);

        Assert.Equal((TargetX, TargetY), (leg.AimX, leg.AimY));
    }

    [Fact]
    public void Walking_the_trail_backwards_works_too()
    {
        // The same L, target at its start: the character is at the far end.
        var guide = TrailGuide.Find([Around()], 0, 5)!;

        var leg = guide.Leg(300, -300);

        Assert.Equal(600, leg.Along, 0.001);
        Assert.Equal(300, leg.AimX, 0.001);
        Assert.Equal(-300 + TrailGuide.LookAhead, leg.AimY, 0.001);
        Assert.Equal(new (double, double)[] { (300, -300), (300, -200), (300, -100), (300, 0), (200, 0), (100, 0), (0, 0) },
            guide.Stretch(6).ToArray());
    }

    [Fact]
    public void Of_two_trails_the_one_that_is_shorter_from_here_wins()
    {
        // A second trail to the same place, starting right beside it.
        var shortcut = new RouteLine(1015, "legs.map.mounted", "Shortcut",
            [(300, -250), (300, -300)]);

        Assert.Equal("Shortcut", TrailGuide.Find([Around(), shortcut], TargetX, TargetY, (300, -240))!.Route.Name);
        Assert.Equal("Around the wall", TrailGuide.Find([Around(), shortcut], TargetX, TargetY, (0, 0))!.Route.Name);
    }

    [Fact]
    public void Without_a_character_the_approach_is_what_is_drawn()
    {
        var guide = TrailGuide.Find([Around()], TargetX, TargetY)!;

        // The first point with at least 250 of trail still ahead of it: the corner, at 300.
        Assert.Equal(3, guide.Back(250));
        Assert.Equal(0, guide.Back(10_000));
    }

    [Fact]
    public void A_trail_with_no_heights_says_nothing_about_height()
    {
        var flat = Around() with { Heights = null };

        var leg = TrailGuide.Find([flat], TargetX, TargetY)!.Leg(0, 0);

        Assert.Null(leg.Climb);
        Assert.Equal(string.Empty, TrailGuide.Height(leg.Climb));
    }

    [Theory]
    [InlineData(-14.2, "14 m lower")]
    [InlineData(8.6, "9 m higher")]
    [InlineData(2.5, "")]   // ordinary ground wobbles this much
    [InlineData(-3, "")]
    public void Height_is_said_only_when_it_matters(double climb, string said) =>
        Assert.Equal(said, TrailGuide.Height(climb));
}

/// <summary>Heights, from the .trl through the stored file and back.</summary>
public class RouteHeightTests
{
    [Fact]
    public void A_trail_file_keeps_its_heights()
    {
        var bytes = new byte[8 + 2 * 12];

        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 1015);

        float[] values = [1, 400, 2, 3, 375.5f, 4];

        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8 + i * 4), BitConverter.SingleToInt32Bits(values[i]));

        var trail = TrailPack.Read(bytes)!;

        Assert.Equal([400.0, 375.5], trail.Heights![0]);
    }

    [Fact]
    public void Stored_heights_read_back()
    {
        var written = RouteLibrary.Write(
            [new StoredRoute(1015, "legs.map", [(0, 0), (10, 10)], Heights: [400, 375.5])]);

        Assert.StartsWith("routes 3", written);
        Assert.Equal([400.0, 375.5], Assert.Single(RouteLibrary.Read(written)).Heights!);
    }

    [Fact]
    public void A_file_from_before_heights_still_reads_without_them()
    {
        var route = Assert.Single(RouteLibrary.Read("routes 2\n15\ttt\t0\t1,2 3,4\n"));

        Assert.Equal(2, route.Points.Count);
        Assert.Null(route.Heights);
    }

    [Fact]
    public void Thinning_keeps_each_height_with_its_point()
    {
        var (points, heights) = RouteLibrary.Thin(
            [(0, 0), (1, 0), (20, 0), (21, 0), (40, 0)], [10, 11, 12, 13, 14], apart: 15);

        Assert.Equal([(0.0, 0.0), (20.0, 0.0), (40.0, 0.0)], points);
        Assert.Equal([10.0, 12.0, 14.0], heights!);
    }

    [Fact]
    public void Heights_go_through_to_the_drawn_line()
    {
        var map = new GuildWarsCompanion.Gw2.MapInfo(
            15, "Queensdale", 4, 1, 1,
            [[42624, 28032], [46208, 30464]],
            [[-43008, -27648], [43008, 27648]]);

        var line = Assert.Single(RouteLibrary.Lines(
            [new StoredRoute(15, "tt.mc", [(0, 0), (100, 100)], Heights: [1, 2])], map, _ => "Route"));

        Assert.Equal([1.0, 2.0], line.Heights!);
    }
}
