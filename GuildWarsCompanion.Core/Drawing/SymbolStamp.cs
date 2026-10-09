namespace GuildWarsCompanion.Drawing;

/// <summary>
/// Stamping a marker onto the dial without going through <c>ICanvas.DrawImage</c>.
///
/// The compass draws up to thirteen of these on every frame, thirty times a second, and that call
/// was measured on the phone as the whole of a leak: with the stamps on, the process grew about
/// 12.4 MB/min; with the same number of marks drawn as plain dots, 3.3; with no marks at all, 0.6.
/// Nothing else in <see cref="CompassRose"/> behaves that way — the ring, the ticks, the range
/// labels and the trail together are a rounding error — and six hours of the difference is the
/// <c>Scudo ERROR: internal map failure (error desc=Out of memory)</c> in the log.
///
/// So the image path is taken by hand instead: one <c>Paint</c>, one source rectangle and one
/// destination rectangle per kind of thing, built once and written to in place, then a single
/// draw of the native bitmap. The alternative — accepting the leak and shortening the session —
/// was measured too, and this keeps the game's own marker art, which is the one thing about this
/// screen a player already knows how to read.
///
/// The hand-written path depends on what the canvas and the image really are underneath, which
/// only the front end knows, so it is handed in as <see cref="Platform"/> at start-up: the
/// Avalonia app's SkiaStamp. This used to be an <c>#if ANDROID</c>;
/// since the drawing moved into a plain library, that symbol is never defined here and the fix
/// would have been lost without a sound.
///
/// Falls back to <c>ICanvas.DrawImage</c> whenever the objects underneath are not what the stamp
/// expects. A platform detail that changes shape should cost the leak again, not the symbols.
/// </summary>
public static class SymbolStamp
{
    /// <summary>The front end's direct path, or null where it has none.</summary>
    public static Func<ICanvas, IImage, float, float, float, float, bool>? Platform { get; set; }

    /// <summary>
    /// Draws one symbol, centred on a point, at the current transform.
    /// </summary>
    /// <returns>False when the platform path was not available and the caller should draw the
    /// image the ordinary way.</returns>
    public static bool TryDraw(ICanvas canvas, IImage image, float x, float y, float size, float alpha) =>
        Platform?.Invoke(canvas, image, x, y, size, alpha) ?? false;
}
