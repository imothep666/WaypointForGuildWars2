using System.Buffers.Binary;

namespace GuildWarsCompanion.Services;

/// <summary>
/// One route out of a pack: a map, and the line to walk, in the pack's own metres.
/// </summary>
/// <param name="Segments">
/// One run of points each. A trail is not always continuous — a route that ports, or that the
/// author drew in pieces, is stored as several runs in one file — so joining them would draw a
/// straight line across the map between the end of one and the start of the next.
/// </param>
/// <param name="Heights">
/// The height of every point, in metres, run for run alongside <paramref name="Segments"/>. Kept
/// apart rather than folded into the points, because the map is flat and everything that draws a
/// route wants x and z alone; only the trail guide asks how far up or down the way goes.
/// </param>
public sealed record PackTrail(
    int MapId,
    IReadOnlyList<IReadOnlyList<(double X, double Z)>> Segments,
    IReadOnlyList<IReadOnlyList<double>>? Heights = null)
{
    public int Points => Segments.Sum(segment => segment.Count);
}

/// <summary>
/// Reading a TacO <c>.trl</c>, which is the other half of a marker pack and the only binary part
/// of it.
///
/// <b>The format was measured, not looked up.</b> Teh's Trails, 526 <c>.trl</c> entries: every one
/// begins <c>int32</c> version 0, then <c>int32</c> map id, then a whole number of 12-byte groups
/// — three little-endian <c>float32</c>, x, y and z in the same avatar-space metres the markers
/// use. Not one of the 526 had a byte left over, and the map ids are ordinary ones (15 is
/// Queensdale). 54338 points across 52 maps, a median of 45 per trail and 926 at the longest.
///
/// <b>A zero triple is a break, not a point.</b> (0,0,0) turns up inside files that are otherwise
/// perfectly ordinary — the very first trail read had one — and it is the format's way of lifting
/// the pen. Drawn as a position it would put a route through the north-west corner of every map.
///
/// <b>Most breaks are not gaps, so a short one is joined over.</b> Measured on Lady Elyssa's
/// Silverwastes map completion: 76 breaks, 62 of them under five metres and the median 1.3 — the
/// author pausing the recorder, not going anywhere. The rest are a glide or a drop at 18 to 38
/// metres, and three waypoint ports at 490 and 798. Kept as breaks, the pauses cut one walk into
/// 249 pieces, and a guide handed a piece 26 units long is a straight line again. Joined within
/// <see cref="JoinWithin"/>, a trail is the walk its author made, broken only where they ported.
///
/// <b>y is height</b>, and is kept apart from x and z: the map is flat, so drawing needs only the
/// two, but a hidden chamber is exactly the place where "the way goes 25 metres down" is the one
/// thing worth knowing. See <see cref="MapSpace"/> for the conversion the rest shares.
/// </summary>
public static class TrailPack
{
    /// <summary>Bytes of header before the points: a version and a map id.</summary>
    private const int Header = 8;

    /// <summary>Three floats.</summary>
    private const int PointSize = 12;

    /// <summary>
    /// The widest break joined over, in metres. Above the 38-metre glides and drops measured, far
    /// below the ports: a straight line of forty metres is a jump somebody made, of five hundred a
    /// loading screen.
    /// </summary>
    public const double JoinWithin = 40;

    /// <summary>
    /// The only version seen in the wild.
    ///
    /// Refused rather than guessed at: a different number means a layout this reader has never
    /// been measured against, and reading it anyway would produce coordinates that look like
    /// coordinates.
    /// </summary>
    private const int KnownVersion = 0;

    /// <summary>
    /// A route from one <c>.trl</c>, or null when the file is not one this reader understands.
    /// </summary>
    /// <param name="mostPoints">
    /// A ceiling per trail, before thinning.
    ///
    /// <b>Raised from 4000 after it cut a route short.</b> Lady Elyssa's chest run through the
    /// Silverwastes is 7778 raw points, and at 4000 it stopped before the last three places it
    /// visits — a route that ends early is a route that lies about where it goes. Thinning to
    /// <see cref="RouteLibrary.Apart"/> afterwards takes that run down to a few hundred, so the
    /// ceiling guards against a broken file rather than against a long walk.
    /// </param>
    public static PackTrail? Read(Stream trail, int mostPoints = MostPoints)
    {
        using var memory = new MemoryStream();

        trail.CopyTo(memory);

        return Read(memory.ToArray(), mostPoints);
    }

    public const int MostPoints = 20000;

    /// <summary>The same, for a file already in hand. Public for the tests, which build one.</summary>
    public static PackTrail? Read(ReadOnlySpan<byte> bytes, int mostPoints = MostPoints)
    {
        if (bytes.Length < Header) return null;

        if (BinaryPrimitives.ReadInt32LittleEndian(bytes) != KnownVersion) return null;

        var mapId = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);

        // A pack with a nonsense map id is a pack this app cannot place anything from: the map's
        // own rectangle is what turns metres into a position, and there is no rectangle for -1.
        if (mapId <= 0) return null;

        var body = bytes[Header..];
        var count = body.Length / PointSize;

        if (count == 0) return null;

        var segments = new List<IReadOnlyList<(double X, double Z)>>();
        var heights = new List<IReadOnlyList<double>>();
        var run = new List<(double X, double Z)>();
        var up = new List<double>();

        // Set by a zero triple and settled by the next real point: joined if it lands close to
        // where the pen lifted, a new run if it does not.
        var lifted = false;

        for (var i = 0; i < count && i < mostPoints; i++)
        {
            var at = i * PointSize;

            var x = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(body[at..]));
            var y = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(body[(at + 4)..]));
            var z = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(body[(at + 8)..]));

            // The pen lifting. Everything after it is a new run.
            if (x == 0 && y == 0 && z == 0)
            {
                lifted = true;

                continue;
            }

            // A pack written by a broken tool, or a file that is not a trail at all. One bad
            // point ends the run rather than bending it across the map.
            if (!float.IsFinite(x) || !float.IsFinite(z))
            {
                Close(segments, heights, run, up);

                run = [];
                up = [];
                lifted = false;

                continue;
            }

            if (lifted && run.Count > 0)
            {
                var (lastX, lastZ) = run[^1];
                var gap = Math.Sqrt((x - lastX) * (x - lastX) + (z - lastZ) * (z - lastZ));

                if (gap > JoinWithin)
                {
                    Close(segments, heights, run, up);

                    run = [];
                    up = [];
                }
            }

            lifted = false;

            run.Add((x, z));

            // A height that is not a number is a height nobody can use, not a reason to lose the
            // point: the line is still right, and NaN is how "not known" travels from here on.
            up.Add(float.IsFinite(y) ? y : double.NaN);
        }

        Close(segments, heights, run, up);

        return segments.Count == 0 ? null : new PackTrail(mapId, segments, heights);
    }

    /// <summary>
    /// Keeps a run, if there is a line in it.
    ///
    /// A single point is not a route. Packs contain them — a break immediately after a point, or
    /// a trail trimmed to nothing — and a line needs two ends.
    /// </summary>
    private static void Close(
        List<IReadOnlyList<(double X, double Z)>> segments, List<IReadOnlyList<double>> heights,
        List<(double X, double Z)> run, List<double> up)
    {
        if (run.Count < 2) return;

        segments.Add(run);
        heights.Add(up);
    }
}
