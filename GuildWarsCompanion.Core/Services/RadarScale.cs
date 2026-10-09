namespace GuildWarsCompanion.Services;

/// <summary>
/// Turns a distance into how far out from the middle of the dial to draw something.
///
/// The radar warning receiver convention, which is the whole reason the screen reads at a glance:
/// the middle is <em>here</em>, and the further out a mark sits the further away the thing is. On
/// a real set the radial axis is signal strength rather than range, but the two point the same
/// way — a strong emitter is a near one — so the instinct transfers.
///
/// Compressed rather than linear, and that is not decoration. A map is thousands of continent
/// units across while the things worth walking to are tens or hundreds away; on a linear dial
/// every landmark in the neighbourhood collapses into a blob at the centre pip and only the far
/// ones are readable, which is exactly backwards.
/// </summary>
public static class RadarScale
{
    /// <summary>
    /// The rings, in continent units. Each is four times the last, which is what makes a
    /// logarithmic radius space them evenly round the dial — 24%, 48%, 76%, 100%.
    ///
    /// The outermost was 8000 and is now 2500, and that single number is what the dial was
    /// getting wrong. Almost everything worth walking to on a map is inside a couple of thousand
    /// units, so reserving the outer quarter of the radius for the 2000-8000 band spent it on
    /// nothing and squeezed every real landmark into a thin mid-radius ring. Pulling the far
    /// edge in stretches the near band across the whole dial and pins genuinely distant things —
    /// a world boss two maps away — to the rim where they belong.
    /// </summary>
    public static readonly double[] Rings = [50, 200, 800, 2500];

    /// <summary>Anything past the outermost ring is pinned to the rim. Beyond this the direction
    /// is still true and the distance has stopped meaning anything on a dial.</summary>
    public static double MaxRange => Rings[^1];

    /// <summary>
    /// Where <paramref name="distance"/> sits, as a fraction of the dial's radius in 0..1.
    ///
    /// Logarithmic, offset by <see cref="Near"/> so that zero distance is the exact centre and
    /// the first few units do not fling a mark a quarter of the way out.
    /// </summary>
    public static double Fraction(double distance)
    {
        if (distance <= 0) return 0;

        var value = Math.Log(1 + distance / Near) / Math.Log(1 + MaxRange / Near);

        return Math.Clamp(value, 0, 1);
    }

    /// <summary>
    /// The distance at which the compression starts to bite, and the reason zero maps to the
    /// exact centre rather than to minus infinity.
    ///
    /// Worth knowing what this does <em>not</em> do: for anything much beyond it the curve is
    /// effectively log(d) - log(Near), so changing it slides every mark the same way and never
    /// changes how far apart two nearby marks are drawn. Relative resolution is set by
    /// <see cref="MaxRange"/> alone, which is why that is the number that moved.
    /// </summary>
    private const double Near = 25;

    /// <summary>Whether something is far enough out to be pinned rather than placed.</summary>
    public static bool BeyondRange(double distance) => distance > MaxRange;

    /// <summary>"125", "2.0k" — the label on a ring, short enough to sit on the line.</summary>
    public static string Label(double distance) =>
        distance >= 1000 ? $"{distance / 1000:0.#}k" : $"{distance:0}";
}
