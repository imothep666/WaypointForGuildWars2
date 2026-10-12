using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

using GuildWarsCompanion.Host;

namespace GuildWarsCompanion.Ava.Shell;

/// <summary>
/// The desktop's window around the shell, with its own title bar.
///
/// No system decorations at all, as in the PC Bridge's AppWindow, and for the same reason:
/// Avalonia 12 draws a title bar of its own over an extended client area. So the edges are hit
/// tested here, and the bar is the handle.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>How close to an edge counts as grabbing it: enough for a mouse, not so much
    /// that it swallows clicks meant for a control against the edge.</summary>
    private const double Grip = 6;

    private static readonly Geometry MaximiseGlyph = Geometry.Parse("M 0.5,0.5 H 9.5 V 9.5 H 0.5 Z");
    private static readonly Geometry RestoreGlyph = Geometry.Parse("M 2.5,2.5 V 0.5 H 9.5 V 7.5 H 7.5 M 0.5,2.5 H 7.5 V 9.5 H 0.5 Z");

    private readonly Dictionary<StandardCursorType, Cursor> _cursors = [];

    public MainWindow()
    {
        InitializeComponent();

        // The shell lays itself out by its own width, so the name goes by the same measure.
        Body.SizeChanged += (_, e) => AppName.IsVisible = e.NewSize.Width < MainView.RailWidth;

        // Fullscreen has no title bar to leave it by. Tunnelling, so a focused text box cannot
        // keep the key from the window.
        AddHandler(KeyDownEvent, OnFullScreenKey, RoutingStrategies.Tunnel);
    }

    private IAppUpdates? _updates;

    /// <summary>The updater, whose readiness the bar shows as a button.</summary>
    public IAppUpdates? Updates
    {
        get => _updates;
        set
        {
            if (_updates is not null) _updates.PropertyChanged -= OnUpdatesChanged;
            _updates = value;
            if (_updates is not null) _updates.PropertyChanged += OnUpdatesChanged;
            UpdateButton.IsVisible = _updates?.IsReady == true;
        }
    }

    /// <summary>The shell, under the title bar.</summary>
    public Control? Page
    {
        get => Body.Content as Control;
        set => Body.Content = value;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != WindowStateProperty) return;

        var state = WindowState;

        Frame.BorderThickness = new Thickness(state == WindowState.Normal ? 1 : 0);

        // The bar stays in fullscreen: without it, leaving or closing takes a keyboard. There
        // the middle mark leaves fullscreen, the way restore leaves maximised.
        var label = state switch
        {
            WindowState.Maximized => L.T("Restore"),
            WindowState.FullScreen => L.T("Leave fullscreen"),
            _ => L.T("Maximise")
        };
        MaximiseMark.Data = state is WindowState.Maximized or WindowState.FullScreen ? RestoreGlyph : MaximiseGlyph;
        ToolTip.SetTip(MaximiseButton, label);
        AutomationProperties.SetName(MaximiseButton, label);
    }

    private void OnFullScreenKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F11) return;

        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        e.Handled = true;
    }

    // --- the bar ---------------------------------------------------------------

    /// <summary>Drags the window; a double click maximises or restores it, as a system bar would.
    /// A fullscreen window does not move. The caption buttons mark their presses handled, so
    /// they never drag.</summary>
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
            ToggleMaximised();
        else if (WindowState != WindowState.FullScreen)
            BeginMoveDrag(e);
    }

    private void OnMinimise(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximise(object? sender, RoutedEventArgs e) => ToggleMaximised();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnRestartToUpdate(object? sender, RoutedEventArgs e) => _updates?.RestartToUpdate();

    private void OnUpdatesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IAppUpdates.IsReady)) UpdateButton.IsVisible = _updates?.IsReady == true;
    }

    /// <summary>Maximises a window, and returns a maximised or fullscreen one to a window.</summary>
    private void ToggleMaximised() =>
        WindowState = WindowState is WindowState.Maximized or WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.Maximized;

    // --- the edges -------------------------------------------------------------

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var edge = EdgeAt(e.GetPosition(this));

        Cursor = edge is null ? Cursor.Default : CursorFor(edge.Value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.Handled) return;

        if (EdgeAt(e.GetPosition(this)) is { } edge) BeginResizeDrag(edge, e);
    }

    /// <summary>Which edge, if any, a point is on. Corners win over sides. Only while the window
    /// is a window: a maximised or fullscreen one has no edges to pull.</summary>
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
