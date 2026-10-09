namespace GuildWarsCompanion.Services;

/// <summary>
/// Converting a position inside a map into the continent coordinates the rest of this app speaks.
///
/// Two spaces, and every published coordinate is in one or the other. <b>Continent</b> coordinates
/// are the ones the compass, the tiles and MumbleLink's playerX/playerY all use: one space for the
/// whole world, y growing south. <b>Map</b> coordinates are local to a single map, y growing
/// north, and they are what <c>/v1/event_details.json</c> gives for an event's centre.
///
/// <c>/v2/maps/{id}</c> publishes both rectangles for every map — <c>map_rect</c> and
/// <c>continent_rect</c> — which is the whole of what makes this convertible. It is a linear fit
/// of one rectangle onto the other, plus a flip, because the two disagree about which way y grows.
/// The flip is the only part worth staring at: getting it wrong mirrors every position
/// north-to-south without making any single number look wrong, which is the same trap
/// <see cref="Bearing"/> documents for the needle.
/// </summary>
public static class MapSpace
{
    /// <summary>
    /// Inches to the metre, which is the unit a marker pack stores and the unit a map rectangle
    /// does not.
    ///
    /// Map coordinates are inches; MumbleLink's avatar position, and therefore everything a pack
    /// records, is metres. <see cref="FromMetres"/> is the only place this is applied.
    /// </summary>
    public const double InchesPerMetre = 39.3701;

    /// <summary>
    /// An avatar-space position — what a marker pack stores — as a point in map coordinates.
    ///
    /// <b>z is not negated, and that was measured rather than reasoned.</b> The reasoning said it
    /// should be: avatar space is x east, y up, z south, and map y grows north, so map y ought to
    /// be minus z. It is not. Measured against a real pack — Teh's Trails, 9264 markers — and the
    /// landmarks the floor endpoint publishes:
    ///
    /// <list type="bullet">
    /// <item><b>+z</b>: 114 of Queensdale's 334 markers and 161 of Metrica Province's 340 land
    /// within 40 units of a published landmark; median distance to the nearest one, 72 and 41.</item>
    /// <item><b>-z</b>: 9 and 18 within 40 units; median 177 and 119, which is no better than
    /// scattering them at random over the same map.</item>
    /// </list>
    ///
    /// So the flip belongs to <see cref="ToContinent"/> and to nothing else, and applying it twice
    /// was the mistake. The scale is inches to the metre either way.
    /// </summary>
    public static (double X, double Y) FromMetres(double metresX, double metresZ) =>
        (metresX * InchesPerMetre, metresZ * InchesPerMetre);

    /// <summary>
    /// Whether a converted point is anywhere inside the map it claims to be on.
    ///
    /// <b>A weak check, and worth knowing how weak.</b> It was written to catch the sign above
    /// being wrong and it does not: a map rectangle is roughly symmetrical about its middle and
    /// markers cluster centrally, so mirroring them north-to-south leaves almost all of them
    /// still inside — 329 of Queensdale's 334, measured. What it does catch is junk: a marker
    /// belonging to another map, a coordinate in the wrong unit, a pack's own mistakes. The sign
    /// was settled by distance to published landmarks instead; see <see cref="FromMetres"/>.
    /// </summary>
    public static bool Inside(
        double mapX, double mapY, (double Left, double Bottom, double Right, double Top) map) =>
        mapX >= map.Left && mapX <= map.Right && mapY >= map.Bottom && mapY <= map.Top;

    /// <summary>
    /// A point in a map's own coordinates, as a continent coordinate.
    /// </summary>
    /// <param name="map">The map's footprint in map coordinates, y growing north.</param>
    /// <param name="continent">Its footprint in continent coordinates, y growing south.</param>
    public static (double X, double Y) ToContinent(
        double mapX, double mapY,
        (double Left, double Bottom, double Right, double Top) map,
        (double Left, double Top, double Right, double Bottom) continent)
    {
        var width = map.Right - map.Left;
        var height = map.Top - map.Bottom;

        // A map with no extent cannot place anything. The middle is the one answer that is not
        // actively wrong, and the caller decides whether to trust it — see CompassTarget.Precise.
        if (width == 0 || height == 0)
            return ((continent.Left + continent.Right) / 2, (continent.Top + continent.Bottom) / 2);

        var acrossX = (mapX - map.Left) / width;

        // From the top of the map downwards, which is what turns a northward axis into a
        // southward one.
        var downY = (map.Top - mapY) / height;

        return (
            continent.Left + acrossX * (continent.Right - continent.Left),
            continent.Top + downY * (continent.Bottom - continent.Top));
    }

    /// <summary>
    /// A continent coordinate back into MumbleLink metres, x and z — the reverse of
    /// <see cref="FromMetres"/> then <see cref="ToContinent"/>. For drawing in the game, which
    /// speaks metres: the API places a point of interest on the continent and nowhere else.
    /// </summary>
    public static (double X, double Z) ToMetres(
        double continentX, double continentY,
        (double Left, double Bottom, double Right, double Top) map,
        (double Left, double Top, double Right, double Bottom) continent)
    {
        var wide = continent.Right - continent.Left;
        var tall = continent.Bottom - continent.Top;

        if (wide == 0 || tall == 0) return (0, 0);

        var mapX = map.Left + (continentX - continent.Left) / wide * (map.Right - map.Left);
        var mapY = map.Top - (continentY - continent.Top) / tall * (map.Top - map.Bottom);

        return (mapX / InchesPerMetre, mapY / InchesPerMetre);
    }
}
