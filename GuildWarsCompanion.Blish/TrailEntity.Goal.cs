using System;

using GuildWarsCompanion.Bridge.Protocol;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GuildWarsCompanion.Blish;

/// <summary>
/// The objective itself, drawn two ways by distance.
///
/// <b>From afar, a beam of light.</b> The first draft stood two crossed quads of flat brass on the
/// objective, and flat colour right to its edges read as a solid post. Light is brightest in its
/// core and gone at its edges, so the beam is exactly that: a thin near-white core, brass glow
/// either side, clear at the rim, faded to nothing at the top — and always turned to face the
/// camera, where the crossed quads showed as an X or a box from half the angles round it.
///
/// <b>Up close, a ring and a marker.</b> Within <see cref="NearGoal"/> metres the beam gives way to
/// a ring of turning dashes on the ground — the way the game itself marks an event's area, and
/// which says where the spot is rather than only that there is one — and a chevron hanging over it,
/// pointing down, in the same shape as the ones along the track. Between <see cref="NearGoal"/>
/// and <see cref="FarGoal"/> one fades into the other. The beam never quite goes: a trace of it
/// stays, for the ridge in the way.
///
/// <b>Arrival</b> flashes the ring once and sends a ring of light out across the ground from it,
/// alongside the burst of sparks.
/// </summary>
public sealed partial class TrailEntity
{
    /// <summary>How tall the beam stands, so it shows over a ridge.</summary>
    private const float Beacon = 30f;

    /// <summary>
    /// How much the beam widens and grows with distance from the camera, per metre.
    ///
    /// A fixed size was measured invisible: the first frame drawn had the objective 340 metres off,
    /// where a beam a metre wide is three pixels. Growing it with distance keeps it about the same
    /// size on screen, which is the only size that matters for "it is over there".
    /// </summary>
    private const float Growth = 0.012f;

    /// <summary>Where the beam hands over to the ring and marker, in metres from the objective
    /// over the ground: all ring nearer than the first, all beam further than the second.</summary>
    private const float NearGoal = 40f;
    private const float FarGoal = 60f;

    /// <summary>How much of the beam is left up close, against its full strength.</summary>
    private const float BeamLeft = 0.12f;

    /// <summary>The ring's radius and the width of its dashes, in metres, how many dashes it has,
    /// and how fast it turns, in radians a second: slow enough to be a shimmer, not a wheel.</summary>
    private const float RingRadius = 3.5f;
    private const float RingWidth = 0.18f;
    private const int RingDashes = 16;
    private const float RingTurn = 0.25f;

    /// <summary>How far above the ground the marker hangs, in metres, and how far it bobs either
    /// way.</summary>
    private const float MarkerHeight = 3.2f;
    private const float MarkerBob = 0.25f;

    /// <summary>How long, in seconds, the ring of light arrival sends out takes to fade.</summary>
    private const float FlashLife = 0.9f;

    private const int RingSegments = 32;

    /// <summary>
    /// When, in seconds after arriving, the goal starts to go, and how long it takes: long enough
    /// for the flash and the burst to be seen first, short enough that the way is out of sight by
    /// the time the character has turned to whatever they came for.
    /// </summary>
    private const float LeaveAfter = 0.6f;
    private const float LeaveFor = 1.8f;

    /// <summary>Raised on the game thread once a trail has been reached and taken down.</summary>
    public event Action<GuidedTrail>? Reached;

    /// <summary>How far the goal has gone since arriving, eased: 0 before, 1 when it is gone.</summary>
    private float _leaving;

    /// <summary>How much of the trail is still shown: 1, down to 0 as it goes.</summary>
    private float _shown = 1;

    /// <summary>The beam's vertices across, against its half width; its rows up, against its
    /// height; and how bright each row is.</summary>
    private static readonly float[] BeamAcross = { -1f, -0.3f, 0f, 0.3f, 1f };
    private static readonly float[] BeamUp = { 0f, 0.08f, 1f };
    private static readonly float[] BeamRowBright = { 0.7f, 1f, 0f };

    private readonly VertexPositionColor[] _goal = new VertexPositionColor[
        4 * 2 * 6 +                 // the beam: four bands across, two rows up
        RingSegments * 3 +          // the ring's faint fill
        RingDashes * 3 * 6 +        // its dashes, three pieces of arc each
        RingSegments * 6 +          // the ring of light arrival sends out
        2 * 12];                    // the marker and its glow

    /// <summary>Half the beam's width and its height, <paramref name="away"/> metres from the
    /// camera.</summary>
    private static float BeamHalf(float away) => 0.6f + away * Growth;

    private static float BeamHeight(float away) => Beacon + away * Growth * 40;

    /// <summary>
    /// Works out how far the goal has gone since the character arrived, and drops the trail once it
    /// has. Returns false when there is nothing left to draw.
    ///
    /// The module tells the bridge through <see cref="Reached"/>, and the phone, hearing of it from
    /// the bridge, clears its objective.
    /// </summary>
    private bool Leave()
    {
        var since = _arrivedAt is { } arrived ? (float)(_seconds - arrived) : 0;
        var gone = MathHelper.Clamp((since - LeaveAfter) / LeaveFor, 0, 1);

        _leaving = gone * gone * (3 - 2 * gone);
        _shown = 1 - _leaving;

        if (gone < 1) return true;

        if (_trail is { } reached) Reached?.Invoke(reached);

        Show(null);
        _leaving = 0;
        _shown = 1;

        return false;
    }

    /// <summary>The goal this frame, into <see cref="_goal"/>; returns how many vertices.</summary>
    private int Goal(Vector3 foot, Vector3 camera, float toGoal)
    {
        var near = MathHelper.Clamp((FarGoal - toGoal) / (FarGoal - NearGoal), 0, 1);
        var t = (float)(_seconds % 3600);

        // Turned to face the camera about its own upright axis: across is level, and square to
        // the line from the camera.
        var toCamera = camera - foot;
        var across = Vector3.Cross(Vector3.UnitZ, toCamera);

        if (across.LengthSquared() < 1e-6f) across = Vector3.UnitX;

        across.Normalize();

        var flash = _arrivedAt is { } arrived ? (float)(_seconds - arrived) : float.MaxValue;

        var at = 0;

        at = Beam(at, foot, Vector3.Distance(foot, camera), across, MathHelper.Lerp(1, BeamLeft, near) * _shown, t);

        if (near > 0)
        {
            at = Ring(at, foot + new Vector3(0, 0, Lift), near * _shown, t, flash);
            at = Marker(at, foot, camera, across, near * _shown, t);
        }

        return at;
    }

    /// <summary>
    /// The beam: five vertices across — clear rim, brass glow, near-white core, glow, rim — in
    /// three rows up: the foot, a little above it where it is at its brightest, and the top, where
    /// it has faded to nothing. A slow unsteady shimmer keeps it from looking painted on the sky.
    /// </summary>
    private int Beam(int at, Vector3 foot, float away, Vector3 across, float strength, float t)
    {
        var half = BeamHalf(away) * 1.4f;
        var height = BeamHeight(away);
        var shimmer = 0.92f + 0.08f * (float)(Math.Sin(t * 2.1) * Math.Sin(t * 3.3 + 1));

        var bright = strength * shimmer;

        for (var row = 0; row < 2; row++)
        {
            for (var band = 0; band < 4; band++)
            {
                at = Corner(at, foot, across, half, height, band, row, bright);
                at = Corner(at, foot, across, half, height, band + 1, row, bright);
                at = Corner(at, foot, across, half, height, band + 1, row + 1, bright);

                at = Corner(at, foot, across, half, height, band, row, bright);
                at = Corner(at, foot, across, half, height, band + 1, row + 1, bright);
                at = Corner(at, foot, across, half, height, band, row + 1, bright);
            }
        }

        return at;
    }

    /// <summary>One vertex of the beam, at column <paramref name="column"/> across and
    /// <paramref name="row"/> up, coloured by where it is: core near-white, glow brass, rim
    /// clear.</summary>
    private int Corner(int at, Vector3 foot, Vector3 across, float half, float height, int column, int row, float strength)
    {
        var position = foot + across * (BeamAcross[column] * half) + new Vector3(0, 0, BeamUp[row] * height);
        var bright = BeamRowBright[row] * strength;

        var colour = column switch
        {
            2 => Spark * (0.9f * bright),
            1 or 3 => Brass * (0.4f * bright),
            _ => Color.Transparent,
        };

        _goal[at++] = new VertexPositionColor(position, colour);

        return at;
    }

    /// <summary>
    /// The ring on the ground: a faint fill, brighter at its rim, and round it dashes turning
    /// slowly. On arrival the dashes flash and a ring of light runs out across the ground.
    /// </summary>
    private int Ring(int at, Vector3 centre, float strength, float t, float flash)
    {
        var lit = flash < FlashLife ? (float)Math.Exp(-flash * 3) : 0;

        // Drawn in to a third of its size as it goes, turning faster and faster.
        var radius = RingRadius * (1 - 0.65f * _leaving);

        var middle = Brass * (0.04f * strength);
        var rim = Brass * (0.16f * strength);

        for (var i = 0; i < RingSegments; i++)
        {
            var a0 = i * MathHelper.TwoPi / RingSegments;
            var a1 = (i + 1) * MathHelper.TwoPi / RingSegments;

            _goal[at++] = new VertexPositionColor(centre, middle);
            _goal[at++] = new VertexPositionColor(centre + Round(a0, radius), rim);
            _goal[at++] = new VertexPositionColor(centre + Round(a1, radius), rim);
        }

        var dash = Glint * (Math.Min(1, 0.85f + lit) * strength);
        var slot = MathHelper.TwoPi / RingDashes;
        var turned = t * RingTurn + _leaving * _leaving * 6;

        for (var i = 0; i < RingDashes; i++)
        {
            for (var piece = 0; piece < 3; piece++)
            {
                // Each dash is three fifths of its slot, drawn as three short straight pieces so it
                // bends with the ring.
                var a0 = turned + i * slot + piece * slot * 0.2f;
                var a1 = a0 + slot * 0.2f;

                at = Band(at, centre, a0, a1, radius - RingWidth / 2, radius + RingWidth / 2, dash);
            }
        }

        if (flash < FlashLife)
        {
            var gone = flash / FlashLife;
            var wave = RingRadius * (1 + gone * 1.2f);
            var light = Spark * (1 - gone);

            for (var i = 0; i < RingSegments; i++)
            {
                var a0 = i * MathHelper.TwoPi / RingSegments;

                at = Band(at, centre, a0, a0 + MathHelper.TwoPi / RingSegments, wave - 0.12f, wave + 0.12f, light);
            }
        }

        return at;
    }

    /// <summary>A piece of flat ring from angle <paramref name="a0"/> to <paramref name="a1"/>,
    /// between two radii.</summary>
    private int Band(int at, Vector3 centre, float a0, float a1, float inner, float outer, Color colour)
    {
        var i0 = centre + Round(a0, inner);
        var o0 = centre + Round(a0, outer);
        var i1 = centre + Round(a1, inner);
        var o1 = centre + Round(a1, outer);

        _goal[at++] = new VertexPositionColor(i0, colour);
        _goal[at++] = new VertexPositionColor(o0, colour);
        _goal[at++] = new VertexPositionColor(o1, colour);

        _goal[at++] = new VertexPositionColor(i0, colour);
        _goal[at++] = new VertexPositionColor(o1, colour);
        _goal[at++] = new VertexPositionColor(i1, colour);

        return at;
    }

    private static Vector3 Round(float angle, float radius) =>
        new Vector3((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius, 0);

    /// <summary>
    /// The marker: a chevron pointing down at the spot, hanging over it and bobbing, turned to the
    /// camera like the beam. A larger, dimmer one behind it is its glow. Grown a little with
    /// distance, so it still reads at the far edge of the ring's range.
    /// </summary>
    private int Marker(int at, Vector3 foot, Vector3 camera, Vector3 across, float strength, float t)
    {
        // Rising away as it goes, as if its work were done.
        var centre = foot + new Vector3(0, 0, MarkerHeight + MarkerBob * (float)Math.Sin(t * 2) + _leaving * 2.5f);
        var grown = 1 + Vector3.Distance(centre, camera) * 0.02f;

        at = Hanging(at, centre, across, grown * 1.35f, Brass * (0.35f * strength), Brass * (0.35f * strength));
        at = Hanging(at, centre, across, grown, Glint * strength, Glint * (0.8f * strength));

        return at;
    }

    /// <summary>One chevron pointing down, standing upright in the plane of <paramref name="across"/>:
    /// the track's chevron, turned on end.</summary>
    private int Hanging(int at, Vector3 centre, Vector3 across, float scale, Color tip, Color wing)
    {
        var down = -Vector3.UnitZ;
        var length = 0.55f * scale;
        var side = across * (0.6f * scale);
        var back = -down * (0.25f * scale);

        var apex = centre + down * (length / 2);
        var left = centre - down * (length / 2) - side;
        var right = centre - down * (length / 2) + side;

        _goal[at++] = new VertexPositionColor(apex, tip);
        _goal[at++] = new VertexPositionColor(left, wing);
        _goal[at++] = new VertexPositionColor(left + back, wing);

        _goal[at++] = new VertexPositionColor(apex, tip);
        _goal[at++] = new VertexPositionColor(left + back, wing);
        _goal[at++] = new VertexPositionColor(apex + back, tip);

        _goal[at++] = new VertexPositionColor(apex, tip);
        _goal[at++] = new VertexPositionColor(apex + back, tip);
        _goal[at++] = new VertexPositionColor(right + back, wing);

        _goal[at++] = new VertexPositionColor(apex, tip);
        _goal[at++] = new VertexPositionColor(right + back, wing);
        _goal[at++] = new VertexPositionColor(right, wing);

        return at;
    }
}
