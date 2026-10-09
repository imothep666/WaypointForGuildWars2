namespace GuildWarsCompanion.Services;

/// <summary>Where one of the zoom pad's buttons sits, in the map's own pixels.</summary>
public readonly record struct PadButton(double X, double Y, double Side)
{
    public double CentreX => X + Side / 2;

    public double CentreY => Y + Side / 2;

    /// <summary>Whether a point in the map's space is on this button.</summary>
    public bool Holds(double x, double y) =>
        x >= X && x <= X + Side && y >= Y && y <= Y + Side;
}

/// <summary>
/// The zoom pad: plus above, minus below, the level between them.
///
/// Pinching works and is not what anybody reaches for on a map they are reading one-handed with a
/// game running on the other screen. The game's own map zooms in steps from a pair of buttons, and
/// steps are what this screen has anyway — the tile service renders discrete levels, so a
/// continuous gesture was always being quantised behind the player's back.
///
/// Laid out in the map's own space rather than the screen's, which is what makes it turn with the
/// map: the canvas is already rotated by the quarter, so a pad placed in the map's bottom-right
/// corner is in the bottom-right corner as the player sees it, whichever way up the phone is. The
/// hit test reads the same rectangles through the same turn, so the button that lights up is the
/// button that was pressed.
///
/// Pure, for the reason <see cref="QuarterTurn"/> is: the drawing and the hit test are written in
/// different files and must not be able to disagree.
///
/// <b>Sized in density-independent units, placed in map pixels.</b> The map's pixels are device
/// pixels, because a tile is 256 of them and is drawn at its own resolution. The pad was sized in
/// them too — 120 for a button — which was right on the phone it was tuned on and twice the size
/// on a desktop at 125 %: the same number of pixels is a much bigger thing where pixels are bigger.
/// So the sizes below are units, and every call says how many pixels a unit is.
/// </summary>
public static class ZoomPad
{
    /// <summary>
    /// A button's side, in density-independent units.
    ///
    /// Fifty-one: the 120 pixels the pad was tuned at, on the phone it was tuned on, which runs at
    /// 375 dpi — 2.34 pixels a unit — because its display size is set one step up from the
    /// panel's own 420. Comfortably over Android's minimum touch target of forty-four. The markers
    /// are smaller than this and that is deliberate: a marker is aimed at deliberately, a zoom
    /// button is jabbed at.
    /// </summary>
    public const double SideUnits = 51;

    /// <summary>How far the pad sits from the corner, in units: the 48 pixels it was tuned at.</summary>
    public const double MarginUnits = 20;

    /// <summary>The gap the level sits in, between the two buttons, in units: the 76 pixels it
    /// was tuned at.</summary>
    public const double GapUnits = 32;

    /// <summary>A button's side in map pixels, at <paramref name="density"/> pixels a unit.</summary>
    public static double Side(double density) => SideUnits * density;

    /// <summary>How far the pad sits from the corner, in map pixels.</summary>
    public static double Margin(double density) => MarginUnits * density;

    /// <summary>The whole pad's height in map pixels, for anything that needs to keep clear of it.</summary>
    public static double Height(double density) => (SideUnits * 2 + GapUnits) * density;

    /// <summary>Zoom in. Uppermost, because the map gets bigger upwards on every control anybody
    /// has used.</summary>
    public static PadButton Plus(double width, double height, double density) =>
        new(Left(width, density), Top(height, density), Side(density));

    /// <summary>Zoom out.</summary>
    public static PadButton Minus(double width, double height, double density) =>
        new(Left(width, density), Top(height, density) + (SideUnits + GapUnits) * density, Side(density));

    /// <summary>Where the level is written: the gap between the buttons.</summary>
    public static (double X, double Y, double Width, double Height) Level(
        double width, double height, double density) =>
        (Left(width, density), Top(height, density) + Side(density), Side(density), GapUnits * density);

    private static double Left(double width, double density) => width - Margin(density) - Side(density);

    private static double Top(double height, double density) => height / 2 - Height(density) / 2;
}
