using System;
using System.Collections.Generic;

namespace GuildWarsCompanion.Bridge.Protocol;

/// <summary>
/// The part of a <see cref="GuidedTrail"/> worth drawing: from where the character is now, a short
/// way on along the trail.
///
/// <b>Why not the whole trail.</b> Drawn end to end, a trail across a map is a brass line over
/// everything between here and there, most of it behind hills the overlay cannot hide it behind.
/// The next stretch is all a walker follows; the column on the objective says where it ends.
///
/// <b>Measured along the trail, not as the crow flies</b>, so a switchback shows as much path as a
/// straight road does.
///
/// Metres, in MumbleLink's (x, up, z), the same as the trail. Compiled into the Blish HUD module,
/// so nothing here may be newer than C# on net48 allows, and nothing allocates: it runs every frame.
/// </summary>
public static class TrailAhead
{
    /// <summary>How far ahead of the character the trail is drawn, in metres along it.</summary>
    public const double Metres = 75;

    /// <summary>
    /// Fills <paramref name="into"/> with the trail from the point on it nearest
    /// <paramref name="at"/> to <paramref name="metres"/> further along, both ends cut exactly
    /// rather than rounded to the nearest recorded point. Returns true when that stops short of the
    /// trail's end — when there is more to come than is drawn.
    /// </summary>
    public static bool Cut(
        IReadOnlyList<(double X, double Up, double Z)> points,
        (double X, double Up, double Z) at,
        double metres,
        List<(double X, double Up, double Z)> into) =>
        Cut(points, at, 0, metres, into, out _);

    /// <summary>
    /// The same cut, begun <paramref name="skip"/> metres further on than the character, and saying
    /// how far along the whole trail it starts, in metres: what keeps anything drawn on the trail
    /// fixed to the ground while the cut slides along under it.
    /// </summary>
    public static bool Cut(
        IReadOnlyList<(double X, double Up, double Z)> points,
        (double X, double Up, double Z) at,
        double skip,
        double metres,
        List<(double X, double Up, double Z)> into,
        out double start)
    {
        into.Clear();
        start = 0;

        if (points.Count == 0) return false;

        if (points.Count == 1)
        {
            into.Add(points[0]);
            return false;
        }

        // The nearest point on any segment. Ties go to the later segment, so where a trail doubles
        // back past itself the character is taken to be further along it, never back at the start.
        var segment = 0;
        var fraction = 0.0;
        var nearest = double.MaxValue;

        for (var i = 0; i < points.Count - 1; i++)
        {
            var t = Project(points[i], points[i + 1], at);
            var distance = Squared(Lerp(points[i], points[i + 1], t), at);

            if (distance > nearest) continue;

            nearest = distance;
            segment = i;
            fraction = t;
        }

        for (var i = 0; i < segment; i++) start += Math.Sqrt(Squared(points[i], points[i + 1]));

        start += fraction * Math.Sqrt(Squared(points[segment], points[segment + 1]));

        var from = Lerp(points[segment], points[segment + 1], fraction);
        var next = segment + 1;

        // Step over the skip without drawing it, stopping at the trail's end if it comes first.
        var skipped = 0.0;

        while (next < points.Count)
        {
            var length = Math.Sqrt(Squared(from, points[next]));

            if (length > skip - skipped)
            {
                from = Lerp(from, points[next], (skip - skipped) / length);
                skipped = skip;
                break;
            }

            skipped += length;
            from = points[next++];
        }

        start += skipped;
        into.Add(from);

        // What is left of the segment underfoot, then whole segments, until the distance runs out.
        var left = metres;

        for (var i = next; i < points.Count; i++)
        {
            var length = Math.Sqrt(Squared(from, points[i]));

            if (length >= left)
            {
                into.Add(Lerp(from, points[i], length <= 0 ? 0 : left / length));
                return i < points.Count - 1 || length > left;
            }

            into.Add(points[i]);
            left -= length;
            from = points[i];
        }

        return false;
    }

    /// <summary>How far along a→b the point nearest p is, from 0 at a to 1 at b.</summary>
    private static double Project((double X, double Up, double Z) a, (double X, double Up, double Z) b, (double X, double Up, double Z) p)
    {
        var dx = b.X - a.X;
        var dUp = b.Up - a.Up;
        var dz = b.Z - a.Z;
        var length = dx * dx + dUp * dUp + dz * dz;

        if (length <= 0) return 0;

        var t = ((p.X - a.X) * dx + (p.Up - a.Up) * dUp + (p.Z - a.Z) * dz) / length;

        return t < 0 ? 0 : t > 1 ? 1 : t;
    }

    private static (double X, double Up, double Z) Lerp((double X, double Up, double Z) a, (double X, double Up, double Z) b, double t) =>
        (a.X + (b.X - a.X) * t, a.Up + (b.Up - a.Up) * t, a.Z + (b.Z - a.Z) * t);

    private static double Squared((double X, double Up, double Z) a, (double X, double Up, double Z) b)
    {
        var dx = a.X - b.X;
        var dUp = a.Up - b.Up;
        var dz = a.Z - b.Z;

        return dx * dx + dUp * dUp + dz * dz;
    }
}
