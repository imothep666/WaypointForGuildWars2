namespace GuildWarsCompanion.Services;

/// <summary>
/// Turning an accelerometer reading into how far the phone has been turned in the hand.
///
/// Its own file, away from <see cref="DeviceTilt"/>, for one reason: that class talks to a sensor
/// and keeps state between readings, and this is the half that needed testing.
/// </summary>
public static class TiltAngle
{
    /// <summary>
    /// The angle the phone is held at, clockwise from upright, from the part of gravity lying in
    /// the plane of the screen.
    ///
    /// MAUI reports the <em>reaction</em> to gravity rather than gravity itself, so a phone held
    /// upright reads about (0, +1) — up the screen, not down it. That is what makes the wanted
    /// angle <c>atan2(-x, y)</c>.
    ///
    /// Two wrong versions got here before this one, and both were sign errors that a screenshot
    /// could not show:
    ///
    /// <list type="bullet">
    /// <item><c>atan2(-x, -y)</c> put upright at 180 degrees, so the "ahead" marker sat at the
    /// bottom of the dial.</item>
    /// <item><c>atan2(x, -y)</c> kept that half turn and inverted the direction as well, which is
    /// why it then read upside down at every angle.</item>
    /// </list>
    ///
    /// The lesson is in <c>DeviceTiltTests</c>: assert all four quarters, because upright alone
    /// passes under conventions that disagree about everything else.
    /// </summary>
    public static double Of(double x, double y) => Math.Atan2(-x, y);
}

/// <summary>
/// Which quarter turn the phone is closest to, held steady across the boundaries.
///
/// The dial can turn smoothly because it is a circle, but text cannot: a readout at 23 degrees is
/// just harder to read than one at zero. So the words and the controls snap to the nearest
/// quarter turn while the dial keeps its continuous angle, and the difference between the two is
/// what the dial draws itself at.
/// </summary>
public sealed class SnappedOrientation
{
    /// <summary>
    /// How far past halfway the phone has to go before the snap follows it.
    ///
    /// Without this the layout flips back and forth every time the angle wobbles across the
    /// halfway line, which is exactly where somebody resting a phone against a mug tends to hold
    /// it. Sixty degrees rather than forty-five means a deliberate turn moves it and a tremble
    /// does not.
    /// </summary>
    private const double Commit = Math.PI / 3;

    /// <summary>Quarter turns clockwise from upright: 0, 1, 2 or 3.</summary>
    public int Quarter { get; private set; }

    /// <summary>The snapped angle itself, in radians clockwise from upright.</summary>
    public double Radians => Quarter * Math.PI / 2;

    /// <summary>
    /// What is left over once the snap is taken out: the small angle the dial turns by so that it
    /// sits exactly upright while the text sits at a right angle.
    /// </summary>
    public double ResidualOf(double tilt) => Bearing.Normalise(tilt - Radians);

    /// <summary>
    /// Follows the phone, but only once it has clearly committed to a new quarter.
    /// </summary>
    /// <returns>True when the quarter changed, so the caller can re-lay the screen out rather
    /// than doing it on every frame.</returns>
    public bool Update(double tilt)
    {
        if (Math.Abs(ResidualOf(tilt)) <= Commit) return false;

        // Nearest quarter to where the phone actually is, not simply the next one round: a phone
        // turned straight from upright to upside down should not stop at sideways on the way.
        var wanted = ((int)Math.Round(tilt / (Math.PI / 2)) % 4 + 4) % 4;

        if (wanted == Quarter) return false;

        Quarter = wanted;

        return true;
    }
}
