using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Hands the trail to the objective to the PC, for the Blish HUD module to draw in the game — or,
/// where no trail leads, the objective alone, which the game marks with the same beam and ring.
///
/// Once per change of objective or trail, not once per screen. The compass and the map both work
/// out a guide, from slightly different positions at slightly different moments, and both would
/// otherwise send — the in-game line redrawn twice, from two starting points, for one choice. The
/// first to say something new about the same objective and trail is the one that is sent.
///
/// And the way back: when the game reports the objective reached — see
/// <see cref="GameReport.Arrived"/> — <see cref="HasArrived"/> says so, once, for the screen that
/// hears it first to clear the objective.
/// </summary>
public sealed class TrailShare(BridgeClient bridge)
{
    /// <summary>What was last sent: the objective, its map and the trail's name. Null before
    /// anything, and "none" once a clear has gone out.</summary>
    private string? _said;

    /// <summary>How much of the approach is sent with nobody on the map, in continent units.</summary>
    private const double Approach = 600;

    /// <summary>
    /// Says what should be drawn now: the guide's stretch from where the character is, or nothing.
    /// </summary>
    /// <param name="from">Where the character is, or null when nobody is on this map.</param>
    public void Share(TrailGuide? guide, CompassTarget? target, MapInfo? map, (double X, double Y)? from)
    {
        if (target is null)
        {
            _target = null;
            Say(null, "none");
            return;
        }

        var who = $"{target.MapId}|{target.X:0}|{target.Y:0}";

        if (guide is null && Alone(target, map) is { } alone)
        {
            // A trail already out for this objective stays: one screen without a guide — the map
            // before its trails have loaded — must not swap the line for the beam and back.
            if (_target == who && _said is { } said && said.StartsWith(who + "|", StringComparison.Ordinal) &&
                !said.EndsWith("|alone", StringComparison.Ordinal))
                return;

            // No trail to it, but it is a place: the game shows the objective by itself.
            _target = who;
            Say(alone, $"{who}|alone");
            return;
        }

        if (guide is null || map is not { MapArea: { } area, ContinentArea: { } continent })
        {
            // No trail from here. Only a different objective is a reason to take the line down:
            // the same one looked at from another map on the phone, or asked about before its
            // trails were loaded, leaves what the game is drawing alone.
            if (_target != who)
            {
                _target = who;
                Say(null, "none");
            }

            return;
        }

        _target = who;

        var start = from is { } here ? guide.Leg(here.X, here.Y).At : guide.Back(Approach);
        var trail = guide.ForGame(start, MapSpace.ToMetres(target.X, target.Y, area, continent));

        // A trail with no heights cannot be drawn in the world, and saying nothing would leave
        // the last objective's line standing. So a trail that cannot be sent clears instead.
        Say(trail, trail is null ? "none" : $"{who}|{guide.Route.Name}");
    }

    /// <summary>
    /// The objective by itself, for the game to mark where no trail leads: null when it cannot be
    /// placed.
    ///
    /// Only a precise one — a world boss's point is the middle of its map, and a beam there would
    /// send the player to nothing. Only on the map being looked at, since that is the map whose
    /// rectangles convert it; looked at from elsewhere, it is left as the game has it. Its height is
    /// the marker pack's where there is one, and otherwise the module's to choose — see
    /// <see cref="GuidedTrail.HeightKnown"/>.
    /// </summary>
    private static GuidedTrail? Alone(CompassTarget target, MapInfo? map)
    {
        if (!target.Precise ||
            map is not { MapArea: { } area, ContinentArea: { } continent } ||
            map.Id != target.MapId)
            return null;

        var (x, z) = MapSpace.ToMetres(target.X, target.Y, area, continent);

        return new GuidedTrail(target.MapId, (x, target.Up ?? 0, z), [], heightKnown: target.Up is not null);
    }

    /// <summary>The objective the last word was about, so a different one can clear the line.</summary>
    private string? _target;

    /// <summary>The trail last sent, or null when the last word was a clear.</summary>
    private GuidedTrail? _sent;

    /// <summary>
    /// How near, in metres, the reported objective has to be to the one sent to be the same one.
    /// The numbers go out with two decimals and come back rebuilt from them, so they differ by
    /// rounding at most.
    /// </summary>
    private const double Same = 0.1;

    /// <summary>
    /// Whether <paramref name="report"/> says the character has reached the objective the trail
    /// was sent for. True once: after that the trail counts as done, so the second screen to hear
    /// the same report, or the next report saying the same thing, is told no.
    /// </summary>
    public bool HasArrived(GameReport report)
    {
        if (report.Arrived is not { } arrived || _sent is not { } sent) return false;

        if (arrived.MapId != sent.MapId ||
            Math.Abs(arrived.X - sent.Target.X) > Same ||
            Math.Abs(arrived.Z - sent.Target.Z) > Same)
            return false;

        _sent = null;

        return true;
    }

    private void Say(GuidedTrail? trail, string key)
    {
        if (key == _said) return;

        _sent = trail;

        // The very first word is never a clear: there is nothing drawn yet to take away, and a
        // phone opening the compass with no objective should not knock on the PC to say so.
        if (_said is null && trail is null)
        {
            _said = key;
            return;
        }

        _said = key;

        _ = SendAsync(trail);
    }

    private async Task SendAsync(GuidedTrail? trail)
    {
        try
        {
            await bridge.GuideAsync(trail);
        }
        catch (Exception)
        {
            // Nothing on the phone depends on this; the journal already has what the bridge said.
        }
    }
}
