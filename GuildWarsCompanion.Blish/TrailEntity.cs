using System;
using System.Collections.Generic;

using Blish_HUD;
using Blish_HUD.Entities;

using GuildWarsCompanion.Bridge.Protocol;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GuildWarsCompanion.Blish;

/// <summary>
/// The trail to the objective, drawn in the world: a track along the ground the pack's author
/// walked, and a goal marked on the objective.
///
/// <b>Only the next stretch.</b> The track starts just ahead of the character and runs
/// <see cref="TrailAhead.Metres"/> on along the trail, cut again every frame as they walk, and
/// fades out at its far end when there is more to come. The whole trail at once was a line across
/// the map over every hill in between; the goal already says where it ends.
///
/// <b>A track and what runs along it.</b> The track is two bright edges round a faint middle, a
/// path marked out rather than painted on. On it, chevrons point the way and flow along it towards
/// the objective. They are fixed to the trail, not to the character, so walking does not drag them
/// along: standing still they stream past, running they roll underfoot like road markings. Every
/// few seconds a pulse of light runs out from the character along the whole stretch, which is what
/// catches the eye when the trail has been out of mind.
///
/// <b>Clear underfoot.</b> Nothing is drawn in the first <see cref="Clear"/> metres. The overlay
/// cannot be hidden behind the character any more than behind a wall, and a track starting at the
/// feet was drawn across the character's legs.
///
/// <b>Coordinates are the game's own.</b> The trail arrives in MumbleLink metres — x, up, z — and
/// Blish HUD's world is the same metres with the last two swapped, exactly as its own
/// <c>ToXnaVector3</c> does for the character. Nothing is scaled or offset.
///
/// <b>What it cannot do: be hidden behind a wall.</b> An overlay has no access to the game's depth
/// buffer, so the track is drawn over terrain rather than behind it — the same limit Pathing
/// lives with. For the case that prompted all this, a chamber behind rock, that is arguably the
/// point: the way is visible where the eye cannot follow it.
///
/// <b>The goal changes with distance</b> — a soft beam of light from afar, a ring on the ground
/// and a marker over it up close; see <c>TrailEntity.Goal.cs</c>.
///
/// <b>Gone once it has been reached.</b> On arrival the ring flashes and the sparks go up; then
/// everything fades, the ring drawing in and the marker rising away, and the trail is dropped —
/// see <see cref="Leave"/>.
///
/// <b>Sparks where they are worth it.</b> A few rise up the beam, a few are thrown off the
/// pulse's front, and a burst goes up on arrival — see <c>TrailEntity.Sparks.cs</c>. Never along the
/// whole track: an overlay draws over everything, and a track full of glitter would be drawn over
/// the fight.
///
/// Everything is rebuilt every frame into buffers kept for the purpose: nothing is allocated.
/// </summary>
public sealed partial class TrailEntity : IEntity
{
    private static readonly Color Brass = new Color(201, 162, 39);

    /// <summary>The bright parts — edges, chevrons, the pulse: lighter than brass, so they read as
    /// lit rather than painted.</summary>
    private static readonly Color Glint = new Color(255, 221, 120);

    /// <summary>Half the track's width, in metres: a path, not a road.</summary>
    private const float HalfWidth = 0.35f;

    /// <summary>How wide each of the track's edges is, in metres.</summary>
    private const float Edge = 0.06f;

    /// <summary>
    /// How bright the edges and the middle are, against full colour.
    ///
    /// The middle was a single band at thirty-five per cent brass before, and rendered over grass it
    /// came out as mud: a colour at a third of its strength over green is brown. Two bright lines
    /// round almost nothing keep the colour where it is strong and let the ground show through.
    /// </summary>
    private const float EdgeBright = 0.6f;
    private const float MiddleBright = 0.12f;

    /// <summary>How far above the recorded points the track floats. A trail is recorded at the
    /// character's feet, and a track exactly there flickers in and out of every slope.</summary>
    private const float Lift = 0.25f;

    /// <summary>How far ahead of the character nothing is drawn, in metres along the trail.</summary>
    private const float Clear = 1.5f;

    /// <summary>Over how many metres the track and chevrons fade in at the near end, and the
    /// chevrons out again at the objective, so nothing starts or stops with a hard edge.</summary>
    private const float Soft = 2f;

    /// <summary>How far before its cut end the track starts to fade, in metres, so it thins out
    /// rather than stopping as if it had arrived.</summary>
    private const float Fade = 20f;

    /// <summary>
    /// How far apart the track is sampled, in metres.
    ///
    /// A trail's own points are fifteen metres apart; the fades and the pulse change far faster
    /// than that along it, and colours are only ever blended between vertices.
    /// </summary>
    private const float Step = 1f;

    /// <summary>Metres between one chevron and the next along the trail.</summary>
    private const float Spacing = 2.5f;

    /// <summary>How fast the chevrons flow towards the objective, in metres a second: about half
    /// a running character, so they lead without looking like they are fleeing.</summary>
    private const float Flow = 4f;

    /// <summary>
    /// Half a chevron's width, its length from tip to wings, and the thickness of its arms, in
    /// metres.
    ///
    /// Arms 0.22 thick were rendered as hairlines from ten metres on; 0.32 still reads as a chevron
    /// close to and as a mark further off.
    /// </summary>
    private const float ChevronHalf = 0.45f;
    private const float ChevronLength = 0.5f;
    private const float ChevronStroke = 0.32f;

    /// <summary>How often a pulse runs out along the track, in seconds, and how fast it goes, in
    /// metres a second: out to the end of the stretch in two and a half, then a rest.</summary>
    private const float PulseEvery = 3.75f;
    private const float PulseSpeed = 30f;

    /// <summary>Half the length of the pulse's bright band, in metres.</summary>
    private const float PulseHalf = 5f;

    /// <summary>
    /// How much the pulse adds to the edges and the middle as it passes.
    ///
    /// Mostly to the middle: at 0.45 it was lost thirty metres out, where the whole track is a few
    /// pixels wide and only filling it in shows.
    /// </summary>
    private const float PulseEdge = 0.4f;
    private const float PulseMiddle = 0.75f;

    private static readonly int MostSamples = (int)(TrailAhead.Metres / Step) + 2;
    private static readonly int MostChevrons = (int)(TrailAhead.Metres / Spacing) + 2;

    private GuidedTrail? _trail;
    private readonly List<(double X, double Up, double Z)> _ahead = new List<(double X, double Up, double Z)>(GuideWire.MostPoints + 2);
    private readonly float[] _along = new float[GuideWire.MostPoints + 2];
    private readonly Vector3[] _samples = new Vector3[MostSamples];
    private readonly float[] _sampleAlong = new float[MostSamples];
    private readonly VertexPositionColor[] _track = new VertexPositionColor[MostSamples * 18];
    private readonly VertexPositionColor[] _chevrons = new VertexPositionColor[MostChevrons * 12];
    private BasicEffect? _effect;
    private double _seconds;

    public float DrawOrder => 0;

    /// <summary>What is being drawn, for the card to describe; null when nothing is.</summary>
    public GuidedTrail? Current => _trail;

    /// <summary>Whether the character has reached it and it is on its way out.</summary>
    public bool HasArrived => _arrivedAt is not null;

    /// <summary>Replaces what is drawn. Called on the game thread; null takes the line away.</summary>
    public void Show(GuidedTrail? trail)
    {
        _trail = trail;
        _arrivedAt = null;
    }

    public void Update(GameTime gameTime)
    {
        _seconds = gameTime.TotalGameTime.TotalSeconds;
    }

    public void Render(GraphicsDevice graphicsDevice, IWorld world, ICamera camera)
    {
        if (_trail is not { } trail) return;

        // Only on its own map. MumbleLink's metres restart on every map, so a trail for somewhere
        // else would be drawn through wherever the same numbers happen to fall here.
        if (GameService.Gw2Mumble.CurrentMap.Id != trail.MapId) return;

        var effect = _effect ??= new BasicEffect(graphicsDevice) { VertexColorEnabled = true };

        // Blish HUD's character is in its own world, z up; the trail is in MumbleLink's.
        var character = GameService.Gw2Mumble.PlayerCharacter.Position;
        var more = TrailAhead.Cut(trail.Points, (character.X, character.Z, character.Y), Clear, TrailAhead.Metres, _ahead, out var start);

        var total = Measure(_ahead);

        // Where the pulse's bright band is, in metres along the stretch; past the end while it rests.
        var pulse = (float)(_seconds % PulseEvery) * PulseSpeed;

        // How far the character is from the objective over the ground: what hands the goal over
        // from the beam, seen from afar, to the ring and marker, seen up close.
        var foot = World(trail.Target);

        // A height nobody knows is the character's own, followed as they climb: from afar the beam
        // is tall enough for it not to matter, and up close they are usually standing level with it.
        if (!trail.HeightKnown) foot.Z = character.Z;

        var toGoal = Vector2.Distance(new Vector2(character.X, character.Y), new Vector2(foot.X, foot.Y));

        if (_arrivedAt is null && toGoal < Arrive) _arrivedAt = _seconds;

        if (!Leave()) return;

        var track = Track(Sample(total), total, more, pulse);
        var chevrons = Chevrons(total, more, start, pulse);

        // The camera's own right and up in the world, from the view matrix's columns: what turns a
        // spark to face it.
        var view = GameService.Gw2Mumble.PlayerCamera.View;
        var right = new Vector3(view.M11, view.M21, view.M31);
        var up = new Vector3(view.M12, view.M22, view.M32);

        var goal = Goal(foot, camera.Position, toGoal);
        var sparks = Sparks(foot, camera.Position, total, more, pulse, right, up);

        effect.World = Matrix.Identity;
        effect.View = GameService.Gw2Mumble.PlayerCamera.View;
        effect.Projection = GameService.Gw2Mumble.PlayerCamera.Projection;

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();

            if (track >= 3)
                graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, _track, 0, track / 3);

            if (chevrons >= 3)
                graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, _chevrons, 0, chevrons / 3);

            if (goal >= 3)
                graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, _goal, 0, goal / 3);

            if (sparks >= 3)
                graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, _sparks, 0, sparks / 3);
        }
    }

    /// <summary>How far along the cut each of its points is, into <see cref="_along"/>; returns
    /// the cut's whole length.</summary>
    private float Measure(IReadOnlyList<(double X, double Up, double Z)> points)
    {
        if (points.Count == 0) return 0;

        _along[0] = 0;

        for (var i = 1; i < points.Count; i++)
            _along[i] = _along[i - 1] + Vector3.Distance(World(points[i - 1]), World(points[i]));

        return _along[points.Count - 1];
    }

    /// <summary>
    /// Where on the cut <paramref name="d"/> metres along falls, and which way the trail runs there.
    /// <paramref name="segment"/> carries on from the last call, so walking forward along the cut
    /// is one pass over it, not one per point asked for.
    /// </summary>
    private bool At(float d, ref int segment, out Vector3 position, out Vector3 direction)
    {
        while (segment < _ahead.Count - 2 && _along[segment + 1] < d) segment++;

        var a = World(_ahead[segment]);
        var b = World(_ahead[segment + 1]);
        var length = _along[segment + 1] - _along[segment];

        position = a;
        direction = Vector3.Zero;

        if (length < 1e-3f) return false;

        direction = (b - a) / length;
        position = Vector3.Lerp(a, b, MathHelper.Clamp((d - _along[segment]) / length, 0, 1));

        return true;
    }

    /// <summary>The cut every <see cref="Step"/> metres, into <see cref="_samples"/>, both ends
    /// included; returns how many.</summary>
    private int Sample(float total)
    {
        if (_ahead.Count < 2) return 0;

        var segment = 0;
        var count = 0;

        for (var d = 0f; count < _samples.Length; d += Step)
        {
            if (d > total) d = total;

            if (At(d, ref segment, out var position, out _))
            {
                _samples[count] = position;
                _sampleAlong[count] = d;
                count++;
            }

            if (d >= total) break;
        }

        return count;
    }

    /// <summary>How bright the pulse makes a place <paramref name="d"/> metres along, from 0 to 1:
    /// a smooth hump <see cref="PulseHalf"/> either side of the pulse's centre.</summary>
    private static float Pulse(float d, float pulse)
    {
        var t = 1 - Math.Abs(d - pulse) / PulseHalf;

        return t <= 0 ? 0 : t * t * (3 - 2 * t);
    }

    /// <summary>
    /// The track along the samples, into <see cref="_track"/>: three bands side by side — an edge,
    /// the middle, an edge — each its own vertices, so the edges are crisp lines rather than a
    /// blur into the middle. Returns how many vertices.
    /// </summary>
    private int Track(int count, float total, bool more, float pulse)
    {
        var at = 0;

        for (var i = 0; i + 1 < count; i++)
        {
            var nearAcross = Side(i, count);
            var farAcross = Side(i + 1, count);

            var near = _samples[i] + new Vector3(0, 0, Lift);
            var far = _samples[i + 1] + new Vector3(0, 0, Lift);

            var nearFade = Fading(_sampleAlong[i], total, more, Fade);
            var farFade = Fading(_sampleAlong[i + 1], total, more, Fade);

            var nearPulse = Pulse(_sampleAlong[i], pulse);
            var farPulse = Pulse(_sampleAlong[i + 1], pulse);

            var nearEdge = Glint * (nearFade * Math.Min(1, EdgeBright + PulseEdge * nearPulse));
            var farEdge = Glint * (farFade * Math.Min(1, EdgeBright + PulseEdge * farPulse));
            var nearMiddle = Glint * (nearFade * (MiddleBright + PulseMiddle * nearPulse));
            var farMiddle = Glint * (farFade * (MiddleBright + PulseMiddle * farPulse));

            var nearOuter = nearAcross * HalfWidth;
            var nearInner = nearAcross * (HalfWidth - Edge);
            var farOuter = farAcross * HalfWidth;
            var farInner = farAcross * (HalfWidth - Edge);

            at = Quad(at, near - nearOuter, near - nearInner, far - farOuter, far - farInner, nearEdge, farEdge);
            at = Quad(at, near - nearInner, near + nearInner, far - farInner, far + farInner, nearMiddle, farMiddle);
            at = Quad(at, near + nearInner, near + nearOuter, far + farInner, far + farOuter, nearEdge, farEdge);
        }

        return at;
    }

    /// <summary>Across the ground plane at sample <paramref name="i"/>, a unit long: Blish HUD's
    /// world has z up, so the width lies in x and y, square to the way the trail runs there.</summary>
    private Vector3 Side(int i, int count)
    {
        var ahead = _samples[Math.Min(i + 1, count - 1)];
        var behind = _samples[Math.Max(i - 1, 0)];

        var way = new Vector2(ahead.X - behind.X, ahead.Y - behind.Y);

        if (way.LengthSquared() < 1e-6f) way = Vector2.UnitX;

        way.Normalize();

        return new Vector3(-way.Y, way.X, 0);
    }

    /// <summary>One band's piece between two samples: a quad from its near left and right to
    /// its far left and right.</summary>
    private int Quad(int at, Vector3 nearLeft, Vector3 nearRight, Vector3 farLeft, Vector3 farRight, Color near, Color far)
    {
        _track[at++] = new VertexPositionColor(nearLeft, near);
        _track[at++] = new VertexPositionColor(nearRight, near);
        _track[at++] = new VertexPositionColor(farRight, far);

        _track[at++] = new VertexPositionColor(nearLeft, near);
        _track[at++] = new VertexPositionColor(farRight, far);
        _track[at++] = new VertexPositionColor(farLeft, far);

        return at;
    }

    /// <summary>How much of something <paramref name="d"/> metres along is shown: faded in over
    /// the first <see cref="Soft"/> metres, and out over the last <paramref name="tail"/> when
    /// <paramref name="more"/> says the cut stops short.</summary>
    private float Fading(float d, float total, bool more, float tail) =>
        MathHelper.Clamp(d / Soft, 0, 1) * (more ? MathHelper.Clamp((total - d) / tail, 0, 1) : 1) * _shown;

    /// <summary>
    /// The chevrons along the cut, into <see cref="_chevrons"/>: one every <see cref="Spacing"/>
    /// metres of the whole trail, shifted by the time so they flow. Returns how many vertices.
    ///
    /// <paramref name="start"/>, how far along the whole trail the cut begins, is what pins them to
    /// the ground: the first one sits where the trail's own spacing falls next, not at the cut's
    /// start, which moves with every step.
    /// </summary>
    private int Chevrons(float total, bool more, double start, float pulse)
    {
        if (_ahead.Count < 2) return 0;

        // Double until here: seconds times speed grows without end, and a float of it would start
        // to stutter within the hour.
        var first = (float)(((_seconds * Flow - start) % Spacing + Spacing) % Spacing);

        var segment = 0;
        var at = 0;

        for (var d = first; d <= total && at + 12 <= _chevrons.Length; d += Spacing)
        {
            if (!At(d, ref segment, out var position, out var dir)) continue;

            var flat = new Vector2(dir.X, dir.Y);

            if (flat.LengthSquared() < 1e-6f) continue;

            flat.Normalize();

            var across = new Vector3(-flat.Y, flat.X, 0) * ChevronHalf;
            var centre = position + new Vector3(0, 0, Lift + 0.02f);

            // Out at the objective too, not only where the cut stops short: a chevron reaching the
            // end of the trail should fade into the goal, not blink out.
            var fade = MathHelper.Clamp(d / Soft, 0, 1) *
                       MathHelper.Clamp((total - d) / (more ? Fade : Soft), 0, 1) * _shown;

            var lit = Pulse(d, pulse);
            var tip = Glint * fade;
            var wing = Glint * (fade * (0.55f + 0.45f * lit));

            var apex = centre + dir * (ChevronLength / 2);
            var left = centre - dir * (ChevronLength / 2) - across;
            var right = centre - dir * (ChevronLength / 2) + across;
            var back = dir * ChevronStroke;

            // Two arms, each a quad from the tip back to a wing, thick along the way of travel.
            _chevrons[at++] = new VertexPositionColor(apex, tip);
            _chevrons[at++] = new VertexPositionColor(left, wing);
            _chevrons[at++] = new VertexPositionColor(left - back, wing);

            _chevrons[at++] = new VertexPositionColor(apex, tip);
            _chevrons[at++] = new VertexPositionColor(left - back, wing);
            _chevrons[at++] = new VertexPositionColor(apex - back, tip);

            _chevrons[at++] = new VertexPositionColor(apex, tip);
            _chevrons[at++] = new VertexPositionColor(apex - back, tip);
            _chevrons[at++] = new VertexPositionColor(right - back, wing);

            _chevrons[at++] = new VertexPositionColor(apex, tip);
            _chevrons[at++] = new VertexPositionColor(right - back, wing);
            _chevrons[at++] = new VertexPositionColor(right, wing);
        }

        return at;
    }

    /// <summary>MumbleLink's (x, up, z) into Blish HUD's world, which has z up.</summary>
    private static Vector3 World((double X, double Up, double Z) point) =>
        new Vector3((float)point.X, (float)point.Z, (float)point.Up);

    public void Dispose()
    {
        _effect?.Dispose();
        _effect = null;
    }
}
