using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Turning continent coordinates into map tile indices.
///
/// Every index here was fetched and looked at rather than derived a second time from the same
/// reasoning the code uses. That is not a preference: the first version of this file agreed with
/// a formula that was one zoom level out, and every tile it asked for existed, downloaded
/// cleanly, and was open sea.
///
/// Tyria is continent 1: <c>continent_dims</c> 81920 x 114688, <c>max_zoom</c> 8 per
/// /v2/continents/1, and a reference zoom of <b>7</b>.
/// </summary>
public class TileGeometryTests
{
    /// <summary>
    /// The zoom at which a continent coordinate is a pixel: <c>max_zoom</c> minus one.
    ///
    /// Not what /v2/continents/1 calls max_zoom, and the difference is invisible until somebody
    /// looks at the screen.
    /// </summary>
    private const int ReferenceZoom = 7;

    /// <summary>What the API publishes, kept here so the relationship between the two is written
    /// down rather than remembered.</summary>
    private const int TyriaMaxZoom = 8;
    private const double TyriaWidth = 81920;
    private const double TyriaHeight = 114688;

    /// <summary>The claim the rest of the file rests on.</summary>
    [Fact]
    public void Continent_coordinates_are_pixels_at_the_reference_zoom()
    {
        Assert.Equal(1, TileGeometry.UnitsPerPixel(ReferenceZoom, ReferenceZoom));
        Assert.Equal(44416, TileGeometry.ToPixels(44416, ReferenceZoom, ReferenceZoom), 6);
    }

    [Fact]
    public void Every_level_out_doubles_the_units_a_pixel_covers()
    {
        Assert.Equal(2, TileGeometry.UnitsPerPixel(6, ReferenceZoom));
        Assert.Equal(4, TileGeometry.UnitsPerPixel(5, ReferenceZoom));
        Assert.Equal(128, TileGeometry.UnitsPerPixel(0, ReferenceZoom));
    }

    /// <summary>
    /// The mistake this file exists to stop coming back.
    ///
    /// Reading <c>max_zoom</c> as the reference puts the middle of Queensdale on 43/28 at zoom 6.
    /// That tile is real, 7697 bytes of JPEG, and it is sea. The same index at zoom 5 is
    /// Queensdale. Nothing errors either way, which is why this is a test and not a comment.
    /// </summary>
    [Fact]
    public void Max_zoom_is_not_the_reference_zoom()
    {
        Assert.Equal(TyriaMaxZoom - 1, ReferenceZoom);

        Assert.Equal(43, TileGeometry.TileIndex(44416, 6, TyriaMaxZoom));
        Assert.Equal(43, TileGeometry.TileIndex(44416, 5, ReferenceZoom));
    }

    /// <summary>
    /// The middle of Queensdale, (44416, 29248), down six zoom levels, each one fetched and
    /// looked at.
    ///
    /// Zoom 2 is <c>1/1/2/5/3.jpg</c> and shows Divinity's Reach, which settles it beyond
    /// argument: a round walled city is not somewhere else. Zoom 7 is 173/114 and is the deepest
    /// the service renders here, since zoom 8 over this map answers NoSuchKey.
    /// </summary>
    [Theory]
    [InlineData(2, 5, 3)]
    [InlineData(3, 10, 7)]
    [InlineData(4, 21, 14)]
    [InlineData(5, 43, 28)]
    [InlineData(6, 86, 57)]
    [InlineData(7, 173, 114)]
    public void Queensdale_centre_lands_on_the_tile_the_service_serves(int zoom, int x, int y)
    {
        Assert.Equal(x, TileGeometry.TileIndex(44416, zoom, ReferenceZoom));
        Assert.Equal(y, TileGeometry.TileIndex(29248, zoom, ReferenceZoom));
    }

    [Fact]
    public void A_pixel_converts_back_to_where_it_came_from()
    {
        var pixels = TileGeometry.ToPixels(44416, 5, ReferenceZoom);

        Assert.Equal(44416, TileGeometry.ToContinent(pixels, 5, ReferenceZoom), 6);
    }

    /// <summary>
    /// The grid at the reference zoom is the continent in tiles, and that is the second piece of
    /// evidence for 7 rather than 8: column 173 exists at zoom 7, and the 160-wide grid that
    /// max_zoom-as-reference implies has no such column.
    /// </summary>
    [Fact]
    public void Tyria_is_320_by_448_tiles_at_the_reference_zoom()
    {
        Assert.Equal(320, TileGeometry.GridTiles(TyriaWidth, ReferenceZoom, ReferenceZoom));
        Assert.Equal(448, TileGeometry.GridTiles(TyriaHeight, ReferenceZoom, ReferenceZoom));

        Assert.True(TileGeometry.TileIndex(44416, 7, ReferenceZoom) < 320);
    }

    /// <summary>A part tile is still a tile: the continent does not end on a 256 boundary at
    /// every level, and the last column exists.</summary>
    [Fact]
    public void A_partial_tile_is_counted()
    {
        Assert.Equal(2, TileGeometry.GridTiles(257, 8, 8));
        Assert.Equal(1, TileGeometry.GridTiles(1, 8, 8));
    }

    /// <summary>
    /// Queensdale's continent_rect is [[42624, 28032], [46208, 30464]] — 3584 x 2432 units. On a
    /// 1080 x 1800 viewport zoom 6 would be 1792 pixels wide and overflow, so zoom 5 is the
    /// answer: 896 x 608, which is what the phone draws.
    /// </summary>
    [Fact]
    public void A_map_is_fitted_at_the_sharpest_zoom_that_holds_it()
    {
        Assert.Equal(5, TileGeometry.FitZoom(3584, 2432, 1080, 1800, ReferenceZoom));
    }

    [Fact]
    public void A_viewport_bigger_than_the_map_still_stops_at_the_reference_zoom()
    {
        Assert.Equal(ReferenceZoom, TileGeometry.FitZoom(100, 100, 4000, 4000, ReferenceZoom));
    }

    /// <summary>
    /// The point under the fingers stays under the fingers.
    ///
    /// Stated as the property rather than as an expected number, because the number is the thing
    /// under test: an assertion of 44544 would be the formula written twice.
    /// </summary>
    [Theory]
    [InlineData(5, 6)]
    [InlineData(6, 5)]
    [InlineData(3, 7)]
    public void A_zoom_holds_the_pinched_point_still(int from, int to)
    {
        const double centre = 44416;
        const double offset = 200;

        var held = centre + offset * TileGeometry.UnitsPerPixel(from, ReferenceZoom);

        var moved = TileGeometry.Recentre(centre, offset, from, to, ReferenceZoom);

        Assert.Equal(held, moved + offset * TileGeometry.UnitsPerPixel(to, ReferenceZoom), 6);
    }

    /// <summary>Pinching about the exact middle moves nothing, whichever way the zoom goes.</summary>
    [Fact]
    public void A_zoom_about_the_middle_does_not_move_the_middle()
    {
        Assert.Equal(44416, TileGeometry.Recentre(44416, 0, 5, 7, ReferenceZoom), 6);
    }

    /// <summary>Zooming in and straight back out leaves the map where it started.</summary>
    [Fact]
    public void A_zoom_there_and_back_is_where_it_started()
    {
        var there = TileGeometry.Recentre(44416, 137, 5, 6, ReferenceZoom);

        Assert.Equal(44416, TileGeometry.Recentre(there, 137, 6, 5, ReferenceZoom), 6);
    }

    [Fact]
    public void Visible_tiles_cover_the_viewport_inclusively()
    {
        var (fromX, fromY, toX, toY) = TileGeometry.Visible(0, 0, 512, 512, 100, 100);

        Assert.Equal(0, fromX);
        Assert.Equal(0, fromY);
        Assert.Equal(2, toX);
        Assert.Equal(2, toY);
    }

    [Fact]
    public void Visible_tiles_are_clamped_to_the_grid()
    {
        var (fromX, fromY, _, _) = TileGeometry.Visible(-600, -600, 512, 512, 100, 100);

        Assert.Equal(0, fromX);
        Assert.Equal(0, fromY);

        var (_, _, farX, farY) = TileGeometry.Visible(90 * 256, 90 * 256, 5000, 5000, 100, 100);

        Assert.Equal(99, farX);
        Assert.Equal(99, farY);
    }
}
