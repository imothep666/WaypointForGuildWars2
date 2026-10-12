using GuildWarsCompanion.Bridge.Protocol;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Where the trail says to go next, from one position.
/// </summary>
/// <param name="AimX">What the needle points at: a point a little way along the trail, or the
/// trail itself when the character has wandered off it, or the target once it is close.</param>
/// <param name="Along">How far is left to walk along the trail to the target, in continent units.</param>
/// <param name="OffTrail">How far the character is from the trail right now.</param>
/// <param name="Climb">Metres up (positive) or down to the target, when the trail knows its
/// heights; null when it does not.</param>
/// <param name="At">The trail point nearest the character, for drawing what is left.</param>
public readonly record struct TrailLeg(
    double AimX, double AimY, double Along, double OffTrail, double? Climb, int At)
{
    public bool IsOnTrail => OffTrail <= TrailGuide.OnTrail;
}

/// <summary>
/// The way to a target that somebody has already walked.
///
/// <b>Why this and not pathfinding.</b> A route planner needs to know where the ground is — walls,
/// cliffs, the mouth of a cave — and nothing ArenaNet publishes says. The API gives a point of
/// interest as one coordinate and MumbleLink gives the character's position; the straight line
/// between them is exactly the wrong answer for a chamber whose entrance is on the far side of a
/// ridge. A marker pack's trails were recorded by players walking there, so where one passes the
/// target it is a path that is known to work, and the only honest source of one there is.
///
/// <b>Measured before it was built.</b> The Silverwastes, which is where this was asked for: Lady
/// Elyssa's Guides passes within 25 continent units of all 18 of its points of interest, and on
/// the last stretch to Blue Oasis walks 301 units to cover a straight line of 186. Teh's Trails,
/// the pack used until then, has no trail on that map at all — so the answer depends entirely on
/// which packs are installed, and a guide that finds nothing says nothing.
///
/// All distances are continent units, the space both the compass and the map already work in.
/// </summary>
public sealed class TrailGuide
{
    /// <summary>
    /// How near a trail has to pass a target to count as leading there.
    ///
    /// Forty continent units, about 25 metres on most maps. Measured on the Silverwastes: every
    /// point of interest had a trail within 28, and the next nearest trails were hundreds away —
    /// a trail within forty was put there on purpose, and one further off only happens to be near.
    /// </summary>
    public const double Reach = 40;

    /// <summary>How far the character can stray from the trail and still be on it. Past this the
    /// needle points back to the trail rather than along it.</summary>
    public const double OnTrail = 60;

    /// <summary>
    /// How far along the trail the needle looks.
    ///
    /// Far enough that a trail's small zigzags do not swing the needle from side to side, near
    /// enough that a turn into a cave mouth is pointed at before it has been walked past.
    /// </summary>
    public const double LookAhead = 40;

    /// <summary>
    /// How much worse a unit of straight line is than a unit of trail, when choosing between
    /// trails.
    ///
    /// Getting onto a trail means crossing open ground in a straight line, and the straight line is
    /// exactly the measure this class exists because it cannot be trusted. Counted at face value, a
    /// trail starting 390 away across a wall beat one starting underfoot and running 600 round it —
    /// found by the test for it, not in the field. Four to one keeps the trail underfoot unless the
    /// other one is very much shorter.
    /// </summary>
    public const double StraightLinePenalty = 4;

    private readonly double[] _along;

    private readonly double _targetX;
    private readonly double _targetY;

    private TrailGuide(RouteLine route, int target, double offset, double targetX, double targetY)
    {
        Route = route;
        TargetIndex = target;
        Offset = offset;

        _targetX = targetX;
        _targetY = targetY;

        _along = new double[route.Points.Count];

        for (var i = 1; i < _along.Length; i++)
            _along[i] = _along[i - 1] + Distance(route.Points[i - 1], route.Points[i]);
    }

    public RouteLine Route { get; }

    /// <summary>The trail point nearest the target.</summary>
    public int TargetIndex { get; }

    /// <summary>How far the target is from the trail.</summary>
    public double Offset { get; }

    /// <summary>
    /// The trail that leads to the target, or null when no installed trail goes near it.
    ///
    /// With a position, the trail that makes the whole way shortest: getting onto the trail plus
    /// walking it to the target. Two packs, or one pack's walking and mounted runs, often both
    /// pass a place; the one that starts nearer is the one worth following. Without a position —
    /// the map screen with nobody playing — simply the trail that passes closest.
    /// </summary>
    public static TrailGuide? Find(
        IReadOnlyList<RouteLine> routes, double targetX, double targetY, (double X, double Y)? from = null)
    {
        TrailGuide? best = null;
        var bestCost = double.MaxValue;

        foreach (var route in routes)
        {
            if (route.Points.Count < 2) continue;

            var (target, offset) = Nearest(route.Points, targetX, targetY);

            if (offset > Reach) continue;

            var guide = new TrailGuide(route, target, offset, targetX, targetY);

            var cost = offset;

            if (from is { } here)
            {
                var leg = guide.Leg(here.X, here.Y);

                cost = leg.Along + leg.OffTrail * StraightLinePenalty;
            }

            if (cost < bestCost)
            {
                best = guide;
                bestCost = cost;
            }
        }

        return best;
    }

    /// <summary>
    /// The next step from here. Cheap enough for every frame of the dial: one pass over a trail
    /// that thinning has already cut to a few hundred points.
    /// </summary>
    public TrailLeg Leg(double x, double y)
    {
        var points = Route.Points;
        var (at, off) = Nearest(points, x, y);

        var along = Math.Abs(_along[TargetIndex] - _along[at]);

        var climb = Route.Heights is { } heights &&
                    double.IsFinite(heights[at]) && double.IsFinite(heights[TargetIndex])
            ? heights[TargetIndex] - heights[at]
            : (double?)null;

        // Off the trail: back to it first. Pointing along a trail sixty units away would send the
        // character parallel to it, which is the one direction guaranteed not to join it.
        if (off > OnTrail) return new TrailLeg(points[at].X, points[at].Y, along, off, climb, at);

        // Nearly there: the target itself, not the trail point beside it.
        if (along <= LookAhead) return new TrailLeg(_targetX, _targetY, along, off, climb, at);

        var (aimX, aimY) = Ahead(at, LookAhead);

        return new TrailLeg(aimX, aimY, along, off, climb, at);
    }

    /// <summary>
    /// The trail from one point to the target, in the order it is walked. For drawing what is
    /// left: the map draws this over the rest of the route so the way reads at a glance.
    /// </summary>
    public IEnumerable<(double X, double Y)> Stretch(int from)
    {
        from = Math.Clamp(from, 0, Route.Points.Count - 1);

        var step = from <= TargetIndex ? 1 : -1;

        for (var i = from; ; i += step)
        {
            yield return Route.Points[i];

            if (i == TargetIndex) yield break;
        }
    }

    /// <summary>
    /// The same stretch into an array the caller keeps, for a loop that runs every frame and must
    /// not allocate an iterator each time. Stops at the end of the array; the far end of a long
    /// stretch is at the dial's rim anyway.
    /// </summary>
    /// <returns>How many entries of <paramref name="into"/> were filled.</returns>
    public int Copy(int from, (double X, double Y)[] into)
    {
        from = Math.Clamp(from, 0, Route.Points.Count - 1);

        var step = from <= TargetIndex ? 1 : -1;
        var count = 0;

        for (var i = from; count < into.Length; i += step)
        {
            into[count++] = Route.Points[i];

            if (i == TargetIndex) break;
        }

        return count;
    }

    /// <summary>
    /// The stretch from <paramref name="from"/> to the target, in the game's metres, for the Blish HUD
    /// module to draw. Null when the trail cannot be drawn in three dimensions: stored before heights
    /// were kept, or missing a height anywhere on the stretch — a ribbon that dives to zero at one
    /// point runs through the floor of the world.
    /// </summary>
    /// <param name="target">The objective in metres; see <see cref="MapSpace.ToMetres"/>. Its height is
    /// the trail's at the nearest point, since the API gives a point of interest none.</param>
    public GuidedTrail? ForGame(int from, (double X, double Z) target)
    {
        if (Route.Metres is not { } metres || Route.Heights is not { } heights ||
            metres.Count != Route.Points.Count || heights.Count != Route.Points.Count)
            return null;

        from = Math.Clamp(from, 0, Route.Points.Count - 1);

        var step = from <= TargetIndex ? 1 : -1;
        var points = new List<(double X, double Up, double Z)>();

        for (var i = from; points.Count < GuideWire.MostPoints; i += step)
        {
            if (!double.IsFinite(heights[i])) return null;

            points.Add((metres[i].X, heights[i], metres[i].Z));

            if (i == TargetIndex) break;
        }

        if (points.Count < 2) return null;

        return new GuidedTrail(Route.MapId, (target.X, heights[TargetIndex], target.Z), points);
    }

    /// <summary>
    /// Where the trail was this far before it reached the target, for a map with nobody on it.
    ///
    /// With no character there is no "from here", and the whole trail is often a tour of the entire
    /// map; the part worth showing is the approach. Before means earlier in the file, which is the
    /// order the author walked it in.
    /// </summary>
    public int Back(double distance)
    {
        var i = TargetIndex;

        while (i > 0 && _along[TargetIndex] - _along[i] < distance) i--;

        return i;
    }

    /// <summary>A point <paramref name="distance"/> further along the trail, towards the target.</summary>
    private (double X, double Y) Ahead(int from, double distance)
    {
        var points = Route.Points;
        var step = from <= TargetIndex ? 1 : -1;
        var left = distance;

        for (var i = from; i != TargetIndex; i += step)
        {
            var a = points[i];
            var b = points[i + step];
            var length = Distance(a, b);

            if (length >= left && length > 0)
            {
                var t = left / length;

                return (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            }

            left -= length;
        }

        return points[TargetIndex];
    }

    private static (int Index, double Distance) Nearest(
        IReadOnlyList<(double X, double Y)> points, double x, double y)
    {
        var best = 0;
        var bestSquared = double.MaxValue;

        for (var i = 0; i < points.Count; i++)
        {
            var dx = points[i].X - x;
            var dy = points[i].Y - y;
            var squared = dx * dx + dy * dy;

            if (squared < bestSquared)
            {
                best = i;
                bestSquared = squared;
            }
        }

        return (best, Math.Sqrt(bestSquared));
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>
    /// "14 m lower", "8 m higher", or nothing when it is level or unknown.
    ///
    /// Under three metres is said as nothing at all: a trail's heights wobble by that much across
    /// ordinary ground, and "1 m higher" on a flat walk is noise wearing a number.
    /// </summary>
    public static string Height(double? climb) => climb switch
    {
        null => string.Empty,
        < -3 => L.F("{0:0} m lower", -climb.Value),
        > 3 => L.F("{0:0} m higher", climb.Value),
        _ => string.Empty
    };
}
