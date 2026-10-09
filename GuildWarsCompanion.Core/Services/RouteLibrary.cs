using System.Globalization;
using System.Text;

using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// A route kept from a pack: one run of points on one map, in the pack's own metres, under the
/// category path the pack filed it in.
/// </summary>
/// <param name="Argb">
/// The colour the pack asked for, packed, or <see cref="RouteColour.None"/> when it asked for
/// nothing and the app's own brass should be used.
/// </param>
/// <param name="Heights">
/// Metres up, one per point, or null for a route stored before heights were kept. A single
/// unknown height inside a known route is NaN rather than a reason to drop the rest.
/// </param>
public sealed record StoredRoute(
    int MapId, string Type, IReadOnlyList<(double X, double Z)> Points, int Argb = RouteColour.None,
    IReadOnlyList<double>? Heights = null);

/// <summary>A route ready to draw: continent coordinates, and what to call it.</summary>
/// <param name="Heights">As on <see cref="StoredRoute"/>: metres, one per point, or null.</param>
/// <param name="Metres">The same points as the pack stored them, in MumbleLink metres, index for
/// index with <paramref name="Points"/>. What the Blish HUD module is handed: the game draws in
/// metres, and converting there and back would only add error.</param>
public sealed record RouteLine(
    int MapId, string Type, string Name, IReadOnlyList<(double X, double Y)> Points,
    int Argb = RouteColour.None, IReadOnlyList<double>? Heights = null,
    IReadOnlyList<(double X, double Z)>? Metres = null);

/// <summary>
/// What a pack's trails leave behind, and how they become a line on the map.
///
/// The same shape as <see cref="MarkerLibrary"/> and for the same reasons: stored raw in the
/// pack's units against a map id, converted to continent coordinates only for the map being
/// looked at, and written as lines rather than JSON because this file is read and written here
/// alone and is worth being able to open in an editor when a route comes out in the sea.
///
/// <b>Why routes are not markers.</b> A marker is a place to point at; a route is a shape to
/// follow, it has no single position, and pointing a compass needle at it would be meaningless.
/// They share the conversion and nothing else — see <see cref="MapSpace"/>.
/// </summary>
public static class RouteLibrary
{
    /// <summary>What is written: each point carries its height as a third number.</summary>
    public const string Version = "routes 3";

    /// <summary>The colour column, without heights. Still read, for the reason below.</summary>
    private const string ColourVersion = "routes 2";

    /// <summary>
    /// The version before the colour column, still read.
    ///
    /// <b>Read rather than refused, and the reason is the update path.</b> A pack that has not
    /// changed on its server answers <c>304 Not Modified</c> — see <see cref="MarkerStore"/> — so
    /// refusing an older file would empty the routes of every pack already on the device and give
    /// them no way back short of removing and re-adding each one. A missing column is a missing
    /// colour, which is exactly what <see cref="RouteColour.None"/> means.
    /// </summary>
    private const string FirstVersion = "routes 1";

    /// <summary>
    /// How far apart two points must be, in metres, to both be kept.
    ///
    /// Packs draw at walking resolution: Teh's Trails carries 54338 points across 526 trails, and
    /// on a phone at the zoom a whole map fits, a metre is far under a pixel. This is a plain
    /// distance filter rather than Douglas-Peucker — the shape of a walking route has no long
    /// straight runs for a proper simplifier to collapse, so the cheap rule keeps the same line
    /// for a fraction of the code.
    /// </summary>
    public const double Apart = 15;

    public static string Write(IEnumerable<StoredRoute> routes)
    {
        var text = new StringBuilder().AppendLine(Version);

        foreach (var route in routes)
        {
            if (route.Points.Count < 2) continue;

            text.Append(route.MapId.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(Clean(route.Type)).Append('\t')
                .Append(route.Argb.ToString(CultureInfo.InvariantCulture)).Append('\t');

            for (var i = 0; i < route.Points.Count; i++)
            {
                if (i > 0) text.Append(' ');

                text.Append(route.Points[i].X.ToString("R", CultureInfo.InvariantCulture))
                    .Append(',')
                    .Append(route.Points[i].Z.ToString("R", CultureInfo.InvariantCulture));

                // Left off when unknown, so a point without a height reads back as one.
                if (route.Heights is { } heights && i < heights.Count && double.IsFinite(heights[i]))
                    text.Append(',').Append(heights[i].ToString("R", CultureInfo.InvariantCulture));
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    public static IReadOnlyList<StoredRoute> Read(string text)
    {
        var routes = new List<StoredRoute>();

        using var lines = new StringReader(text);

        if (lines.ReadLine()?.Trim() is not { } header) return routes;

        if (header != Version && header != ColourVersion && header != FirstVersion) return routes;

        // Where the points are, which is the difference between the first version and the rest.
        // Heights need no switch: a point with two numbers has none, whichever version wrote it.
        var coloured = header != FirstVersion;
        var column = coloured ? 3 : 2;

        while (lines.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;

            var parts = line.Split('\t');

            if (parts.Length < column + 1) continue;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var map))
                continue;

            var argb = RouteColour.None;

            if (coloured &&
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out argb))
                continue;

            var points = new List<(double X, double Z)>();
            var heights = new List<double>();
            var anyHeight = false;

            foreach (var pair in parts[column].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var at = pair.Split(',');

                if (at.Length is not (2 or 3)) continue;

                if (!double.TryParse(at[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                    !double.TryParse(at[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    continue;

                points.Add((x, z));

                var height = double.NaN;

                if (at.Length == 3 &&
                    double.TryParse(at[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
                {
                    height = h;
                    anyHeight = true;
                }

                heights.Add(height);
            }

            if (points.Count >= 2)
                routes.Add(new StoredRoute(map, parts[1], points, argb, anyHeight ? heights : null));
        }

        return routes;
    }

    /// <summary>
    /// Drops points a screen cannot tell apart, keeping the ends.
    ///
    /// The last point is always kept: a route that stops short of where it ends is a route that
    /// lies about where it goes, and the saving from dropping it is one point.
    /// </summary>
    public static IReadOnlyList<(double X, double Z)> Thin(
        IReadOnlyList<(double X, double Z)> points, double apart = Apart) =>
        Thin(points, null, apart).Points;

    /// <summary>
    /// The same, keeping each surviving point's height with it.
    ///
    /// Distance is judged flat, on x and z alone. A ladder or a drop straight down therefore
    /// collapses to its two ends — which is the right answer for a line on a map, and loses
    /// nothing a guide needs, since the ends are exactly where the height is read.
    /// </summary>
    public static (IReadOnlyList<(double X, double Z)> Points, IReadOnlyList<double>? Heights) Thin(
        IReadOnlyList<(double X, double Z)> points, IReadOnlyList<double>? heights, double apart = Apart)
    {
        if (heights is not null && heights.Count != points.Count) heights = null;

        if (points.Count <= 2) return (points, heights);

        var kept = new List<(double X, double Z)> { points[0] };
        List<double>? up = heights is null ? null : [heights[0]];

        for (var i = 1; i < points.Count; i++)
        {
            var point = points[i];
            var last = kept[^1];
            var dx = point.X - last.X;
            var dz = point.Z - last.Z;

            if (dx * dx + dz * dz < apart * apart) continue;

            kept.Add(point);

            if (up is not null && heights is not null) up.Add(heights[i]);
        }

        var end = points[^1];

        if (kept[^1] != end)
        {
            kept.Add(end);

            if (up is not null && heights is not null) up.Add(heights[^1]);
        }

        return (kept, up);
    }

    /// <summary>
    /// Everything a pack has for one map, as lines in continent coordinates.
    ///
    /// A route with any point outside the map is kept, unlike a marker, and that is deliberate:
    /// a marker outside its map is junk, while a route often runs to the very edge of one and a
    /// single stray point would throw away the whole line. Points are clamped to nothing and
    /// drawn where they fall; the map's own clipping is what keeps them on screen.
    /// </summary>
    public static IReadOnlyList<RouteLine> Lines(
        IEnumerable<StoredRoute> routes, MapInfo map, Func<string, string> name)
    {
        var lines = new List<RouteLine>();

        if (map.MapArea is not { } area || map.ContinentArea is not { } continent) return lines;

        foreach (var route in routes)
        {
            if (route.MapId != map.Id) continue;

            var points = new List<(double X, double Y)>(route.Points.Count);

            foreach (var (metresX, metresZ) in route.Points)
            {
                var (mapX, mapY) = MapSpace.FromMetres(metresX, metresZ);

                points.Add(MapSpace.ToContinent(mapX, mapY, area, continent));
            }

            if (points.Count >= 2)
                lines.Add(new RouteLine(
                    map.Id, route.Type, name(route.Type), points, route.Argb, route.Heights,
                    route.Points));
        }

        return lines;
    }

    private static string Clean(string? type) =>
        type is null ? string.Empty : type.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
