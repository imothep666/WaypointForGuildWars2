namespace GuildWarsCompanion.Services;

/// <summary>
/// The map's two movements that take time: a zoom easing between levels, and the view gliding
/// after a character who has walked out of the middle.
///
/// Both used to happen in one frame. A zoom swapped the level and let the new tiles pop in over
/// the background; a character leaving <see cref="FollowBox"/> moved the centre a quarter of the
/// screen at once. Either is a jump, and a jump on a screen glanced at mid-fight reads as the map
/// having gone somewhere else.
///
/// Pure, like <see cref="FollowBox"/>: it holds the numbers and steps them by the time that has
/// passed, and the screen asks for frames only while <see cref="IsMoving"/>. A frame loop that
/// never stopped would redraw every tile thirty times a second for nothing, and per-frame drawing
/// is what cost the compass its memory.
/// </summary>
public sealed class MapMotion
{
    /// <summary>
    /// How long a zoom takes: a quarter of a second. Long enough to see where the map went, short
    /// enough that a second tap on the pad is not waiting for the first.
    /// </summary>
    public const double ZoomSeconds = 0.25;

    /// <summary>
    /// How quickly a glide closes the distance: about a seventh of a second to cover two thirds of
    /// what is left, so it has all but arrived in half a second.
    ///
    /// Towards a fixed point: where the character was when they left the middle. The first version
    /// moved the point with every report, and a character who keeps walking never lets a glide
    /// arrive, so the map slid under them the whole time and the frame loop never stopped, at a
    /// whole core of the phone. The half second they walk on during a glide leaves them a little
    /// off centre, which is well inside <see cref="FollowBox"/>.
    /// </summary>
    public const double GlideSeconds = 0.15;

    /// <summary>Close enough to call a glide finished, in pixels on the screen.</summary>
    private const double Arrived = 0.5;

    private Zooming? _zoom;

    private (double X, double Y)? _glide;

    /// <summary>
    /// How much bigger the map is drawn than its level's own pixels. One at rest; on the way in
    /// from one level out, it starts at a half and grows to one.
    /// </summary>
    public double Scale { get; private set; } = 1;

    public bool IsMoving => _zoom is not null || _glide is not null;

    public bool IsZooming => _zoom is not null;

    /// <summary>
    /// Starts easing from one level to another, holding one point of the world still on the
    /// screen: the point between the fingers, or the middle for the pad.
    /// </summary>
    /// <param name="held">The world point held still, in continent units.</param>
    /// <param name="offset">How far it sits from the middle of the view, in screen pixels. A screen
    /// distance, so the same number at either level.</param>
    /// <param name="unitsPerPixel">Continent units per pixel at the level being reached.</param>
    public void Zoom(int fromZoom, int toZoom, (double X, double Y) held, (double X, double Y) offset,
        double unitsPerPixel)
    {
        _glide = null;
        _zoom = new Zooming(fromZoom - toZoom, held, offset, unitsPerPixel, 0);

        Scale = Math.Pow(2, fromZoom - toZoom);
    }

    /// <summary>Starts gliding towards a point.</summary>
    public void GlideTo(double x, double y) => _glide = (x, y);

    /// <summary>Stops everything where it is, for a finger on the map or a new map.</summary>
    public void Stop()
    {
        _zoom = null;
        _glide = null;
        Scale = 1;
    }

    /// <summary>
    /// Steps by the time since the last frame and says where the middle of the view now is.
    /// </summary>
    /// <param name="unitsPerPixel">Continent units per pixel at the current level, for knowing
    /// when a glide is close enough to stop.</param>
    public (double X, double Y) Advance(double seconds, double centreX, double centreY, double unitsPerPixel)
    {
        seconds = Math.Max(0, seconds);

        if (_zoom is { } zoom)
        {
            var elapsed = zoom.Elapsed + seconds;
            var done = elapsed >= ZoomSeconds;
            var eased = done ? 1 : EaseOut(elapsed / ZoomSeconds);

            // The exponent is eased rather than the scale, so each half of the way is the same
            // doubling: a linear scale would rush the far end of a zoom out and crawl the near end.
            Scale = Math.Pow(2, zoom.Exponent * (1 - eased));

            _zoom = done ? null : zoom with { Elapsed = elapsed };

            // Whatever keeps the held point under the same pixel at this scale.
            var units = zoom.UnitsPerPixel / Scale;

            return (zoom.Held.X - zoom.Offset.X * units, zoom.Held.Y - zoom.Offset.Y * units);
        }

        if (_glide is { } target)
        {
            var keep = Math.Exp(-seconds / GlideSeconds);
            var x = target.X + (centreX - target.X) * keep;
            var y = target.Y + (centreY - target.Y) * keep;

            var left = Math.Max(Math.Abs(target.X - x), Math.Abs(target.Y - y)) / unitsPerPixel;

            if (left >= Arrived) return (x, y);

            _glide = null;

            return target;
        }

        return (centreX, centreY);
    }

    /// <summary>Fast at first and settling at the end, the way a thing thrown and caught moves.</summary>
    public static double EaseOut(double t)
    {
        t = Math.Clamp(t, 0, 1);

        return 1 - Math.Pow(1 - t, 3);
    }

    private sealed record Zooming(
        int Exponent, (double X, double Y) Held, (double X, double Y) Offset, double UnitsPerPixel,
        double Elapsed);
}
