namespace GuildWarsCompanion.Services;

/// <summary>
/// A waypoint worth porting to on the way to an objective, and how far is left to walk after it.
/// </summary>
/// <param name="Waypoint">The waypoint itself, so the screen can name it exactly as the map does.</param>
/// <param name="Walk">Continent units from that waypoint to the objective.</param>
public sealed record PortHop(CompassTarget Waypoint, double Walk)
{
    /// <summary>
    /// Whether porting is better advice than walking, from where the character is standing now.
    ///
    /// Two conditions, and both are needed. The walk has to be long enough that a loading screen
    /// is the lesser cost — <see cref="PortRoute.WorthPorting"/> — and the waypoint has to leave
    /// meaningfully less of it, or the answer is "port, then walk almost as far", which is worse
    /// than saying nothing.
    /// </summary>
    public bool BeatsWalking(double direct) =>
        direct >= PortRoute.WorthPorting && Walk <= direct * PortRoute.MustSave;
}

/// <summary>
/// A waypoint to port to when a trail leads to the objective: how far it is from there to the
/// trail, and how far along the trail after that.
/// </summary>
public sealed record TrailHop(CompassTarget Waypoint, double ToTrail, double Along)
{
    /// <summary>
    /// The way from the waypoint, counted as <see cref="TrailGuide.Find"/> counts it: the trail at
    /// face value, the straight line onto it at <see cref="TrailGuide.StraightLinePenalty"/> times,
    /// because the straight line is what cannot be trusted.
    /// </summary>
    public double Cost => Along + ToTrail * TrailGuide.StraightLinePenalty;

    /// <summary>
    /// Whether porting is better advice than walking from <paramref name="here"/>, by the same two
    /// rules as <see cref="PortHop.BeatsWalking"/>, on the same weighted measure as <see cref="Cost"/>.
    ///
    /// Weighted, because the case this is for is the straight line: "925 to the trail" from Camp
    /// Resolve towards Charnel Grounds, where that trail only starts after its author ported. At
    /// face value 925 is under the porting threshold; across ground nobody has walked, it is not a
    /// distance that can be taken at its word.
    /// </summary>
    public bool BeatsWalking(TrailLeg here)
    {
        var walk = here.Along + here.OffTrail * TrailGuide.StraightLinePenalty;

        return walk >= PortRoute.WorthPorting && Cost <= walk * PortRoute.MustSave;
    }
}

/// <summary>
/// Which waypoint to port to, for an objective somewhere across the map.
///
/// The compass answers "which way and how far", and for anything past a few hundred units that
/// is true and useless: nobody walks four thousand units across Queensdale, they open the map,
/// click the nearest waypoint and walk the last stretch. The dial's outer ring is eight thousand,
/// so the screen was at its least useful exactly where it had the most to say.
///
/// Pure arithmetic over coordinates that are already loaded — the map's waypoints arrive in the
/// same floor response as its hearts and vistas — so this costs no request and no new data.
///
/// <b>What this deliberately does not say.</b> The API publishes neither a waypoint's cost nor
/// whether it is contested, and both decide whether the advice is usable. So the claim is kept
/// strictly geometric: this waypoint is the closest one to the objective, and that much leaves
/// to walk. A player who finds it contested knows what to do about it; a screen that had implied
/// it was open would have lied.
/// </summary>
public static class PortRoute
{
    /// <summary>
    /// How far the walk has to be before porting is worth suggesting, in continent units.
    ///
    /// The dial's third ring. Inside it the whole neighbourhood is walkable and a loading screen
    /// costs more than the walk saves; outside it, walking is the thing nobody actually does.
    /// </summary>
    public const double WorthPorting = 2000;

    /// <summary>
    /// How much of the walk the waypoint has to take off: it must leave at most half.
    ///
    /// A ratio rather than a second distance, because it also disposes of the case that would
    /// otherwise need its own rule — a character already standing at the nearest waypoint. There
    /// the remaining walk is the whole walk, the ratio fails, and nothing is suggested.
    /// </summary>
    public const double MustSave = 0.5;

    /// <summary>
    /// The nearest waypoint to an objective, out of everything the map has given us.
    ///
    /// Nearest to the <i>objective</i>, not to the character: where the character is standing has
    /// no bearing on which waypoint is the right one to arrive at, and porting from anywhere
    /// costs the same loading screen.
    /// </summary>
    /// <returns>Null when there is nothing honest to say — see the remarks for each case.</returns>
    public static PortHop? To(CompassTarget objective, IEnumerable<CompassTarget> candidates)
    {
        // A waypoint is its own route. "Port to the waypoint you picked" is not advice.
        if (objective.Kind == TargetKind.Waypoint) return null;

        // A map-centre fallback is not a place. The nearest waypoint to the middle of Frostgorge
        // Sound is a precise answer to a question about an imprecise point, which reads as though
        // the arena position were known after all. See CompassTarget.Precise.
        if (!objective.Precise) return null;

        PortHop? best = null;

        foreach (var candidate in candidates)
        {
            if (candidate.Kind != TargetKind.Waypoint) continue;

            // Only waypoints on the objective's own map. A waypoint on the map the character is
            // standing in says nothing about a walk that starts two maps away, and the distance
            // between the two would be arithmetic across a continent that nobody walks.
            if (candidate.MapId != objective.MapId) continue;

            var walk = Bearing.Distance(candidate.X, candidate.Y, objective.X, objective.Y);

            if (best is null || walk < best.Walk) best = new PortHop(candidate, walk);
        }

        return best;
    }

    /// <summary>
    /// The line the compass shows, once <see cref="PortHop.BeatsWalking"/> has agreed there is
    /// one.
    ///
    /// Built once when the objective is chosen rather than per frame: the waypoint and the walk
    /// from it are fixed by the objective, and only the decision to show them moves as the
    /// character walks. The dial repaints sixty times a second and the readout is rebuilt with
    /// it, so a string interpolated in that loop is a string allocated sixty times a second.
    ///
    /// "then" rather than "left", because this screen already uses left and right for which way
    /// to turn.
    /// </summary>
    public static string Line(PortHop hop) => $"via {hop.Waypoint.Name} · then {hop.Walk:0}";

    /// <summary>
    /// The waypoint to port to when a trail leads to the objective: the one that makes the rest of
    /// the way cheapest, onto the trail and along it.
    ///
    /// <see cref="To"/> picks the waypoint nearest the objective, and with a trail that is the wrong
    /// question. The trail is followed because the straight line fails — a chamber behind rock — so
    /// "port here, then 180" sends the character back to the rock face. The waypoint that matters is
    /// the one nearest the trail's way in, and that is often not the one nearest the objective.
    /// </summary>
    /// <returns>Null for an objective that is itself a waypoint, or a map with no waypoints in hand.</returns>
    public static TrailHop? ToTrail(TrailGuide guide, CompassTarget objective, IEnumerable<CompassTarget> candidates)
    {
        if (objective.Kind == TargetKind.Waypoint) return null;

        TrailHop? best = null;

        foreach (var candidate in candidates)
        {
            if (candidate.Kind != TargetKind.Waypoint || candidate.MapId != guide.Route.MapId) continue;

            // The leg from the waypoint is exactly the question: how far to the trail, how far along.
            var leg = guide.Leg(candidate.X, candidate.Y);
            var hop = new TrailHop(candidate, leg.OffTrail, leg.Along);

            if (best is null || hop.Cost < best.Cost) best = hop;
        }

        return best;
    }

    /// <summary>The trail's version of <see cref="Line(PortHop)"/>, built once per objective.</summary>
    public static string Line(TrailHop hop) =>
        hop.ToTrail <= TrailGuide.OnTrail
            ? $"via {hop.Waypoint.Name} · then {hop.Along:0} along the trail"
            : $"via {hop.Waypoint.Name} · then {hop.ToTrail:0} to the trail, {hop.Along:0} along it";
}
