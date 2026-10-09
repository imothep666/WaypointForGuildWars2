using System;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GuildWarsCompanion.Blish;

/// <summary>
/// The sparks: a few rising up the beam, a few thrown off the pulse's front as it runs out, and a
/// burst on arrival.
///
/// <b>Nothing is simulated.</b> Every spark is a number, and where it is, how bright and how big
/// all follow from that number and the clock: a spark's place in its own life is the time plus an
/// offset of its own, wrapped round its lifetime. So there is no list of live sparks to spawn into,
/// age and throw away — nothing to allocate, and nothing to go wrong if a frame is late.
///
/// <b>Each spark faces the camera</b>: a small diamond, bright in the middle and clear at its
/// points, laid out along the camera's own right and up, so it is a soft star from any side.
/// </summary>
public sealed partial class TrailEntity
{
    private static readonly Color Spark = new Color(255, 236, 170);

    /// <summary>How many sparks rise up the beam at once, how long one takes to rise, in
    /// seconds, and how big it is close to, in metres. Sized with the beam, so that from a long
    /// way off they still read as sparks and not as nothing.</summary>
    private const int BeamSparks = 24;
    private const float BeamSparkLife = 3f;
    private const float BeamSparkSize = 0.2f;

    /// <summary>How far they rise, against the beam's height: most of the way, so they are seen
    /// against the sky over a ridge, and not the whole way, where the beam has faded to nothing.</summary>
    private const float BeamSparkRise = 0.6f;

    /// <summary>
    /// One spark is thrown off the pulse every <see cref="PulseSparkGap"/> metres it runs, and lives
    /// <see cref="PulseSparkLife"/> seconds: at the pulse's speed, that is five or six alive at a
    /// time, all just behind its front. Enough to show a still frame where the pulse is; few enough
    /// not to be a trail of their own.
    /// </summary>
    private const float PulseSparkGap = 2.5f;
    private const float PulseSparkLife = 0.6f;
    private const float PulseSparkSize = 0.2f;

    /// <summary>How much a pulse spark grows per metre from the camera. At a fixed size they were
    /// rendered invisible twenty-five metres out, which is where the pulse is most of the time.</summary>
    private const float PulseSparkGrowth = 0.04f;
    private const float PulseSparkRise = 0.9f;

    /// <summary>How near the objective, in metres over the ground, counts as arriving.</summary>
    private const float Arrive = 4f;

    /// <summary>The arrival burst: how many sparks, how long it lasts in seconds, how big each is,
    /// and the fall that brings them back down, in metres a second squared.</summary>
    private const int BurstSparks = 30;
    private const float BurstLife = 1.6f;
    private const float BurstSize = 0.16f;
    private const float BurstFall = 6f;

    private static readonly int MostPulseSparks = (int)(PulseSparkLife * PulseSpeed / PulseSparkGap) + 2;

    private readonly VertexPositionColor[] _sparks =
        new VertexPositionColor[(BeamSparks + MostPulseSparks + BurstSparks) * 12];

    /// <summary>When the character reached the objective, on the clock of <see cref="_seconds"/>;
    /// null until then. Once per trail: a new trail clears it.</summary>
    private double? _arrivedAt;

    /// <summary>All the sparks this frame, into <see cref="_sparks"/>; returns how many
    /// vertices.</summary>
    private int Sparks(Vector3 foot, Vector3 camera, float total, bool more, float pulse, Vector3 right, Vector3 up)
    {
        var away = Vector3.Distance(foot, camera);
        var at = 0;

        at = BeamRise(at, foot, away, right, up);
        at = PulseSparks(at, camera, total, more, pulse, right, up);

        if (_arrivedAt is { } arrived) at = Burst(at, foot, (float)(_seconds - arrived), right, up);

        return at;
    }

    /// <summary>Sparks rising slowly up the beam in a loose spiral, each fading in at the foot
    /// and out before the top.</summary>
    private int BeamRise(int at, Vector3 foot, float away, Vector3 right, Vector3 up)
    {
        var half = BeamHalf(away);
        var height = BeamHeight(away) * BeamSparkRise;
        var grown = half / BeamHalf(0);

        for (var i = 0; i < BeamSparks; i++)
        {
            var life = BeamSparkLife * (0.7f + 0.6f * Hash(i, 1));
            var t = Wrap((_seconds + Hash(i, 2) * life) / life);

            var angle = Hash(i, 3) * MathHelper.TwoPi + t * 1.5f;
            var radius = half * (0.4f + 0.8f * Hash(i, 4));
            var position = foot + new Vector3((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius, t * height);

            var bright = Math.Min(1, t / 0.1f) * Math.Min(1, (1 - t) / 0.5f) * _shown;

            at = Diamond(at, position, BeamSparkSize * grown * (0.7f + 0.6f * Hash(i, 5)), Spark * bright, right, up);
        }

        return at;
    }

    /// <summary>
    /// Sparks thrown up off the track as the pulse passes, each where the pulse's front was when it
    /// was thrown. Their places along the track are fixed for every pulse, a little uneven so they
    /// do not read as a row; which of them are alive depends only on how far the pulse has run.
    /// </summary>
    private int PulseSparks(int at, Vector3 camera, float total, bool more, float pulse, Vector3 right, Vector3 up)
    {
        if (_ahead.Count < 2) return at;

        var oldest = (int)Math.Max(0, Math.Floor((pulse - PulseSparkLife * PulseSpeed) / PulseSparkGap) - 1);
        var newest = (int)Math.Floor(pulse / PulseSparkGap);
        var segment = 0;

        for (var j = oldest; j <= newest && at + 12 <= _sparks.Length; j++)
        {
            var d = (j + 0.5f + (Hash(j, 6) - 0.5f) * 0.8f) * PulseSparkGap;

            if (d > total || d > pulse) break;

            var age = (pulse - d) / PulseSpeed / PulseSparkLife;

            if (age >= 1 || !At(d, ref segment, out var position, out var dir)) continue;

            var flat = new Vector2(dir.X, dir.Y);

            if (flat.LengthSquared() < 1e-6f) continue;

            flat.Normalize();

            var across = new Vector3(-flat.Y, flat.X, 0) * ((Hash(j, 7) - 0.5f) * 2 * HalfWidth);
            var lifted = position + across + new Vector3(0, 0, Lift + age * PulseSparkRise);

            var bright = (1 - age) * (1 - age) * Fading(d, total, more, Fade);

            var size = PulseSparkSize * (1 - 0.5f * age) * (1 + Vector3.Distance(lifted, camera) * PulseSparkGrowth);

            at = Diamond(at, lifted, size, Spark * bright, right, up);
        }

        return at;
    }

    /// <summary>A fountain on the objective when the character reaches it: sparks thrown up and
    /// out in every direction, falling back as they fade.</summary>
    private int Burst(int at, Vector3 foot, float age, Vector3 right, Vector3 up)
    {
        if (age >= BurstLife) return at;

        var left = 1 - age / BurstLife;

        for (var k = 0; k < BurstSparks && at + 12 <= _sparks.Length; k++)
        {
            var angle = Hash(k, 8) * MathHelper.TwoPi;
            var rise = 3f + 3f * Hash(k, 9);
            var outward = 1.5f + 2f * Hash(k, 10);

            var position = foot + new Vector3(
                (float)Math.Cos(angle) * outward * age,
                (float)Math.Sin(angle) * outward * age,
                Lift + rise * age - BurstFall / 2 * age * age);

            at = Diamond(at, position, BurstSize * (0.6f + 0.4f * left), Spark * left, right, up);
        }

        return at;
    }

    /// <summary>One spark: a diamond facing the camera, a little taller than wide, bright at its
    /// centre and clear at its four points — four triangles round the middle.</summary>
    private int Diamond(int at, Vector3 centre, float size, Color colour, Vector3 right, Vector3 up)
    {
        if (colour.A == 0 || at + 12 > _sparks.Length) return at;

        var r = right * size;
        var u = up * (size * 1.5f);
        var clear = Color.Transparent;

        _sparks[at++] = new VertexPositionColor(centre, colour);
        _sparks[at++] = new VertexPositionColor(centre + u, clear);
        _sparks[at++] = new VertexPositionColor(centre + r, clear);

        _sparks[at++] = new VertexPositionColor(centre, colour);
        _sparks[at++] = new VertexPositionColor(centre + r, clear);
        _sparks[at++] = new VertexPositionColor(centre - u, clear);

        _sparks[at++] = new VertexPositionColor(centre, colour);
        _sparks[at++] = new VertexPositionColor(centre - u, clear);
        _sparks[at++] = new VertexPositionColor(centre - r, clear);

        _sparks[at++] = new VertexPositionColor(centre, colour);
        _sparks[at++] = new VertexPositionColor(centre - r, clear);
        _sparks[at++] = new VertexPositionColor(centre + u, clear);

        return at;
    }

    /// <summary>The fractional part: a value's place in its own cycle, 0 to 1.</summary>
    private static float Wrap(double value) => (float)(value - Math.Floor(value));

    /// <summary>A fixed number from 0 to 1 for spark <paramref name="i"/>, one per
    /// <paramref name="salt"/>: the same every frame, so each spark keeps its own path.</summary>
    private static float Hash(int i, int salt) =>
        Wrap(Math.Sin(i * 12.9898 + salt * 78.233) * 43758.5453);
}
