using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;

using Microsoft.Maui.Graphics.Skia;

using SkiaSharp;

using IDrawable = Microsoft.Maui.Graphics.IDrawable;
using RectF = Microsoft.Maui.Graphics.RectF;

namespace GuildWarsCompanion.Ava.Controls;

/// <summary>
/// MAUI's GraphicsView, for the drawables in Core: the map and the compass dial.
///
/// They are written against Microsoft.Maui.Graphics' ICanvas, and Avalonia draws with Skia, so
/// this hands them Avalonia's own Skia canvas wrapped as one — the same drawing code, no second
/// copy of it. <see cref="Invalidate"/> asks for a frame, as GraphicsView's does.
/// </summary>
public sealed class DrawableView : Control
{
    private readonly SkiaCanvas _canvas = new();

    public IDrawable? Drawable { get; set; }

    /// <summary>
    /// Asks for a frame, from any thread. The map's view model raises Changed from wherever its
    /// tile loads finish, which MAUI's GraphicsView tolerates; Avalonia's InvalidateVisual throws
    /// off the UI thread, and the throw vanished inside the load and left the map undrawn.
    /// </summary>
    public void Invalidate()
    {
        if (Dispatcher.UIThread.CheckAccess())
            InvalidateVisual();
        else
            Dispatcher.UIThread.Post(InvalidateVisual);
    }

    public override void Render(DrawingContext context)
    {
        // Transparent, but painted: an unpainted control is not hit, and the map is driven by
        // pointer input on exactly this surface.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        if (Drawable is { } drawable) context.Custom(new Frame(new Rect(Bounds.Size), drawable, _canvas));
    }

    private sealed class Frame(Rect bounds, IDrawable drawable, SkiaCanvas canvas) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;

            using var lease = feature.Lease();
            var surface = lease.SkCanvas;

            surface.Save();
            surface.ClipRect(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height));

            // One SkiaCanvas per view, pointed at this frame's surface: it carries the drawing
            // state the drawables set (colours, stroke, font size), and building one per frame at
            // thirty frames a second is garbage nobody needs.
            canvas.Canvas = surface;
            canvas.ResetState();
            drawable.Draw(canvas, new RectF(0, 0, (float)bounds.Width, (float)bounds.Height));
            canvas.Canvas = null;

            surface.Restore();
        }
    }
}
