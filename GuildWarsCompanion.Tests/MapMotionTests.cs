using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The map's zoom easing and its glide after the character.
///
/// Reference zoom 7 throughout, as Tyria's, so a continent unit is a pixel at zoom 7 and two at 6.
/// </summary>
public class MapMotionTests
{
    private const int Reference = 7;

    private static double Units(int zoom) => TileGeometry.UnitsPerPixel(zoom, Reference);

    /// <summary>
    /// The point between the fingers stays under them for the whole zoom, not only at its ends:
    /// a zoom that held it only at the end would swing the map out and back.
    /// </summary>
    [Fact]
    public void A_zoom_holds_its_point_still_all_the_way()
    {
        var motion = new MapMotion();
        var centre = (X: 10_000.0, Y: 20_000.0);
        var offset = (X: 120.0, Y: -80.0);
        var held = (centre.X + offset.X * Units(5), centre.Y + offset.Y * Units(5));

        motion.Zoom(5, 6, held, offset, Units(6));

        for (var frame = 0; frame < 10; frame++)
        {
            centre = motion.Advance(0.033, centre.X, centre.Y, Units(6));

            // Where the held point is on the screen at this frame's scale.
            var units = Units(6) / motion.Scale;
            Assert.Equal(offset.X, (held.Item1 - centre.X) / units, 6);
            Assert.Equal(offset.Y, (held.Item2 - centre.Y) / units, 6);
        }
    }

    [Fact]
    public void A_zoom_ends_where_a_jump_would_have_landed_and_stops()
    {
        var motion = new MapMotion();
        var offset = (X: 120.0, Y: -80.0);
        var held = (10_000 + offset.X * Units(5), 20_000 + offset.Y * Units(5));

        motion.Zoom(5, 6, held, offset, Units(6));

        var centre = motion.Advance(MapMotion.ZoomSeconds, 10_000, 20_000, Units(6));

        Assert.Equal(TileGeometry.Recentre(10_000, offset.X, 5, 6, Reference), centre.X, 6);
        Assert.Equal(TileGeometry.Recentre(20_000, offset.Y, 5, 6, Reference), centre.Y, 6);
        Assert.Equal(1, motion.Scale);
        Assert.False(motion.IsMoving);
    }

    /// <summary>Zooming in starts from the old level's size, half of the new one's; out, from
    /// twice. Nothing changes size in the first frame.</summary>
    [Theory]
    [InlineData(5, 6, 0.5)]
    [InlineData(6, 5, 2)]
    public void A_zoom_starts_at_the_size_it_left(int from, int to, double scale)
    {
        var motion = new MapMotion();

        motion.Zoom(from, to, (0, 0), (0, 0), Units(to));

        Assert.Equal(scale, motion.Scale);
        Assert.True(motion.IsZooming);
    }

    /// <summary>Eased on the exponent: half of the way through in time is past half of the way in
    /// scale, because the ease is fast first, and between the two ends either way.</summary>
    [Fact]
    public void A_zoom_eases_out()
    {
        var motion = new MapMotion();

        motion.Zoom(5, 6, (0, 0), (0, 0), Units(6));
        motion.Advance(MapMotion.ZoomSeconds / 2, 0, 0, Units(6));

        // Exponent -1 eased to 1 - (1 - 0.5)^3 = 0.875 of the way: 2^-0.125.
        Assert.Equal(Math.Pow(2, -0.125), motion.Scale, 6);
    }

    [Fact]
    public void A_glide_closes_most_of_the_distance_in_half_a_second()
    {
        var motion = new MapMotion();

        motion.GlideTo(1000, 0);

        var centre = (X: 0.0, Y: 0.0);
        for (var frame = 0; frame < 15; frame++) centre = motion.Advance(1 / 30.0, centre.X, centre.Y, 1);

        Assert.InRange(centre.X, 950, 1000);
        Assert.True(motion.IsMoving, "a glide stopped short of the point");
    }

    [Fact]
    public void A_glide_arrives_exactly_and_stops()
    {
        var motion = new MapMotion();

        motion.GlideTo(1000, 500);

        var centre = (X: 0.0, Y: 0.0);
        for (var frame = 0; frame < 90 && motion.IsMoving; frame++)
            centre = motion.Advance(1 / 30.0, centre.X, centre.Y, 1);

        Assert.Equal((1000.0, 500.0), centre);
        Assert.False(motion.IsMoving);
    }

    /// <summary>Nothing moving, nothing moves: a frame asked for with no motion under way leaves
    /// the centre where it is.</summary>
    [Fact]
    public void With_nothing_moving_the_centre_stays()
    {
        var motion = new MapMotion();

        Assert.False(motion.IsMoving);
        Assert.Equal((5.0, 6.0), motion.Advance(1, 5, 6, 1));
    }

    [Fact]
    public void A_zoom_replaces_a_glide()
    {
        var motion = new MapMotion();

        motion.GlideTo(1000, 0);
        motion.Zoom(5, 6, (0, 0), (0, 0), Units(6));
        motion.Advance(MapMotion.ZoomSeconds, 0, 0, Units(6));

        Assert.False(motion.IsMoving);
    }
}
