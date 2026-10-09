namespace GuildWarsCompanion.Services;

/// <summary>
/// The arithmetic between continent coordinates and the game's own map tiles.
///
/// <c>https://tiles.guildwars2.com/{continent}/{floor}/{zoom}/{x}/{y}.jpg</c> serves the map as
/// 256-pixel squares. The one fact the whole file rests on is that <b>continent coordinates are
/// pixels at the continent's reference zoom</b> — the same coordinates MumbleLink reports and
/// /v2/continents publishes for every heart and waypoint. That is why the character's position
/// and a landmark's can be drawn onto a tile without a projection: there is no projection, only
/// a halving per zoom level.
///
/// <b>The reference zoom is not the <c>max_zoom</c> the API reports, and that cost a screen full
/// of open ocean to find out.</b> /v2/continents/1 says Tyria is 81920 x 114688 with
/// <c>max_zoom</c> 8. Taking 8 as the reference puts the middle of Queensdale — (44416, 29248) —
/// on tile 43/28 at zoom 6, and that tile is real, downloads cleanly, and is sea. Every index was
/// one zoom level out, which is the least visible way to be wrong: the tiles exist, so nothing
/// errors.
///
/// Seven is the reference, established by fetching and looking at six of them: the same
/// coordinates give 5/3 at zoom 2 (Divinity's Reach, unmistakable), 10/7 at zoom 3, 21/14 at
/// zoom 4, 43/28 at zoom 5, 86/57 at zoom 6 and 173/114 at zoom 7 — all Kryta, all land. Zoom 8
/// answers NoSuchKey over this map, so <c>max_zoom</c> is the deepest level the service will ever
/// serve rather than the level the coordinates are quoted in. The caller passes
/// <c>max_zoom - 1</c>; see <c>MapViewModel</c>.
///
/// Pure, and kept apart from anything that fetches or draws, so the indices can be checked
/// against tiles that were actually downloaded rather than against a second copy of this
/// reasoning.
/// </summary>
public static class TileGeometry
{
    /// <summary>The side of one tile in pixels. Fixed by the service, not a choice.</summary>
    public const int TileSize = 256;

    /// <summary>
    /// Continent units per pixel at a zoom level.
    ///
    /// One at the reference zoom, doubling for every level out. Tyria's reference is 7 and its
    /// <c>continent_dims</c> are 81920 x 114688, so the continent is 320 x 448 tiles at zoom 7.
    /// </summary>
    public static double UnitsPerPixel(int zoom, int referenceZoom) => Math.Pow(2, referenceZoom - zoom);

    /// <summary>A continent coordinate as a pixel at a zoom level.</summary>
    public static double ToPixels(double continent, int zoom, int referenceZoom) =>
        continent / UnitsPerPixel(zoom, referenceZoom);

    /// <summary>A pixel back to a continent coordinate, for turning a tap into a place.</summary>
    public static double ToContinent(double pixels, int zoom, int referenceZoom) =>
        pixels * UnitsPerPixel(zoom, referenceZoom);

    /// <summary>Which tile a continent coordinate falls in.</summary>
    public static int TileIndex(double continent, int zoom, int referenceZoom) =>
        (int)Math.Floor(ToPixels(continent, zoom, referenceZoom) / TileSize);

    /// <summary>
    /// How many tiles wide or tall the grid is at a zoom level.
    ///
    /// Rounded up, because the continent's dimensions are not multiples of the tile size at every
    /// level: the last row and column are part tiles, and they exist.
    /// </summary>
    public static int GridTiles(double dimension, int zoom, int referenceZoom) =>
        Math.Max(1, (int)Math.Ceiling(ToPixels(dimension, zoom, referenceZoom) / TileSize));

    /// <summary>
    /// The closest zoom that still fits a map inside the viewport.
    ///
    /// Zooming in is free detail and zooming out is wasted screen, so this walks down from the
    /// sharpest level and takes the first that fits rather than computing a fractional zoom. The
    /// levels are what the service has; anything between them would have to be scaled, and a
    /// scaled tile is a blurred one.
    /// </summary>
    public static int FitZoom(
        double rectWidth, double rectHeight, double viewWidth, double viewHeight,
        int referenceZoom, int minZoom = 0)
    {
        for (var zoom = referenceZoom; zoom > minZoom; zoom--)
            if (ToPixels(rectWidth, zoom, referenceZoom) <= viewWidth &&
                ToPixels(rectHeight, zoom, referenceZoom) <= viewHeight)
                return zoom;

        return minZoom;
    }

    /// <summary>
    /// Where the middle has to move to so that a zoom holds one point still.
    ///
    /// Pinching about the middle of the screen makes a map feel like it is fighting the hand: the
    /// thing being pinched is whatever is under the fingers, and that is what has to stay there.
    /// So the place under the fingers is found at the old scale, and the middle is set to whatever
    /// puts it back under them at the new one.
    /// </summary>
    /// <param name="offsetPixels">How far the held point is from the middle of the viewport,
    /// in pixels at the zoom being left.</param>
    public static double Recentre(
        double centre, double offsetPixels, int fromZoom, int toZoom, int referenceZoom)
    {
        var held = centre + offsetPixels * UnitsPerPixel(fromZoom, referenceZoom);

        // The offset is a screen distance, so it is the same number of pixels at either zoom —
        // what changes is how much world a pixel is worth.
        return held - offsetPixels * UnitsPerPixel(toZoom, referenceZoom);
    }

    /// <summary>
    /// The block of tiles a viewport covers, clamped to the grid.
    ///
    /// Inclusive at both ends. The caller draws what it is given and does not need a second
    /// bounds check.
    /// </summary>
    /// <param name="left">The pixel at the left edge of the viewport, at this zoom.</param>
    public static (int FromX, int FromY, int ToX, int ToY) Visible(
        double left, double top, double viewWidth, double viewHeight, int gridWidth, int gridHeight)
    {
        var fromX = Math.Max(0, (int)Math.Floor(left / TileSize));
        var fromY = Math.Max(0, (int)Math.Floor(top / TileSize));
        var toX = Math.Min(gridWidth - 1, (int)Math.Floor((left + viewWidth) / TileSize));
        var toY = Math.Min(gridHeight - 1, (int)Math.Floor((top + viewHeight) / TileSize));

        return (fromX, fromY, toX, toY);
    }
}
