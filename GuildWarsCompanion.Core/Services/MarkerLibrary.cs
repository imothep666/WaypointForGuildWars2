using System.Globalization;
using System.Text;

using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>What was kept from a pack: a place on a map, and what this app calls it.</summary>
/// <param name="MetresUp">
/// How high it is, in the game's metres, for drawing it in the world: a chest at the top of a
/// jumping puzzle is not at the height of whoever is looking for it. Null for a marker read before
/// heights were kept, or one its pack gave none.
/// </param>
public sealed record StoredMarker(
    int MapId, double MetresX, double MetresZ, TargetKind Kind, string Name, double? MetresUp = null);

/// <summary>
/// What a downloaded pack leaves behind, and how it becomes something the compass can point at.
///
/// <b>Stored raw, converted late.</b> A marker is kept in the pack's own units against its map id,
/// and turned into continent coordinates only when that map is the one being looked at. The
/// alternative — converting at import — needs `map_rect` for every map the pack mentions, which is
/// a request per map for a pack that may name two hundred of them. The map being stood in is
/// already fetched, so the conversion is free at the point it is wanted.
///
/// <b>Lines, not JSON.</b> A tab-separated line per marker: the file is written and read by this
/// class alone, it holds six fields of which five are numbers, and the app's only serializer is a
/// source-generated context for the API's own models. A format that can be read in a text editor
/// is also a format that can be checked when somebody asks why a chest is in the sea.
/// </summary>
public static class MarkerLibrary
{
    /// <summary>The version this file's first line carries, so a later format can refuse an
    /// older file instead of misreading it.</summary>
    public const string Version = "markers 2";

    /// <summary>
    /// The format before heights, still read: a pack stored that way keeps working, without
    /// heights, until its next update re-reads it.
    /// </summary>
    private const string Version1 = "markers 1";

    public static string Write(IEnumerable<StoredMarker> markers)
    {
        var text = new StringBuilder().AppendLine(Version);

        foreach (var marker in markers)
            text.Append(marker.MapId.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(marker.MetresX.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                .Append(marker.MetresZ.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                .Append((int)marker.Kind).Append('\t')
                .Append(marker.MetresUp?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty).Append('\t')
                .AppendLine(Clean(marker.Name));

        return text.ToString();
    }

    public static IReadOnlyList<StoredMarker> Read(string text)
    {
        var markers = new List<StoredMarker>();

        using var lines = new StringReader(text);

        var version = lines.ReadLine()?.Trim();
        var withHeight = version == Version;

        if (!withHeight && version != Version1) return markers;

        while (lines.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;

            var parts = line.Split('\t');

            if (parts.Length < (withHeight ? 6 : 5)) continue;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var map) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z) ||
                !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kind))
                continue;

            double? up = withHeight &&
                         double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
                ? height
                : null;

            markers.Add(new StoredMarker(map, x, z, (TargetKind)kind, parts[withHeight ? 5 : 4], up));
        }

        return markers;
    }

    /// <summary>
    /// Everything a pack has for one map, as targets in continent coordinates.
    ///
    /// <b>A marker that converts to somewhere outside its own map is dropped.</b> It catches junk:
    /// a coordinate in the wrong unit, a marker a pack has simply got wrong, which every pack has
    /// some of. It does <i>not</i> catch a mirrored map — see <see cref="MapSpace.Inside"/> for the
    /// measurement that says so.
    /// </summary>
    public static IReadOnlyList<CompassTarget> Targets(
        IEnumerable<StoredMarker> markers, MapInfo map)
    {
        var found = new List<CompassTarget>();

        if (map.MapArea is not { } area || map.ContinentArea is not { } continent) return found;

        foreach (var marker in markers)
        {
            if (marker.MapId != map.Id) continue;

            var (mapX, mapY) = MapSpace.FromMetres(marker.MetresX, marker.MetresZ);

            if (!MapSpace.Inside(mapX, mapY, area)) continue;

            var at = MapSpace.ToContinent(mapX, mapY, area, continent);

            found.Add(new CompassTarget(
                marker.Name is { Length: > 0 } named ? named : Describe(marker.Kind),
                marker.Kind, map.Id, map.Name, at.X, at.Y, Up: marker.MetresUp));
        }

        return found;
    }

    /// <summary>
    /// How many of a map's markers land outside it, which is how an import reports that the
    /// conversion — or the pack — is wrong rather than leaving it to be found on a dial.
    /// </summary>
    public static int Outside(IEnumerable<StoredMarker> markers, MapInfo map)
    {
        if (map.MapArea is not { } area) return 0;

        var outside = 0;

        foreach (var marker in markers)
        {
            if (marker.MapId != map.Id) continue;

            var (mapX, mapY) = MapSpace.FromMetres(marker.MetresX, marker.MetresZ);

            if (!MapSpace.Inside(mapX, mapY, area)) outside++;
        }

        return outside;
    }

    /// <summary>
    /// How close two markers of the same kind have to be before they are treated as the same
    /// thing, in continent units.
    ///
    /// <b>Measured against two real packs rather than chosen.</b> Metal Marker Myriad and Hero's
    /// Marker Pack both cover Lowland Shore and Janthir Syntri, and where they mark the same
    /// in-game chest they agree to within a unit — the closest pairs are 0.1 to 0.7 apart, because
    /// both authors stood on the same object. Then there is a gap: on Lowland Shore 8 pairs fall
    /// within 10 units, 9 within 25, and still 9 at 100. Only past about 200 does the count climb
    /// again, and that climb is distinct chests in the same farming spot being matched to each
    /// other — at 400 units it would merge 21 of 32 markers, which is not deduplication but data
    /// loss.
    ///
    /// Twenty-five sits in the flat part of that curve: past every real duplicate, far short of
    /// the first genuine neighbour.
    /// </summary>
    public const double SameThing = 25;

    /// <summary>
    /// Drops the second copy of anything two packs both carry.
    ///
    /// Only within a kind: a chest and a jumping puzzle at the same spot are two different things
    /// to say, and a farming route often has both. Order decides the survivor, so the caller's
    /// pack order is the tie-break — first pack added wins, which is at least predictable.
    ///
    /// Quadratic, deliberately. This runs over one map's markers, which is tens, at the moment a
    /// map is loaded; a grid index would be faster and would have to be right.
    /// </summary>
    public static IReadOnlyList<CompassTarget> Deduplicate(
        IReadOnlyList<CompassTarget> targets, double within = SameThing)
    {
        var kept = new List<CompassTarget>(targets.Count);

        foreach (var target in targets)
        {
            var seen = false;

            foreach (var already in kept)
            {
                if (already.Kind != target.Kind) continue;

                if (Bearing.Distance(already.X, already.Y, target.X, target.Y) > within) continue;

                seen = true;

                break;
            }

            if (!seen) kept.Add(target);
        }

        return kept;
    }

    private static string Describe(TargetKind kind) =>
        kind == TargetKind.JumpingPuzzle ? L.T("Jumping puzzle") : L.T("Chest");

    /// <summary>A name with nothing in it that would break the line it is written on.</summary>
    private static string Clean(string? name) =>
        name is null ? string.Empty : name.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
