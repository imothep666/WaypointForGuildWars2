namespace GuildWarsCompanion.Services;

/// <summary>
/// Where something is, relative to the way you are looking.
///
/// Pure arithmetic over continent coordinates, kept apart from anything that streams, draws or
/// fetches so it can be tested against known angles. Every value here is in the one coordinate
/// space the whole feature depends on: continent coordinates, where X grows east and Y grows
/// south, which is what MumbleLink's playerX/playerY and every /v2/continents coordinate use.
/// </summary>
public static class Bearing
{
    /// <summary>
    /// The compass bearing from one continent point to another, clockwise from map north.
    ///
    /// North is -Y because continent Y grows southwards, which is the one sign in this file
    /// worth staring at: getting it wrong mirrors every needle without making any single value
    /// look obviously wrong.
    /// </summary>
    public static double To(double fromX, double fromY, double toX, double toY) =>
        Math.Atan2(toX - fromX, -(toY - fromY));

    /// <summary>
    /// How far to turn to be looking at the target: negative left, positive right, zero straight
    /// ahead.
    ///
    /// This is the number the screen actually draws. An absolute bearing would make the player
    /// do the subtraction in their head while something is eating them.
    /// </summary>
    public static double Relative(
        double fromX, double fromY, double facingRadians, double toX, double toY) =>
        Normalise(To(fromX, fromY, toX, toY) - facingRadians);

    /// <summary>
    /// Folds an angle into (-π, π].
    ///
    /// Without this a target slightly to the left reads as almost a full turn to the right, and
    /// the needle takes the long way round every time it crosses behind the player.
    /// </summary>
    public static double Normalise(double radians)
    {
        var turn = Math.Tau;

        radians %= turn;

        if (radians > Math.PI) radians -= turn;
        if (radians <= -Math.PI) radians += turn;

        return radians;
    }

    /// <summary>
    /// Straight-line distance in continent units.
    ///
    /// Deliberately not converted to anything friendlier. Continent units are what the game's own
    /// map uses, so a number here is comparable with a number there; inventing "metres" would
    /// mean picking a conversion and defending it.
    /// </summary>
    public static double Distance(double fromX, double fromY, double toX, double toY) =>
        Math.Sqrt((toX - fromX) * (toX - fromX) + (toY - fromY) * (toY - fromY));

    /// <summary>
    /// A bearing as a point of the compass: "north", "north-east" and so on.
    ///
    /// For the map rather than the dial, and the difference is the point. The dial says "bear
    /// left" because it is read while walking and the player cannot see the target; a map is
    /// north-up and the marker is on it, so the useful words are the ones that match what the eye
    /// is already doing. Eight points rather than sixteen — nobody navigates by north-north-east
    /// off a phone.
    /// </summary>
    /// <param name="radians">Clockwise from map north, as <see cref="To"/> returns.</param>
    public static string Cardinal(double radians)
    {
        // Into eighths of a turn, offset by half a sector so each name covers 45 degrees centred
        // on its own direction rather than starting at it.
        var eighth = (int)Math.Round(Normalise(radians) / (Math.Tau / 8));

        return ((((eighth % 8) + 8) % 8) switch
        {
            1 => L.T("north-east"),
            2 => L.T("east"),
            3 => L.T("south-east"),
            4 => L.T("south"),
            5 => L.T("south-west"),
            6 => L.T("west"),
            7 => L.T("north-west"),
            _ => L.T("north")
        });
    }

    /// <summary>
    /// The instruction, in the words somebody would use out loud.
    ///
    /// Bands rather than degrees: the useful question mid-fight is "which way do I turn", and a
    /// number to one decimal place answers it worse than four words do. The exact angle is still
    /// drawn by the needle for anyone who wants it.
    /// </summary>
    public static string Describe(double relativeRadians)
    {
        var degrees = relativeRadians * 180 / Math.PI;
        var turn = Math.Abs(degrees);
        var left = degrees < 0;

        // Whole phrases rather than a word and a side: another language may put the side first.
        return turn switch
        {
            < 10 => L.T("straight ahead"),
            < 60 => left ? L.T("bear left") : L.T("bear right"),
            < 120 => left ? L.T("turn left") : L.T("turn right"),
            < 170 => left ? L.T("hard left") : L.T("hard right"),
            _ => L.T("turn around")
        };
    }
}
