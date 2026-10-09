using GuildWarsCompanion.Services;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Which way up the phone is being held, from the accelerometer.
///
/// Four readings, one per quarter turn. The whole point is the <em>sign</em>: upright reads zero
/// under either convention, so a wrong sign leaves the dial looking perfect in a screenshot and
/// rotating the wrong way the moment somebody turns the handset. That is precisely how the first
/// version got through.
///
/// MAUI reports the reaction to gravity rather than gravity itself, so upright is (0, +1) — up
/// the screen. Two earlier versions of this got the convention wrong in ways that only showed up
/// once somebody physically turned the handset.
/// </summary>
public class DeviceTiltTests
{
    private static double Degrees(double radians) => radians * 180 / Math.PI;

    [Fact]
    public void Upright_is_zero()
    {
        Assert.Equal(0, Degrees(TiltAngle.Of(0, 1)), 6);
    }

    /// <summary>
    /// Turned a quarter clockwise — the top of the phone points right — so the reaction to
    /// gravity now lies along the device's -X. The angle has to be positive, or the dial turns
    /// with the phone instead of against it.
    /// </summary>
    [Fact]
    public void A_quarter_turn_clockwise_is_positive()
    {
        Assert.Equal(90, Degrees(TiltAngle.Of(-1, 0)), 6);
    }

    [Fact]
    public void A_quarter_turn_anticlockwise_is_negative()
    {
        Assert.Equal(-90, Degrees(TiltAngle.Of(1, 0)), 6);
    }

    [Fact]
    public void Upside_down_is_half_a_turn()
    {
        Assert.Equal(180, Math.Abs(Degrees(TiltAngle.Of(0, -1))), 6);
    }

    /// <summary>
    /// Halfway round a quarter turn reads as halfway, so the dial tracks the handset smoothly
    /// rather than snapping between the four corners.
    /// </summary>
    [Fact]
    public void Part_of_a_turn_reads_as_part_of_a_turn()
    {
        var diagonal = Math.Sqrt(0.5);

        Assert.Equal(45, Degrees(TiltAngle.Of(-diagonal, diagonal)), 4);
        Assert.Equal(-45, Degrees(TiltAngle.Of(diagonal, diagonal)), 4);
    }
}

/// <summary>
/// Snapping the readable parts of the screen to quarter turns.
/// </summary>
public class SnappedOrientationTests
{
    private static double Turns(double quarters) => quarters * Math.PI / 2;

    [Fact]
    public void It_starts_upright()
    {
        Assert.Equal(0, new SnappedOrientation().Quarter);
    }

    /// <summary>
    /// A small tilt must not move it. Somebody resting a phone against a mug holds it at twenty
    /// degrees, and a layout that flipped there would be unusable.
    /// </summary>
    [Fact]
    public void A_small_tilt_does_not_move_it()
    {
        var snap = new SnappedOrientation();

        Assert.False(snap.Update(Turns(0.2)));
        Assert.Equal(0, snap.Quarter);
    }

    [Fact]
    public void A_clear_quarter_turn_moves_it()
    {
        var snap = new SnappedOrientation();

        Assert.True(snap.Update(Turns(1)));
        Assert.Equal(1, snap.Quarter);
    }

    /// <summary>
    /// Once committed it stays: the residual is measured from the new quarter, so holding the
    /// phone steadily sideways does not keep re-triggering a layout pass.
    /// </summary>
    [Fact]
    public void It_settles_once_it_has_moved()
    {
        var snap = new SnappedOrientation();

        snap.Update(Turns(1));

        Assert.False(snap.Update(Turns(1)));
        Assert.False(snap.Update(Turns(1.1)));
        Assert.Equal(1, snap.Quarter);
    }

    /// <summary>
    /// Turned straight over, it lands upside down rather than stopping at sideways on the way.
    /// </summary>
    [Fact]
    public void A_half_turn_goes_all_the_way()
    {
        var snap = new SnappedOrientation();

        snap.Update(Math.PI);

        Assert.Equal(2, snap.Quarter);
    }

    [Fact]
    public void Anticlockwise_lands_on_the_fourth_quarter()
    {
        var snap = new SnappedOrientation();

        snap.Update(Turns(-1));

        Assert.Equal(3, snap.Quarter);
    }

    /// <summary>
    /// The residual is what keeps the dial upright while the text is square: the snap plus the
    /// residual must always add back up to where the phone actually is.
    /// </summary>
    [Fact]
    public void The_snap_and_the_residual_add_back_up()
    {
        var snap = new SnappedOrientation();

        snap.Update(Turns(1));

        var tilt = Turns(1) + 0.2;

        Assert.Equal(0.2, snap.ResidualOf(tilt), 6);
        Assert.Equal(tilt, Bearing.Normalise(snap.Radians + snap.ResidualOf(tilt)), 6);
    }
}
