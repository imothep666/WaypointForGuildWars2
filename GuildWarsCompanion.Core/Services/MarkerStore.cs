using System.Net;
using System.Net.Http.Headers;

using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Services;

/// <summary>What an import did, in one sentence and a count.</summary>
public sealed record MarkerImport(int Kept, int Maps, string Note, bool Worked);

/// <summary>
/// The downloaded marker packs: fetching them, keeping them, and answering for a map.
///
/// <b>Downloaded rather than shipped, and that is a licence decision as much as a size one.</b> A
/// pack is somebody else's work published on their own terms. Fetching the file the player points
/// at is ordinary use of a published URL; putting a copy inside the APK is redistribution, and
/// would need a per-pack licence this repo has no way to check. The URLs are the player's to
/// choose and nothing is bundled.
///
/// <b>Several packs, because no one pack covers the ground.</b> Metal Marker Myriad and Hero's
/// Marker Pack both carry chests on Lowland Shore and each has ones the other has not; TacO
/// itself assumes a handful are enabled at once. Each gets its own file, keyed by a hash of its
/// URL, so removing one costs deleting one file and a failed download cannot damage a pack that
/// was already working.
///
/// <b>Kept in <see cref="AppEnvironment.DataDirectory"/>, not the cache.</b> A pack is a deliberate
/// download of tens of megabytes over somebody's mobile data; the cache directory is the one
/// Android empties when it feels like it, which would silently cost that download again. What is
/// stored is not the pack — it is the few thousand lines got out of it. The <c>.taco</c> itself is
/// deleted once read.
/// </summary>
public sealed class MarkerStore(AppEnvironment environment)
{
    private const string IndexFile = "packs.tsv";

    /// <summary>
    /// A ceiling on what is kept from one pack, after the filtering.
    ///
    /// A large pack carries tens of thousands of markers across the whole world; chests and
    /// jumping puzzles are a small slice of that.
    /// </summary>
    private const int MostKept = 20000;

    /// <summary>
    /// What this app's own import collects, as a number that goes up when that changes.
    ///
    /// <b>It exists because a pack that has not changed answers 304, and an import that has
    /// changed still needs the body.</b> Routes were added long after the first packs were read:
    /// those packs are unchanged on their servers, so every update would answer "already up to
    /// date" and they would never grow the half the app had just learned to read. When the stored
    /// number is older than this one, the validators are left off and the body is fetched.
    ///
    /// 1 was markers alone. 2 added routes and the colours a pack states for them. 3 raised the
    /// route ceiling from 4000, which Metal Marker Myriad had been silently truncated to — and
    /// that is the second time this number has earned itself: what an import collects changed
    /// again, and without bumping it every pack already here would have kept the truncation.
    /// 4 kept each route point's height, for the trail guide, and raised the points read per trail
    /// from 4000 — see <see cref="TrailPack.MostPoints"/>. Both change what an unchanged pack
    /// yields, so both needed the body again.
    /// Not raised when the marker ceiling stopped counting discarded markers (2026-10-06): measured
    /// against Lady Elyssa's Guides, Metal Marker Myriad and Hero's pack, their stored reads were
    /// already complete, and only Tekkit's All-In-One, which sends no validators and so re-reads on
    /// every update anyway, had been cut short.
    /// 5 kept each marker's height, for drawing the chosen one in the game — a chest at the top of
    /// a jumping puzzle is not at the height of whoever is looking for it.
    /// </summary>
    private const int ImportFormat = 5;

    /// <summary>The ceiling on routes kept from one pack. See <see cref="MarkerPack.ReadRoutes"/>
    /// for what it costs and why it is this number.</summary>
    private const int MostRoutes = 20000;

    private readonly HttpClient _http = Client();

    /// <summary>
    /// A client that says who it is.
    ///
    /// Some hosts refuse a request with no <c>User-Agent</c> at all, and naming the client is
    /// ordinary manners when fetching somebody's file. It is the app's own name, <b>not</b> a
    /// browser's: tekkitsworkshop.net answers 403 to this, to a full Chrome string, and to a
    /// request carrying its own download page as the referer, so its block is not about the agent
    /// and pretending to be Chrome would buy nothing except a lie in a header.
    /// </summary>
    private static HttpClient Client()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        http.DefaultRequestHeaders.UserAgent.ParseAdd("GuildWarsCompanion/1.0");

        return http;
    }

    private List<MarkerPackRecord>? _packs;

    private readonly Dictionary<string, IReadOnlyList<StoredMarker>> _markers = [];

    private readonly Dictionary<string, IReadOnlyList<StoredRoute>> _routes = [];

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _names = [];

    /// <summary>Every pack on the device, in the order they were added.</summary>
    public IReadOnlyList<MarkerPackRecord> Packs => Index();

    public bool HasPacks => Index().Count > 0;

    /// <summary>
    /// Everything every pack holds for one map, as targets, with duplicates removed.
    ///
    /// Two packs marking the same chest is the normal case, not the exception — see
    /// <see cref="MarkerLibrary.SameThing"/> for the measurement behind the threshold.
    /// </summary>
    public IReadOnlyList<CompassTarget> OnMap(MapInfo map)
    {
        if (Index().Count == 0) return [];

        var found = new List<CompassTarget>();

        foreach (var pack in Index())
            found.AddRange(MarkerLibrary.Targets(Markers(pack.Id), map));

        return found.Count == 0 ? found : MarkerLibrary.Deduplicate(found);
    }

    /// <summary>
    /// Every route every pack has for one map, in continent coordinates and named in the
    /// author's own words.
    ///
    /// Not deduplicated, unlike the markers. Two packs marking the same chest are saying the same
    /// thing twice; two packs routing the same map completion are two opinions about how to walk
    /// it, and merging them by proximity would produce a line neither author drew.
    /// </summary>
    public IReadOnlyList<RouteLine> RoutesOnMap(MapInfo map)
    {
        if (Index().Count == 0) return [];

        var found = new List<RouteLine>();

        foreach (var pack in Index())
        {
            var names = Names(pack.Id);
            var routes = Routes(pack.Id);

            // Named against the other categories on this map, not on their own: two branches of
            // one pack can end in the same two segments, and two rows reading the same in the
            // chooser cannot be told apart. See RouteNames.Distinguish.
            var said = RouteNames.Distinguish(
                routes.Where(route => route.MapId == map.Id).Select(route => route.Type).Distinct(),
                names);

            // The pack's own name in front, when more than one pack has routes here. Two packs
            // routing the same zone name their categories in their own words and nothing else
            // tells the rows apart — and with a single pack installed, which is the ordinary
            // case, the prefix would be the same on every row and say nothing.
            var prefix = Index().Count(p => Routes(p.Id).Any(route => route.MapId == map.Id)) > 1
                ? pack.Name + " · "
                : string.Empty;

            found.AddRange(RouteLibrary.Lines(
                routes, map,
                type => prefix + (said.GetValueOrDefault(type) ?? RouteNames.Describe(type, names))));
        }

        return found;
    }

    /// <summary>
    /// Adds a pack, or re-fetches one that is already here.
    ///
    /// Everything that can go wrong is somebody else's server or somebody else's file, so nothing
    /// throws: the result says what happened and the screen shows it.
    /// </summary>
    public async Task<MarkerImport> AddAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new MarkerImport(0, 0, "Paste the address of a .taco pack first.", false);

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var address) ||
            address.Scheme != Uri.UriSchemeHttps)
            return new MarkerImport(0, 0, "That is not an https address.", false);

        var id = MarkerIndex.Name(url);
        var known = Index().FirstOrDefault(p => p.Id == id);

        return await FetchAsync(address, id, known, ct);
    }

    /// <summary>
    /// Asks whether a pack has changed, and fetches it only if it has.
    ///
    /// <b>The check is free when nothing moved.</b> The server's <c>ETag</c> and
    /// <c>Last-Modified</c> are kept from the last fetch and sent back as <c>If-None-Match</c> and
    /// <c>If-Modified-Since</c>; an unchanged pack answers <c>304 Not Modified</c> with no body, so
    /// checking a seven-megabyte pack costs one request rather than seven megabytes. Verified
    /// against a real pack URL before this was written.
    ///
    /// This is also why the app does not copy TacO's own approach, which scrapes a version string
    /// out of a web page per pack — that needs configuration for every pack and breaks whenever a
    /// site is redesigned. A host that sets no validator simply re-downloads, which is correct and
    /// merely not free.
    /// </summary>
    public async Task<MarkerImport> UpdateAsync(string id, CancellationToken ct = default)
    {
        if (Index().FirstOrDefault(p => p.Id == id) is not { } pack)
            return new MarkerImport(0, 0, "That pack is not here any more.", false);

        if (!Uri.TryCreate(pack.Url, UriKind.Absolute, out var address))
            return new MarkerImport(0, 0, "That pack's address is no longer valid.", false);

        return await FetchAsync(address, id, pack, ct);
    }

    /// <summary>
    /// Asks every pack's server whether anything changed.
    ///
    /// Sequential rather than parallel: these are somebody else's servers, the answer is usually
    /// 304 and arrives in a moment, and three at once would be rude for no gain.
    /// </summary>
    public async Task<MarkerImport> UpdateAllAsync(CancellationToken ct = default)
    {
        if (Index().Count == 0)
            return new MarkerImport(0, 0, "No packs to check.", false);

        var fresh = 0;
        var same = 0;
        var failed = 0;
        var kept = 0;

        foreach (var pack in Index().ToList())
        {
            var done = await UpdateAsync(pack.Id, ct);

            kept += done.Kept;

            if (!done.Worked) failed++;
            else if (done.Note == UpToDate) same++;
            else fresh++;
        }

        // Said plainly rather than as one number: "3 packs" hides that two of them failed.
        var said = new List<string>();

        if (fresh > 0) said.Add($"{fresh} updated");
        if (same > 0) said.Add($"{same} already up to date");
        if (failed > 0) said.Add($"{failed} could not be reached");

        return new MarkerImport(kept, 0, string.Join(" · ", said) + ".", failed == 0);
    }

    /// <summary>The words a 304 answers with, in one place so <see cref="UpdateAllAsync"/> can
    /// recognise them rather than guess from a count.</summary>
    private const string UpToDate = "Already up to date.";

    /// <summary>Puts one pack down, leaving the others alone.</summary>
    public void Remove(string id)
    {
        var packs = Index().Where(p => p.Id != id).ToList();

        _markers.Remove(id);
        _routes.Remove(id);
        _names.Remove(id);

        Delete(File(id + ".tsv"));
        Delete(File(id + ".routes"));
        Delete(File(id + ".names"));

        Save(packs);
    }

    private async Task<MarkerImport> FetchAsync(
        Uri address, string id, MarkerPackRecord? known, CancellationToken ct)
    {
        var pack = File(id + ".taco");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);

            // Only when this pack is already here, the server told us how to ask, and the last
            // read collected everything this version wants. A first fetch sends neither and gets
            // the body; so does a pack whose stored read is older than ImportFormat.
            if (known is { } held && held.Imported >= ImportFormat)
            {
                if (held.Tag is { Length: > 0 } tag &&
                    EntityTagHeaderValue.TryParse(tag, out var parsed))
                    request.Headers.IfNoneMatch.Add(parsed);

                if (held.Modified is { Length: > 0 } modified &&
                    DateTimeOffset.TryParse(modified, out var since))
                    request.Headers.IfModifiedSince = since;
            }

            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == HttpStatusCode.NotModified)
                return new MarkerImport(known?.Count ?? 0, known?.Maps ?? 0, UpToDate, true);

            response.EnsureSuccessStatusCode();

            // To disk rather than into memory: packs run to hundreds of megabytes of textures,
            // and the phone has no reason to hold one.
            await using (var body = await response.Content.ReadAsStreamAsync(ct))
            await using (var file = System.IO.File.Create(pack))
                await body.CopyToAsync(file, ct);

            List<StoredMarker> kept = [];

            await using (var file = System.IO.File.OpenRead(pack))
                foreach (var marker in MarkerPack.Read(file, MostKept, type => MarkerKinds.Of(type) is not null))
                {
                    if (MarkerKinds.Of(marker.Type) is not { } kind) continue;

                    kept.Add(new StoredMarker(
                        marker.MapId, marker.MetresX, marker.MetresZ, kind,
                        marker.Name ?? string.Empty, marker.MetresY));

                    if (kept.Count >= MostKept) break;
                }

            // The other half of a pack. Read in its own pass over the zip rather than folded into
            // the one above: the markers come out of the XML alone, the routes need the XML and
            // then a binary file per trail, and a pack that carries only one of the two is the
            // normal case — Teh's Trails imports no markers at all and 490 trails.
            IReadOnlyList<StoredRoute> routes;
            IReadOnlyList<PackCategory> categories;

            await using (var file = System.IO.File.OpenRead(pack))
                (routes, categories) = MarkerPack.ReadRoutes(file, MostRoutes);

            await System.IO.File.WriteAllTextAsync(
                File(id + ".tsv"), MarkerLibrary.Write(kept), ct);

            await System.IO.File.WriteAllTextAsync(
                File(id + ".routes"), RouteLibrary.Write(routes), ct);

            await System.IO.File.WriteAllTextAsync(
                File(id + ".names"), RouteNames.Write(categories), ct);

            _markers[id] = kept;
            _routes[id] = routes;
            _names[id] = RouteNames.Lookup(categories);

            var maps = kept.Select(m => m.MapId).Distinct().Count();

            var record = new MarkerPackRecord(
                id,
                address.ToString(),
                MarkerIndex.Title(address.ToString(),
                    response.Content.Headers.ContentDisposition?.FileNameStar
                    ?? response.Content.Headers.ContentDisposition?.FileName),
                response.Headers.ETag?.ToString() ?? string.Empty,
                response.Content.Headers.LastModified?.ToString("R") ?? string.Empty,
                kept.Count,
                maps,
                routes.Count,
                ImportFormat);

            var packs = Index().Where(p => p.Id != id).ToList();

            packs.Add(record);

            Save(packs);

            // Said as two counts, because a pack carrying one and not the other is the normal
            // case rather than a failure: Teh's Trails has 490 routes and no chest at all, and
            // "nothing was imported" would be wrong about it in both directions.
            var said = new List<string>();

            if (kept.Count > 0) said.Add($"{kept.Count} markers across {maps} maps");

            // A count that is exactly the ceiling is a truncation wearing a number's clothes:
            // Metal Marker Myriad read "4000 routes" and meant "4000 of however many there are".
            if (routes.Count > 0)
                said.Add(routes.Count >= MostRoutes
                    ? $"{routes.Count} routes — the most this app keeps"
                    : $"{routes.Count} routes");

            return new MarkerImport(
                kept.Count, maps,
                said.Count == 0
                    ? "Nothing in that pack was a chest, a jumping puzzle or a route."
                    : string.Join(" · ", said) + ".",
                said.Count > 0);
        }
        catch (OperationCanceledException)
        {
            return new MarkerImport(0, 0, "Download stopped.", false);
        }
        catch (Exception ex)
        {
            // The address, the network, the zip, the disk. All of them are the same answer to the
            // player — it did not work, and here is what was said.
            return new MarkerImport(0, 0, ex.Message, false);
        }
        finally
        {
            // The pack has been read; keeping a hundred megabytes of somebody else's textures on
            // the phone would be storing the thing this deliberately does not ship.
            Delete(pack);
        }
    }

    private IReadOnlyList<StoredMarker> Markers(string id)
    {
        if (_markers.TryGetValue(id, out var already)) return already;

        try
        {
            var path = File(id + ".tsv");

            return _markers[id] = System.IO.File.Exists(path)
                ? MarkerLibrary.Read(System.IO.File.ReadAllText(path))
                : [];
        }
        catch (Exception)
        {
            return _markers[id] = [];
        }
    }

    private IReadOnlyList<StoredRoute> Routes(string id)
    {
        if (_routes.TryGetValue(id, out var already)) return already;

        try
        {
            var path = File(id + ".routes");

            return _routes[id] = System.IO.File.Exists(path)
                ? RouteLibrary.Read(System.IO.File.ReadAllText(path))
                : [];
        }
        catch (Exception)
        {
            return _routes[id] = [];
        }
    }

    private IReadOnlyDictionary<string, string> Names(string id)
    {
        if (_names.TryGetValue(id, out var already)) return already;

        try
        {
            var path = File(id + ".names");

            return _names[id] = System.IO.File.Exists(path)
                ? RouteNames.Read(System.IO.File.ReadAllText(path))
                : new Dictionary<string, string>();
        }
        catch (Exception)
        {
            return _names[id] = new Dictionary<string, string>();
        }
    }

    private List<MarkerPackRecord> Index()
    {
        if (_packs is not null) return _packs;

        // The single-pack format wrote one markers.tsv with no index beside it. Nothing reads it
        // now, so it is dead weight on the device until it is removed — and a file nothing reads
        // is exactly the sort of thing that gets found years later and puzzled over. The pack
        // itself is re-added by its URL, which the player still has.
        Delete(File("markers.tsv"));

        try
        {
            var path = File(IndexFile);

            return _packs = System.IO.File.Exists(path)
                ? MarkerIndex.Read(System.IO.File.ReadAllText(path)).ToList()
                : [];
        }
        catch (Exception)
        {
            return _packs = [];
        }
    }

    private void Save(List<MarkerPackRecord> packs)
    {
        _packs = packs;

        try
        {
            System.IO.File.WriteAllText(File(IndexFile), MarkerIndex.Write(packs));
        }
        catch (Exception)
        {
            // The packs are still usable this run; the list comes back empty on the next one,
            // which is visible and recoverable rather than silent.
        }
    }

    private static void Delete(string path)
    {
        try
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        catch (Exception)
        {
            // A file that will not delete costs disk and nothing else.
        }
    }

    private string File(string name)
    {
        var folder = Path.Combine(environment.DataDirectory, "markers");

        Directory.CreateDirectory(folder);

        return Path.Combine(folder, name);
    }
}
