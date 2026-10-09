namespace GuildWarsCompanion.Services;

/// <summary>
/// Whether a report from the bridge changed anything the map would draw differently.
///
/// The bridge reports ten times a second, and every report used to redraw the whole map: around
/// forty tiles, every marker, every route. With the character standing still that was ten
/// identical frames a second, measured at about 70% of a core on the phone, for a picture that
/// had not changed. The text under the map updates through its own bindings; only what the canvas
/// draws needs a frame.
///
/// Pure, like <see cref="FollowBox"/>.
/// </summary>
public static class MapRedraw
{
    /// <summary>
    /// Whether the character's dot has moved on the screen far enough to draw again: appeared,
    /// gone, or shifted by at least one step.
    ///
    /// The step is a screen point, not a device pixel: about 2.3 pixels on the test phone. A dot
    /// moving by less is not something the eye follows, and a pixel's threshold still redrew on
    /// nearly every report of a walking character, at about 38% of a core.
    /// </summary>
    /// <param name="unitsPerStep">Continent units in one step, at the drawn scale.</param>
    public static bool PlayerMoved((double X, double Y)? before, (double X, double Y)? after,
        double unitsPerStep)
    {
        if (before is not { } was || after is not { } now) return before.HasValue != after.HasValue;

        var dx = (now.X - was.X) / unitsPerStep;
        var dy = (now.Y - was.Y) / unitsPerStep;

        return dx * dx + dy * dy >= 1;
    }
}
