using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace GuildWarsCompanion.Bridge.Views;

/// <summary>
/// A window that draws its own title bar.
///
/// The point is not decoration: the system bar was saying "Waypoint PC Bridge" directly above
/// a masthead saying the same thing, so the window announced itself twice. Take the bar away and
/// the masthead becomes the bar.
///
/// What that costs is everything the frame was doing for free — moving, resizing, and the edge
/// that tells a dark window apart from a dark desktop — so each has to be given back, and the
/// two platforms want it given back differently:
///
/// <b>Windows</b> — the frame stays and the client area is extended over it. Snapping, the
/// resize borders, the drop shadow and Aero's window animations all keep working because they
/// are still the frame's job; only the painting is ours.
///
/// <b>X11</b> — there is no equivalent, so the decorations come off entirely and this class
/// hit-tests the edges itself. A window manager that ignores the hint and keeps drawing a title
/// bar is the one case where this looks worse than doing nothing, which is why it is asked for
/// per platform rather than everywhere.
/// </summary>
public abstract class AppWindow : Window
{
    /// <summary>
    /// How close to an edge counts as grabbing it. Six pixels: enough to hit with a mouse
    /// without swallowing clicks meant for a control that sits against the edge.
    /// </summary>
    private const double Grip = 6;

    /// <summary>Whether the edges are this window's problem. See the class remarks.</summary>
    private readonly bool _ownEdges;

    private readonly Dictionary<StandardCursorType, Cursor> _cursors = [];

    protected AppWindow()
    {
        // The same on both platforms, which took a wrong turn to arrive at. Extending the
        // client area over the frame looked like the better trade on Windows — it keeps
        // snapping and the shadow — but Avalonia 12 draws the decorations itself now, and
        // decides which parts to draw from the platform: extend the client area and it puts a
        // drawn title bar straight over the masthead. HasTitleBar is read-only, so the only
        // way to be rid of it without writing a whole decorations theme is to ask for no
        // decorations at all and mean it.
        WindowDecorations = WindowDecorations.None;
        _ownEdges = true;
    }

    /// <summary>
    /// Moves the window, for a masthead that has been pressed.
    ///
    /// Called from the view rather than wired up here, because which part of the window is the
    /// handle is a question about the layout, and the layout is in the XAML. Buttons inside the
    /// handle mark the event handled, so they never drag.
    /// </summary>
    protected void DragWindow(PointerPressedEventArgs e)
    {
        if (e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        BeginMoveDrag(e);
    }

    /// <summary>
    /// What the caption's close mark does.
    ///
    /// Virtual because <see cref="Window.Close()"/> is not the same act as clicking the X used
    /// to be: a programmatic close reports itself as programmatic, and the main window's
    /// "keep running in the tray" check deliberately ignores those so that Quit really quits.
    /// Left alone, drawing our own X would have turned closing to the tray into closing the
    /// application — the one regression this whole change could plausibly have caused.
    /// </summary>
    protected virtual void CloseFromCaption() => Close();

    /// <summary>
    /// The resize cursor, for a pointer near an edge.
    ///
    /// Only while the window is a window. The caption offers no maximise, but Win+Up and the
    /// desktop's own snapping still reach it, and a maximised window has no edges to pull —
    /// offering the cursor anyway is a promise that does nothing when taken up.
    /// </summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_ownEdges) return;

        var edge = EdgeAt(e.GetPosition(this));

        Cursor = edge is null ? Cursor.Default : CursorFor(edge.Value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!_ownEdges || e.Handled) return;

        if (EdgeAt(e.GetPosition(this)) is { } edge) BeginResizeDrag(edge, e);
    }

    // The caption, wired from the XAML: the masthead is the handle and the marks sit on its
    // first line, so what is a title bar and what is content is a layout question.
    protected void OnCaptionPressed(object? sender, PointerPressedEventArgs e) => DragWindow(e);

    protected void OnMinimise(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    protected void OnClose(object? sender, RoutedEventArgs e) => CloseFromCaption();

    /// <summary>Which edge, if any, a point is on. Corners win over sides.</summary>
    private WindowEdge? EdgeAt(Point point)
    {
        if (!CanResize || WindowState != WindowState.Normal) return null;

        var left = point.X <= Grip;
        var right = point.X >= Bounds.Width - Grip;
        var top = point.Y <= Grip;
        var bottom = point.Y >= Bounds.Height - Grip;

        return (left, right, top, bottom) switch
        {
            (true, _, true, _) => WindowEdge.NorthWest,
            (_, true, true, _) => WindowEdge.NorthEast,
            (true, _, _, true) => WindowEdge.SouthWest,
            (_, true, _, true) => WindowEdge.SouthEast,
            (true, _, _, _) => WindowEdge.West,
            (_, true, _, _) => WindowEdge.East,
            (_, _, true, _) => WindowEdge.North,
            (_, _, _, true) => WindowEdge.South,
            _ => null
        };
    }

    /// <summary>Cached, because this is asked on every pointer move.</summary>
    private Cursor CursorFor(WindowEdge edge)
    {
        var type = edge switch
        {
            WindowEdge.North or WindowEdge.South => StandardCursorType.SizeNorthSouth,
            WindowEdge.West or WindowEdge.East => StandardCursorType.SizeWestEast,
            WindowEdge.NorthWest or WindowEdge.SouthEast => StandardCursorType.TopLeftCorner,
            _ => StandardCursorType.TopRightCorner
        };

        if (!_cursors.TryGetValue(type, out var cursor))
            _cursors[type] = cursor = new Cursor(type);

        return cursor;
    }
}
