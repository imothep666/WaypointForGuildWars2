using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GuildWarsCompanion.Bridge.Protocol;

/// <summary>
/// The way to the objective, in the game's own metres, for Blish HUD to draw in the world.
///
/// A class rather than a record: this file is compiled into the Blish HUD module too, which is
/// net48 and has no init-only setters.
/// </summary>
public sealed class GuidedTrail
{
    public GuidedTrail(
        int mapId, (double X, double Up, double Z) target, IReadOnlyList<(double X, double Up, double Z)> points,
        bool heightKnown = true, string? name = null)
    {
        MapId = mapId;
        Target = target;
        Points = points;
        HeightKnown = heightKnown;
        Name = name;
    }

    /// <summary>
    /// What the objective is called, in the phone's language, for the module's card. Null for a
    /// trail from a phone that does not say: the card then has a distance and no name.
    /// </summary>
    public string? Name { get; }

    /// <summary>The same trail, called <paramref name="name"/>.</summary>
    public GuidedTrail Named(string? name) => new(MapId, Target, Points, HeightKnown, name);

    public int MapId { get; }

    /// <summary>Where the objective is. With a trail its height is borrowed from the trail beside
    /// it; a marker pack's chest brings its own; the API publishes a point of interest in two
    /// dimensions only — see <see cref="HeightKnown"/>.</summary>
    public (double X, double Up, double Z) Target { get; }

    /// <summary>
    /// The trail from the character to the objective, in walking order. Empty for an objective
    /// with no trail to it: the game then shows the objective alone.
    /// </summary>
    public IReadOnlyList<(double X, double Up, double Z)> Points { get; }

    /// <summary>
    /// False when nobody knows how high the objective is — a hero point, a vista, anything the API
    /// publishes on the flat — and <see cref="Target"/>'s height is a placeholder. The module stands
    /// it at the character's own height instead, which from a distance is all a beam needs and up
    /// close is usually right.
    /// </summary>
    public bool HeightKnown { get; }

    public bool IsObjectiveOnly => Points.Count == 0;
}

/// <summary>
/// How a <see cref="GuidedTrail"/> travels: phone to bridge in the body of <c>/guide</c>, and bridge
/// to module unchanged, between a <c>trail</c> line and an <c>end</c>.
///
/// <b>Metres, x east, up, z as MumbleLink writes them</b> — the space a <c>.trl</c> is recorded in and
/// the one Blish HUD draws its world in, after its own swap of the last two. Nothing on the way
/// converts anything, which is the point: the coordinates the pack's author walked are the ones the
/// line is drawn through.
///
///   guide 1
///   map 1015
///   target 1204.5 402.5 -8812.1
///   p 1190.2 410.0 -8790.4
///   p …
///
/// or <c>guide 1</c> then <c>clear</c>, which takes the line out of the world. One point per line, so
/// no line is long, and the module link — which refuses long lines — carries it untouched.
///
/// An objective with no trail to it has no <c>p</c> lines at all, and one whose height is unknown
/// says <c>height unknown</c> after its target. An older module refuses both as not a path, and
/// draws nothing rather than something wrong.
///
/// A <c>name</c> line after the map gives the objective's name for the module's card. A bridge
/// from before it refuses the whole trail, so the phone sends it again without one when refused.
/// </summary>
public static class GuideWire
{
    public const string Header = "guide 1";

    /// <summary>
    /// The most points a path may carry. A trail is thinned to a point every fifteen metres or so,
    /// so this is several kilometres of walk, and at about thirty bytes a point it keeps a request
    /// well inside the bridge's sixteen kilobytes.
    /// </summary>
    public const int MostPoints = 400;

    /// <summary>Far larger than any map, small enough that a number this big is certainly junk.</summary>
    private const double Largest = 100_000;

    /// <summary>
    /// The longest name sent, in characters. Longer than any objective the game names, and short
    /// enough that the line stays well inside the module link's limit whatever the language.
    /// </summary>
    public const int LongestName = 80;

    private const string NameWord = "name ";

    public static string Write(GuidedTrail? path)
    {
        var text = new StringBuilder().Append(Header).Append('\n');

        if (path is null) return text.Append("clear\n").ToString();

        text.Append("map ").Append(path.MapId.ToString(CultureInfo.InvariantCulture)).Append('\n');

        if (OneLine(path.Name) is { } name) text.Append(NameWord).Append(name).Append('\n');

        Point(text.Append("target "), path.Target);

        if (!path.HeightKnown) text.Append("height unknown\n");

        var count = Math.Min(path.Points.Count, MostPoints);

        for (var i = 0; i < count; i++) Point(text.Append("p "), path.Points[i]);

        return text.ToString();
    }

    /// <summary>
    /// The path, or null with <paramref name="clear"/> saying whether that null means "take it
    /// away" or "this was not a path". Strict, because what comes out of here is drawn in somebody's
    /// game: a line through the sky is worse than no line.
    /// </summary>
    public static GuidedTrail? Parse(string? text, out bool clear)
    {
        clear = false;

        if (text is null) return null;

        using var lines = new StringReader(text);

        if (lines.ReadLine()?.Trim() != Header) return null;

        var mapId = 0;
        (double X, double Up, double Z)? target = null;
        var points = new List<(double X, double Up, double Z)>();
        var heightKnown = true;
        string? name = null;

        string? line;

        while ((line = lines.ReadLine()) is not null)
        {
            line = line.Trim();

            if (line.Length == 0) continue;

            // Before the split: a name has spaces in it.
            if (line.StartsWith(NameWord, StringComparison.Ordinal))
            {
                var said = line.Substring(NameWord.Length);
                if (said.Length > LongestName) return null;

                name = OneLine(said);
                continue;
            }

            if (line == "clear")
            {
                clear = true;
                return null;
            }

            if (line == "height unknown")
            {
                heightKnown = false;
                continue;
            }

            var parts = line.Split(' ');

            switch (parts[0])
            {
                case "map" when parts.Length == 2:
                    if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out mapId)) return null;
                    break;

                case "target" when parts.Length == 4:
                    if (ReadPoint(parts) is not { } at) return null;
                    target = at;
                    break;

                case "p" when parts.Length == 4:
                    if (ReadPoint(parts) is not { } point) return null;
                    if (points.Count >= MostPoints) return null;
                    points.Add(point);
                    break;

                default:
                    return null;
            }
        }

        // No points is an objective alone; one point is neither an objective nor a line.
        if (mapId <= 0 || target is not { } place || points.Count == 1) return null;

        return new GuidedTrail(mapId, place, points, heightKnown, name);
    }

    /// <summary>
    /// A name as one line of plain text, cut to <see cref="LongestName"/>; null when nothing is left.
    /// Control characters go, so a name cannot start a second line or hide one.
    /// </summary>
    private static string? OneLine(string? text)
    {
        if (text is null) return null;

        var plain = new StringBuilder(text.Length);

        foreach (var c in text)
            plain.Append(char.IsControl(c) ? ' ' : c);

        var line = plain.ToString().Trim();

        if (line.Length > LongestName) line = line.Substring(0, LongestName - 1).TrimEnd() + "…";

        return line.Length == 0 ? null : line;
    }

    private static void Point(StringBuilder text, (double X, double Up, double Z) point) =>
        text.Append(point.X.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
            .Append(point.Up.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
            .Append(point.Z.ToString("0.##", CultureInfo.InvariantCulture)).Append('\n');

    private static (double X, double Up, double Z)? ReadPoint(string[] parts)
    {
        if (!Number(parts[1], out var x) || !Number(parts[2], out var up) || !Number(parts[3], out var z))
            return null;

        return (x, up, z);
    }

    private static bool Number(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) < Largest;
}
