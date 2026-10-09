namespace GuildWarsCompanion.Services;

/// <summary>
/// Turning a screen into the map's own upright rectangle, a quarter at a time.
///
/// The window never rotates — the app is portrait-locked at the activity, deliberately — so a
/// sideways phone is handled by drawing the map turned inside an unchanged window. That makes two
/// spaces: the screen, whose top-left is the corner of the handset, and the map's, whose top-left
/// is the corner of the rectangle the map is drawn in. A quarter turn swaps the sides of one to
/// get the other.
///
/// Pure and separate because the drawing and the hit test must agree exactly. They are written
/// in different places — <c>MapCanvas</c> turns the canvas, <c>MapViewModel</c> turns the tap —
/// and a sign error between them is a map that looks right and picks the wrong marker, which is
/// the kind of thing that is found weeks later by someone tapping a heart and getting a vista.
/// </summary>
public static class QuarterTurn
{
    /// <summary>
    /// The viewport as the map sees it: the screen, with its sides swapped on a quarter turn.
    /// </summary>
    public static (double Width, double Height) Extent(int quarter, double width, double height) =>
        quarter % 2 == 0 ? (width, height) : (height, width);

    /// <summary>
    /// A screen movement in the map's space — the inverse of the turn the canvas applies.
    ///
    /// The canvas is rotated by minus the quarter, so undoing it is a turn by plus the quarter.
    /// Written out per case rather than as a matrix: there are four, they are exact, and a
    /// rounded sine is a worse way to express a right angle than a swapped pair.
    ///
    /// <b>The screen's y grows downwards, and quarters 1 and 3 were the wrong way round because
    /// of it.</b> <c>ICanvas.Rotate</c> applies the ordinary rotation matrix, which in a y-down
    /// space turns clockwise — so a canvas turned by -90 sends a map point (x, y) to the screen
    /// at (y, -x), and the inverse of that is (-y, x) rather than the (y, -x) that a y-up
    /// derivation gives. Getting it backwards composes with the real turn into a half turn, which
    /// shows up as a map that pans away from the finger and markers that answer a tap mirrored
    /// through the middle. Reported from the phone, which is the only place it is visible.
    /// </summary>
    public static (double X, double Y) Unturn(int quarter, double x, double y) =>
        (((quarter % 4) + 4) % 4) switch
        {
            1 => (-y, x),
            2 => (-x, -y),
            3 => (y, -x),
            _ => (x, y)
        };

    /// <summary>
    /// A point on the screen, in the map's space.
    ///
    /// Both rectangles are centred on the same point — the canvas is turned about its middle — so
    /// this is the turn above taken about that middle rather than about a corner.
    /// </summary>
    public static (double X, double Y) FromScreen(
        int quarter, double x, double y, double screenWidth, double screenHeight)
    {
        var (width, height) = Extent(quarter, screenWidth, screenHeight);

        var (dx, dy) = Unturn(quarter, x - screenWidth / 2, y - screenHeight / 2);

        return (dx + width / 2, dy + height / 2);
    }
}
