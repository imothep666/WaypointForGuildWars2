namespace GuildWarsCompanion.Services;

/// <summary>
/// When a map that is following the character has to move.
///
/// Not "keep the dot in the middle". The bridge reports ten times a second, and a map pinned
/// exactly to the character slides under the eye continuously while they walk — which is
/// unreadable, and which re-fetches a ring of tiles for every step. So the character is allowed
/// to wander a box in the middle of the screen, and the map only moves when they leave it.
///
/// The same idea as a camera's dead zone in a platformer, and for the same reason: motion should
/// cost something, or everything is always moving.
///
/// Pure, like <see cref="ZoomPad"/> and <see cref="QuarterTurn"/>: the rule is testable and the
/// screen that applies it is not.
/// </summary>
public static class FollowBox
{
    /// <summary>
    /// How much of the viewport the character may roam before the map follows, as a share of
    /// each side.
    ///
    /// Half. A quarter moves the map almost as often as centring on every report does; three
    /// quarters lets the character reach the edge, which is where they stop being surrounded by
    /// the thing the screen is for — what is *around* them.
    /// </summary>
    public const double Share = 0.5;

    /// <summary>
    /// The box at a zoom level: tighter at the top two. Close in, a character crosses half the
    /// screen in a few steps, and the ring stood well off the middle most of the time it was
    /// being followed. One level from the top it is 0.4 of each side, at the top 0.3: more
    /// glides, each of them short (see <see cref="MapMotion.GlideSecondsAt"/>), and only where a
    /// step is many pixels. Further out, the half above.
    /// </summary>
    /// <param name="level">One for the widest view, <paramref name="levels"/> for the closest.</param>
    public static double ShareAt(int level, int levels) => (levels - level) switch
    {
        <= 0 => 0.3,
        1 => 0.4,
        _ => Share
    };

    /// <summary>
    /// Whether the character, at this point in the viewport, has left the middle box.
    ///
    /// Both arguments are in the map's own space — the screen with its sides swapped on a quarter
    /// turn — so a sideways map asks this question about the box it actually has.
    /// </summary>
    public static bool Escaped(double x, double y, double width, double height, double share = Share) =>
        Math.Abs(x - width / 2) > width * share / 2 ||
        Math.Abs(y - height / 2) > height * share / 2;
}
