using System.Globalization;
using System.IO.Compression;
using System.Xml;

namespace GuildWarsCompanion.Services;

/// <summary>One marker as a pack publishes it, before anything has been decided about it.</summary>
/// <param name="MetresX">The avatar-space position a pack stores: metres, x east, y up, z south.</param>
/// <param name="Type">The category path, dotted — <c>mypack.chests.blackcitadel</c>.</param>
public sealed record PackMarker(
    int MapId, double MetresX, double? MetresY, double MetresZ, string Type, string? Name);

/// <summary>
/// A trail as the XML declares it: a category path, and the <c>.trl</c> inside the same zip that
/// holds the line itself.
/// </summary>
/// <param name="Colour">The <c>color</c> the trail states, or null. See <see cref="RouteColour"/>.</param>
public sealed record PackTrailRef(string Type, string File, string? Colour = null);

/// <summary>
/// One node of a pack's own category tree.
/// </summary>
/// <param name="Name">The segment as it appears in a dotted type path — <c>mt</c>.</param>
/// <param name="DisplayName">What the author calls it — "Main Trail". Empty when unnamed.</param>
/// <param name="IsSeparator">
/// A heading rather than a category. Packs use them as dividers — Teh's Trails has
/// <c>|=====Core Tyria=====|</c> — and they are never the end of a type path.
/// </param>
/// <param name="Colour">A <c>color</c> this category states, inherited by what is under it.</param>
/// <param name="Alpha">An <c>alpha</c> this category states, likewise.</param>
public sealed record PackCategory(
    string Name, string DisplayName, bool IsSeparator, string? Colour = null, string? Alpha = null);

/// <summary>
/// Reading a TacO marker pack.
///
/// A <c>.taco</c> is a zip of XML documents. Each declares a tree of <c>MarkerCategory</c> and a
/// list of <c>POI</c> elements carrying a map id, a position in metres and a dotted category path.
/// That is the whole of what this needs; trails, textures, behaviours, festival filters and the
/// rest of the format are skipped rather than modelled.
///
/// <b>Why a pack at all, when the bosses did not need one.</b> Chests and jumping puzzles are not
/// events, so <c>/v1/event_details.json</c> has nothing to say about them and no endpoint anywhere
/// does — see <c>### Boss arenas</c> for the search that settled that. A community pack is the
/// only published source, which is also why the app downloads one rather than shipping it: the
/// file stays the publisher's, fetched from wherever the player points it.
///
/// <b>Read leniently.</b> Packs are written by hand and by a dozen different tools: attribute
/// casing varies (<c>MapID</c>, <c>mapid</c>), numbers may be absent, and a malformed document is
/// common enough that one must not cost the rest. Anything unreadable is skipped and the reading
/// continues.
/// </summary>
public static class MarkerPack
{
    /// <summary>
    /// Every marker in a pack, from its zip.
    /// </summary>
    /// <param name="limit">A ceiling on how many are kept. Packs run to tens of thousands of
    /// markers and a phone has no reason to hold a continent's worth of chests in memory.</param>
    /// <param name="keep">
    /// Which category paths are wanted, asked before a marker counts against <paramref name="limit"/>.
    /// Without it the limit was spent on everything: Tekkit's All-In-One has 56,368 POIs, mostly
    /// Super Adventure Box steps and fishing holes, and the first 20,000 held 314 of its chests and
    /// puzzles where the whole pack holds about 1,900.
    /// </param>
    public static IReadOnlyList<PackMarker> Read(Stream zip, int limit = 20000, Func<string, bool>? keep = null)
    {
        var found = new List<PackMarker>();

        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            if (found.Count >= limit) break;

            if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                using var stream = entry.Open();

                ReadDocument(stream, found, limit, keep);
            }
            catch (Exception)
            {
                // One unreadable document in a pack of thirty must not cost the other
                // twenty-nine. A pack that is entirely unreadable answers empty, and the caller
                // says so rather than pretending it imported something.
            }
        }

        return found;
    }

    /// <summary>
    /// Every route in a pack: the XML says which <c>.trl</c> belongs to which category, and the
    /// <c>.trl</c> holds the line.
    /// </summary>
    /// <param name="mostRoutes">
    /// A ceiling on runs kept, matching the markers' own.
    ///
    /// <b>Raised from 4000 after it bit.</b> Metal Marker Myriad reported exactly 4000 routes on
    /// import, which is the shape of a truncation rather than a count — it carries 5409 trails and
    /// a trail splits into several runs. Measured cost: 4000 runs is 3.5 MB on disk, about 880
    /// bytes each, so a whole large pack is under ten megabytes against the seven the
    /// <c>.taco</c> itself took to download and which is deleted after reading.
    /// </param>
    public static (IReadOnlyList<StoredRoute> Routes, IReadOnlyList<PackCategory> Categories)
        ReadRoutes(Stream zip, int mostRoutes = 20000)
    {
        var routes = new List<StoredRoute>();
        var categories = new List<PackCategory>();

        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);

        // The zip's own entry names, because a pack writes its trailData paths with whatever
        // separator and casing its author's machine used. Matching on the leaf name would be
        // ambiguous — Teh's Trails has AS.trl and AS-Segment-0.trl in one folder — so the whole
        // path is normalised instead.
        var entries = archive.Entries.ToDictionary(
            entry => Normalise(entry.FullName), entry => entry, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                using var stream = entry.Open();

                var (declared, found) = ReadTrails(stream);

                categories.AddRange(found);

                // What the categories in this document state, for the trails in it. A pack's
                // attributes inherit down its tree and a type path carries its own ancestry, so
                // these two tables plus the path recover the inheritance — see RouteColour.
                var colours = Stated(found, category => category.Colour);
                var alphas = Stated(found, category => category.Alpha);

                foreach (var trail in declared)
                {
                    if (routes.Count >= mostRoutes) break;

                    if (!entries.TryGetValue(Normalise(trail.File), out var file)) continue;

                    using var line = file.Open();

                    if (TrailPack.Read(line) is not { } read) continue;

                    var argb = RouteColour.Resolve(trail.Colour, trail.Type, colours, alphas);

                    for (var run = 0; run < read.Segments.Count; run++)
                    {
                        if (routes.Count >= mostRoutes) break;

                        var (points, heights) = RouteLibrary.Thin(
                            read.Segments[run], read.Heights?[run]);

                        routes.Add(new StoredRoute(read.MapId, trail.Type, points, argb, heights));
                    }
                }
            }
            catch (Exception)
            {
                // One unreadable document, or one trail file that is not one, must not cost the
                // rest of the pack. The same rule the markers are read under.
            }
        }

        return (routes, categories);
    }

    /// <summary>A zip path as this compares them: forward slashes, no leading one.</summary>
    private static string Normalise(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    /// <summary>
    /// One attribute of every category that states it, by segment name.
    ///
    /// First wins, as in <see cref="RouteNames.Lookup"/> and for the same reason: two branches
    /// can use the same short segment name, and without the tree there is nothing to prefer
    /// between them except order, which is at least stable.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Stated(
        IEnumerable<PackCategory> categories, Func<PackCategory, string?> of)
    {
        var stated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in categories)
            if (of(category) is { Length: > 0 } value)
                stated.TryAdd(category.Name, value);

        return stated;
    }

    /// <summary>
    /// The trails a document declares, and the category names it uses along the way.
    ///
    /// Read in one pass because they arrive interleaved: a <c>MarkerCategory</c> tree with
    /// <c>Trail</c> and <c>POI</c> elements inside it. The categories are wanted for their
    /// <c>DisplayName</c> alone — a type path reads <c>tt.mc.mt.th.t</c> and a person choosing
    /// what to show needs "Teh's Trails › Map Completion › Main Trail".
    ///
    /// <b>Only the segment names are kept, not the shape of the tree.</b> A type path already
    /// carries its own ancestry, so a lookup from segment to display name is enough to write the
    /// whole path out in words. Measured on Teh's Trails: 513 category elements against 22
    /// distinct trail paths, so the tree is mostly detail about markers this app does not carry.
    /// </summary>
    public static (IReadOnlyList<PackTrailRef> Trails, IReadOnlyList<PackCategory> Categories)
        ReadTrails(Stream xml, int limit = 20000)
    {
        var trails = new List<PackTrailRef>();
        var categories = new List<PackCategory>();

        using var reader = XmlReader.Create(xml, Settings());

        while (reader.Read())
        {
            if (trails.Count >= limit) break;

            if (reader.NodeType != XmlNodeType.Element) continue;

            if (reader.Name.Equals("MarkerCategory", StringComparison.OrdinalIgnoreCase))
            {
                if (Attribute(reader, "name") is { Length: > 0 } name)
                    categories.Add(new PackCategory(
                        name,
                        Attribute(reader, "DisplayName") ?? string.Empty,
                        Attribute(reader, "isseparator") is "1" or "true",
                        Attribute(reader, "color"),
                        Attribute(reader, "alpha")));

                continue;
            }

            if (!reader.Name.Equals("Trail", StringComparison.OrdinalIgnoreCase)) continue;

            // A trail with no file is a declaration of nothing; a trail with no type still has a
            // line worth drawing, and lands under the empty path rather than being dropped.
            if (Attribute(reader, "trailData") is { Length: > 0 } file)
                trails.Add(new PackTrailRef(
                    Attribute(reader, "type") ?? string.Empty, file, Attribute(reader, "color")));
        }

        return (trails, categories);
    }

    /// <summary>Every marker in one XML document. Public for the tests, which have no zip.</summary>
    public static IReadOnlyList<PackMarker> ReadDocument(Stream xml, int limit = 20000)
    {
        var found = new List<PackMarker>();

        ReadDocument(xml, found, limit);

        return found;
    }

    /// <summary>
    /// How a stranger's document is read.
    ///
    /// A pack is a downloaded file from somebody else. None of these are needed to read one, and
    /// each is a way for a document to reach off the device or spend the afternoon expanding
    /// itself.
    /// </summary>
    private static XmlReaderSettings Settings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreWhitespace = true,
        CheckCharacters = false
    };

    private static void ReadDocument(Stream xml, List<PackMarker> into, int limit, Func<string, bool>? keep = null)
    {
        using var reader = XmlReader.Create(xml, Settings());

        while (reader.Read())
        {
            if (into.Count >= limit) return;

            if (reader.NodeType != XmlNodeType.Element) continue;

            if (!reader.Name.Equals("POI", StringComparison.OrdinalIgnoreCase)) continue;

            if (Marker(reader) is { } marker && (keep is null || keep(marker.Type))) into.Add(marker);
        }
    }

    private static PackMarker? Marker(XmlReader reader)
    {
        if (Attribute(reader, "MapID") is not { } mapText ||
            !int.TryParse(mapText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mapId))
            return null;

        if (Number(reader, "xpos") is not { } x ||
            Number(reader, "zpos") is not { } z)
            return null;

        var type = Attribute(reader, "type") ?? string.Empty;

        // No height is no height, not ground level: a chest drawn at zero would stand in the sea.
        return new PackMarker(mapId, x, Number(reader, "ypos"), z, type, Attribute(reader, "name"));
    }

    /// <summary>
    /// An attribute by name, whatever case the pack wrote it in.
    ///
    /// <c>XmlReader.GetAttribute</c> is case-sensitive and packs are not consistent — the same
    /// tool writes <c>MapID</c> in one file and <c>mapID</c> in another.
    /// </summary>
    private static string? Attribute(XmlReader reader, string name)
    {
        if (!reader.HasAttributes) return null;

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);

            if (reader.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                var value = reader.Value;

                reader.MoveToElement();

                return value;
            }
        }

        reader.MoveToElement();

        return null;
    }

    /// <summary>
    /// A number, always read in the invariant culture.
    ///
    /// A pack writes <c>-17.5</c> and a German phone reads that as minus one hundred and
    /// seventy-five if the culture is allowed a say. Every coordinate in this app is a machine
    /// number and none of them are ever parsed any other way.
    /// </summary>
    private static double? Number(XmlReader reader, string name) =>
        Attribute(reader, name) is { } text &&
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
