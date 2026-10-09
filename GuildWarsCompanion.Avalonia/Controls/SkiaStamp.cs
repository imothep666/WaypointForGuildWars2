using Microsoft.Maui.Graphics.Skia;

using SkiaSharp;

using ICanvas = Microsoft.Maui.Graphics.ICanvas;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace GuildWarsCompanion.Ava.Controls;

/// <summary>
/// The Avalonia app's half of <see cref="Drawing.SymbolStamp"/>: a marker or tile drawn straight
/// onto the Skia canvas with one paint shared by every stamp, rather than through
/// <c>ICanvas.DrawImage</c>, whose per-call allocations were the whole of the compass's memory
/// growth on Android.
///
/// The bitmap itself, not an SKImage made from it. A cached SKImage per bitmap was the first
/// version, and on Android's GPU canvas it drew the 32-pixel marker icons and silently nothing
/// for the 256-pixel map tiles, while the same tiles through DrawBitmap drew fine.
///
/// Called from the render thread only, one frame at a time, which is what makes the shared paint
/// safe.
/// </summary>
public static class SkiaStamp
{
    /// <summary>
    /// No edge anti-aliasing. Everything stamped is an axis-aligned square — the map turns only
    /// by quarters — and anti-aliased, each edge two tiles share was drawn at partial coverage
    /// from both sides, which let the dark background through as a hairline between every tile.
    /// </summary>
    private static readonly SKPaint Paint = new() { IsAntialias = false };

    public static bool TryDraw(ICanvas canvas, IImage image, float x, float y, float size, float alpha)
    {
        if (canvas is not SkiaCanvas { Canvas: { } surface } || image is not SkiaImage { PlatformRepresentation: { } bitmap })
            return false;

        Paint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(alpha * 255, 0, 255));

        var half = size / 2;

        surface.DrawBitmap(bitmap, new SKRect(x - half, y - half, x + half, y + half), Paint);

        return true;
    }
}
