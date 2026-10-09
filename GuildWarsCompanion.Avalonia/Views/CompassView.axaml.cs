using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using GuildWarsCompanion.Ava.Host;
using GuildWarsCompanion.Ava.Shell;
using GuildWarsCompanion.Drawing;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace GuildWarsCompanion.Ava.Views;

/// <summary>
/// The compass screen.
///
/// The window stays portrait and the screen adapts to how the phone is held instead. The dial and
/// its marks are drawn by <see cref="CompassRose"/> and turned inside the drawing. The controls sit
/// in a rail along the bottom of the screen — the handset's USB edge, a fixed place in the room —
/// and only what is inside the rail turns. Avalonia's LayoutTransformControl measures a turned
/// view by its turned shape, so the turned controls need no hand-sized wrappers.
/// </summary>
public partial class CompassView : PageView
{
    /// <summary>
    /// Thirty-three milliseconds: thirty frames a second.
    ///
    /// Sixty was the first answer and it was the wrong trade. The easing is frame-rate
    /// independent — <see cref="CompassViewModel.Advance"/> takes the elapsed time rather than
    /// assuming it — so halving the rate changes how often the dial is redrawn and not how fast
    /// it catches up. What it does halve is the redraw's cost, which on this screen is paid in
    /// memory rather than in milliseconds: every frame stamps an image per mark, and that was
    /// measured at about 9 MB/min the process never gets back.
    ///
    /// Thirty is still well above the rate the bridge sends at — ten a second — so the motion
    /// between reported positions is still drawn rather than stepped, which was the whole point
    /// of easing at display rate. See <see cref="CompassViewModel"/>'s MostBlips for the other
    /// half of the same sum.
    /// </summary>
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);

    /// <summary>
    /// A button's own size before it is turned. Long enough for "CHOOSE AN OBJECTIVE", and short
    /// enough that two of them, with their margins, fit the rail of a 411-wide phone: 200 left the
    /// right-hand column cut off.
    /// </summary>
    private const double ButtonLength = 186;

    private const double ButtonThickness = 44;

    /// <summary>The readout block's own size before it is turned.</summary>
    private const double ReadoutWidth = 360;

    /// <summary>
    /// Six lines at 15, 11, 14, 18, 11 and 11 points come to about 105 with their line height. It
    /// was 96, which cut the last line — the route — in half whenever there was one; seen on the
    /// phone with the trail's port advice, the longest line it carries.
    /// </summary>
    private const double ReadoutHeight = 108;

    private readonly CompassRose _rose = new();

    /// <summary>
    /// Which way up the phone is, so the dial can be turned back. Owned by the page: it is a sensor
    /// subscription, and should live exactly as long as the one screen that looks at it.
    /// </summary>
    private readonly DeviceTilt _tilt = new(App.Services.GetRequiredService<IAccelerometer>());

    /// <summary>Which quarter turn the controls are pinned to. Labels snap to right angles while
    /// the dial keeps the continuous angle: a label at twenty-three degrees is harder to read.</summary>
    private readonly SnappedOrientation _snap = new();

    private readonly IKeepAwake _awake = App.Services.GetRequiredService<IKeepAwake>();

    private TextBlock? _character;
    private TextBlock? _map;
    private TextBlock? _objective;
    private TextBlock? _instruction;
    private TextBlock? _distance;

    /// <summary>The port-then-walk advice, under the range it applies to.</summary>
    private TextBlock? _route;

    private DispatcherTimer? _frames;
    private DateTime _lastTick = DateTime.UtcNow;
    private int _placed = -1;
    private double _tiltNow;

    public CompassView() => InitializeComponent();

    private CompassViewModel ViewModel => (CompassViewModel)DataContext!;

    /// <summary>The band above the dial shows the loading screen of the map the character is on —
    /// the band only: behind the dial a painting would cost the contrast its marks live on.</summary>
    private void OnModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CompassViewModel.SceneMap)) ShowMapScene(ViewModel.SceneMap);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is not CompassViewModel viewModel) return;

        _rose.Symbol = viewModel.Symbol;
        Rose.Drawable = _rose;
    }

    public override void OnAppearing()
    {
        // Paired with OnDisappearing rather than taken once: the view model is a singleton, and
        // Moved is only raised between Start and Stop, which is exactly this span.
        ViewModel.Moved += OnMoved;

        ViewModel.PropertyChanged += OnModelPropertyChanged;
        ShowMapScene(ViewModel.SceneMap);

        // Read rather than operated, and propped up while both hands are on the game: the screen
        // must not sleep under it. A window flag, released the moment the page goes.
        _awake.KeepScreenOn = true;

        _tilt.Start();

        PlaceRail();

        _lastTick = DateTime.UtcNow;

        _frames = new DispatcherTimer { Interval = FrameInterval };
        _frames.Tick += OnFrame;
        _frames.Start();

        // Only while the screen is on top: the stream holds a socket open and drives the PC's
        // MumbleLink poll at ten a second.
        ViewModel.Start();
    }

    public override void OnDisappearing()
    {
        ViewModel.PropertyChanged -= OnModelPropertyChanged;

        if (_frames is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnFrame;
            _frames = null;
        }

        _tilt.Stop();

        _awake.KeepScreenOn = false;

        ViewModel.Stop();

        ViewModel.Moved -= OnMoved;
    }

    /// <summary>
    /// Back closes the sheet, a layer at a time. The picker is a sheet rather than a pushed page
    /// on purpose: leaving the page would stop the bridge stream and dim the dial.
    /// </summary>
    public override bool HandleBack()
    {
        if (ViewModel.IsFilterOpen)
        {
            ViewModel.ToggleFilterCommand.Execute(null);
            return true;
        }

        if (ViewModel.IsPickerOpen)
        {
            ViewModel.TogglePickerCommand.Execute(null);
            return true;
        }

        return false;
    }

    /// <summary>One display frame: advance the easing by the time that really passed, then repaint.</summary>
    private void OnFrame(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastTick).TotalSeconds;

        _lastTick = now;

        // Nothing to animate behind the sheet.
        if (ViewModel.IsPickerOpen) return;

        _tiltNow = _tilt.Radians;

        if (_snap.Update(_tiltNow)) PlaceRail();

        ViewModel.Advance(elapsed);
    }

    /// <summary>
    /// Rebuilds the rail for the way the phone is being held.
    ///
    /// The rail does not move; only what sits in it changes. The labels turn so they stay readable,
    /// and the buttons are arranged so they never run past the edge: two by two while they are
    /// wide, four abreast once they have been turned on their side and become narrow.
    /// </summary>
    private void PlaceRail()
    {
        if (_placed == _snap.Quarter && RailSlot.Content is not null) return;

        _placed = _snap.Quarter;

        var sideways = _snap.Quarter % 2 == 1;

        // Counter the phone, so the words read upright whatever the handset is doing.
        var rotation = -_snap.Quarter * 90;

        var buttons = new List<Control>
        {
            Rail("CHOOSE AN OBJECTIVE", ViewModel.TogglePickerCommand, rotation),
            Rail("CLEAR OBJECTIVE", ViewModel.ClearObjectiveCommand, rotation),
            Rail("RECHECK PROGRESS", ViewModel.RefreshProgressCommand, rotation),
            Rail("CLEAR TRAIL", ViewModel.ClearTrailCommand, rotation)
        };

        // Turned past halfway, the natural reading order runs the other way along the rail.
        if (_snap.Quarter is 2 or 3) buttons.Reverse();

        var readout = Turned(Readout(), ReadoutWidth, ReadoutHeight, rotation);

        var columns = sideways ? buttons.Count : 2;
        var rows = (buttons.Count + columns - 1) / columns;

        var grid = new Grid { VerticalAlignment = VerticalAlignment.Bottom };

        if (sideways)
        {
            // One row: the readout and every button abreast, on the side the reading starts from.
            for (var i = 0; i <= columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var first = _snap.Quarter == 1;

            Place(grid, readout, 0, first ? 0 : columns);

            for (var i = 0; i < buttons.Count; i++) Place(grid, buttons[i], 0, first ? i + 1 : i);
        }
        else
        {
            // The readout across the rail, the buttons in a block on the dial's side of it.
            for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            for (var i = 0; i <= rows; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var upsideDown = _snap.Quarter == 2;

            Place(grid, readout, upsideDown ? rows : 0, 0);
            Grid.SetColumnSpan(readout, columns);

            for (var i = 0; i < buttons.Count; i++)
                Place(grid, buttons[i], i / columns + (upsideDown ? 0 : 1), i % columns);
        }

        RailSlot.Content = grid;
    }

    private static void Place(Grid grid, Control child, int row, int column)
    {
        grid.Children.Add(child);
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
    }

    /// <summary>
    /// The character, where they are, and what they are walking towards — in the rail rather than
    /// under the dial, so it is still on screen once the phone is turned. Rebuilt with the rail.
    /// </summary>
    private Control Readout()
    {
        TextBlock Line(double size, string brush, string? font = null) => new()
        {
            FontSize = size,
            Foreground = Resource<IBrush>(brush),
            FontFamily = Resource<FontFamily>(font ?? "Barlow"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };

        _character = Line(15, "MistBrush");
        _map = Line(11, "BrassDimBrush");
        _objective = Line(14, "MistBrush");
        _instruction = Line(18, "BrassBrush", "BarlowSemiBold");
        _distance = Line(11, "MistBrush");
        _route = Line(11, "BrassBrush");

        Refresh();

        return new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _character, _map, _objective, _instruction, _distance, _route }
        };
    }

    /// <summary>Any view at its natural size, turned, and laid out by its turned shape.</summary>
    private static Control Turned(Control content, double width, double height, double rotation)
    {
        content.Width = width;
        content.Height = height;

        return new LayoutTransformControl
        {
            LayoutTransform = new RotateTransform(rotation),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(4),
            Child = content
        };
    }

    private static Control Rail(string text, ICommand command, double rotation) =>
        Turned(new Button
        {
            Content = text,
            Command = command,
            Classes = { "quiet" },
            FontSize = 11,
            Padding = new Thickness(8, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        }, ButtonLength, ButtonThickness, rotation);

    private T Resource<T>(string key) where T : class =>
        (this.FindResource(key) ?? Application.Current!.FindResource(key)) as T
        ?? throw new InvalidOperationException($"No resource {key}.");

    /// <summary>
    /// One frame. Copies the values the drawable needs and asks for a repaint — copied rather than
    /// bound, because the canvas does not observe its drawable.
    /// </summary>
    private void OnMoved()
    {
        _rose.Blips = ViewModel.Blips;
        _rose.BlipCount = ViewModel.BlipCount;
        _rose.Trail = ViewModel.Trail;
        _rose.TrailCount = ViewModel.TrailCount;
        _rose.Guide = ViewModel.Guide;
        _rose.GuideCount = ViewModel.GuideCount;
        _rose.Facing = ViewModel.Facing;
        _rose.IsLive = ViewModel.IsLive;
        _rose.Tilt = _tiltNow;

        Refresh();

        Rose.Invalidate();
    }

    /// <summary>
    /// Pushes the current words into the rail's labels, only on a change: this runs thirty times a
    /// second, and assigning text unconditionally re-measures six labels every frame.
    /// </summary>
    private void Refresh()
    {
        Set(_character, ViewModel.CharacterLine);
        Set(_map, ViewModel.MapLine);
        Set(_objective, ViewModel.TargetLine);
        Set(_instruction, ViewModel.Instruction);
        Set(_distance, ViewModel.DistanceLine);
        Set(_route, ViewModel.RouteLine);

        static void Set(TextBlock? label, string text)
        {
            if (label is not null && label.Text != text) label.Text = text;
        }
    }

    private void OnTargetClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TargetRow row) ViewModel.PickCommand.Execute(row);
    }

    /// <summary>Leaves the sheet the way back does, so the two cannot disagree about what closes.</summary>
    private void OnSheetBack(object? sender, RoutedEventArgs e) => HandleBack();

    private void OnSheetTabSelected(PageTab tab) => ViewModel.Select(tab);

    /// <summary>Takes one target off the dial, or puts it back. Serves both lists in the sheet.</summary>
    private void OnDismissClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TargetRow row) ViewModel.ToggleDismissedCommand.Execute(row);
    }

    private void OnKindClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is KindRow kind) ViewModel.ToggleKindCommand.Execute(kind);
    }
}
