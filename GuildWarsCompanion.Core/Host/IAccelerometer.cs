namespace GuildWarsCompanion.Host;

/// <summary>
/// Gravity, as the phone's own accelerometer reports it, for <see cref="Services.DeviceTilt"/>.
///
/// Readings are in g along the screen's axes: X to the right, Y up the screen. Only the in-plane
/// part is handed on because nothing here needs the third axis.
/// </summary>
public interface IAccelerometer
{
    /// <summary>False on a device without one, or a front end that cannot read it.</summary>
    bool IsSupported { get; }

    /// <summary>Starts readings at a rate fit for a UI. May throw if the platform refuses.</summary>
    void Start(Action<double, double> reading);

    void Stop();
}

/// <summary>For a front end with no sensor: the compass then stays upright with the window.</summary>
public sealed class NoAccelerometer : IAccelerometer
{
    public bool IsSupported => false;

    public void Start(Action<double, double> reading)
    {
    }

    public void Stop()
    {
    }
}
