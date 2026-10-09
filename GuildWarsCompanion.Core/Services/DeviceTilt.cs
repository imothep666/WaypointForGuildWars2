using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Which way up the phone is being held, in radians about the screen's own normal.
///
/// Read off the accelerometer rather than off the window, because the window never changes: the
/// app is locked to portrait on purpose (see MainActivity) and the compass is not an exception to
/// that. What turns is the dial drawn inside the window, so a player holding the phone sideways
/// still sees an upright instrument.
///
/// Zero is upright portrait. Positive is the phone turned clockwise in the hand, which is the
/// direction the drawing has to be turned back by.
/// </summary>
public sealed class DeviceTilt(IAccelerometer accelerometer) : IDisposable
{
    /// <summary>
    /// How much gravity has to be in the screen's plane before the reading means anything.
    ///
    /// A phone lying flat on a desk has almost all of gravity going through the screen and
    /// almost none across it, so the in-plane angle is noise — it will swing wildly from a
    /// passing lorry. Below this the last good angle is held instead, which is why a compass put
    /// down on a table stays where it was rather than spinning.
    ///
    /// A fraction of g: 0.3 is roughly seventeen degrees off flat.
    /// </summary>
    private const double Meaningful = 0.3;

    /// <summary>
    /// How quickly the angle is allowed to follow the sensor.
    ///
    /// Low-passed because the raw signal shakes with the hand holding it, and a dial that
    /// trembles is harder to read than one that lags. This is per sample at the UI rate
    /// <see cref="IAccelerometer.Start"/> asks for, so it settles in well under a second.
    /// </summary>
    private const double Follow = 0.15;

    private double _radians;
    private bool _seen;

    /// <summary>The current angle. Safe to read from the drawing thread.</summary>
    public double Radians => Volatile.Read(ref _radians);

    /// <summary>Whether the sensor is running. False on a device without an accelerometer, where
    /// the dial simply stays upright with the window.</summary>
    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning || !accelerometer.IsSupported) return;

        try
        {
            accelerometer.Start(OnReading);

            IsRunning = true;
        }
        catch (Exception)
        {
            // Some devices refuse the sensor outright. An untilted dial is a fine fallback.
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;

        IsRunning = false;

        try
        {
            accelerometer.Stop();
        }
        catch (Exception)
        {
            // Already stopped, or the platform is unhappy. Nothing depends on this succeeding.
        }
    }

    private void OnReading(double x, double y)
    {
        // Only the part of gravity lying in the plane of the screen says anything about how the
        // phone is turned. Straight down the screen's normal is a phone lying flat.
        var across = Math.Sqrt(x * x + y * y);

        if (across < Meaningful) return;

        var measured = TiltAngle.Of(x, y);

        Volatile.Write(ref _radians, _seen ? Smooth(Radians, measured) : measured);

        _seen = true;
    }

    /// <summary>
    /// Eases towards a new angle the short way round.
    ///
    /// The folding matters: without it, crossing the boundary between +180 and -180 makes the
    /// dial spin the whole way round rather than nudge a degree, which happens every time
    /// somebody holds the phone upside down.
    /// </summary>
    private static double Smooth(double current, double target) =>
        Bearing.Normalise(current + Bearing.Normalise(target - current) * Follow);

    public void Dispose() => Stop();
}
