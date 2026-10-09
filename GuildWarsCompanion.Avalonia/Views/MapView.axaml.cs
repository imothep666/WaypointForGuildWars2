using System.Diagnostics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Interactivity;
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
/// The map screen.
///
/// Thin on purpose: the drawing is <see cref="MapCanvas"/> and the state is
/// <see cref="MapViewModel"/>. What lives here is turning pointer input into a pan, a pinch or a
/// tap, and knowing how many device pixels a unit is worth — Avalonia's render scaling.
/// </summary>
public partial class MapView : PageView
{
    /// <summary>How far a press may wander and still be a tap rather than the start of a pan.</summary>
    private const double TapSlop = 8;

    /// <summary>
    /// How often the tilt is read: five times a second. The only thing it can produce is a quarter
    /// turn, which happens when somebody physically picks the phone up.
    /// </summary>
    private static readonly TimeSpan TiltInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Thirty frames a second while the map moves, as the compass draws; see its FrameInterval for
    /// why not sixty. Only while it moves: every frame redraws every tile.
    /// </summary>
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);

    private readonly MapCanvas _canvas = new();

    /// <summary>The frame loop, alive only while a zoom eases or the view glides.</summary>
    private DispatcherTimer? _frames;

    /// <summary>Time since the last frame, because a timer's ticks are not evenly spaced.</summary>
    private readonly Stopwatch _clock = new();

    /// <summary>Which way up the phone is, so the map can be turned back. Owned by the page.</summary>
    private readonly DeviceTilt _tilt = new(App.Services.GetRequiredService<IAccelerometer>());

    /// <summary>
    /// The quarter the map is pinned to: snapped rather than eased, because a map is read while it
    /// is still, and a map at twenty-three degrees is a map whose labels cannot be read.
    /// </summary>
    private readonly SnappedOrientation _snap = new();

    private readonly IKeepAwake _awake = App.Services.GetRequiredService<IKeepAwake>();

    private DispatcherTimer? _tilts;

    /// <summary>The fingers on the canvas. A pan is one of them; two are a pinch, and do not pan.</summary>
    private readonly Dictionary<int, Point> _pointers = [];

    private Point _pressedAt;
    private Point _lastAt;
    private bool _panning;

    /// <summary>
    /// How much of a pinch has been gathered but not yet spent. The gesture is continuous and the
    /// zoom is not, so the scale is accumulated until it has doubled or halved and then a level is
    /// taken out of it. Multiplied rather than added: zoom levels are a doubling each.
    /// </summary>
    private double _pinch = 1;

    /// <summary>The pinch's own running scale, which Avalonia reports since the fingers went down
    /// rather than since the last report, as MAUI does.</summary>
    private double _pinchSeen = 1;

    private Point _pinchAt;
    private bool _pinching;

    public MapView()
    {
        InitializeComponent();

        _canvas.Model = null;

        Surface.Drawable = _canvas;
        Surface.GestureRecognizers.Add(new PinchGestureRecognizer());

        Surface.PointerPressed += OnPressed;
        Surface.PointerMoved += OnMoved;
        Surface.PointerReleased += OnReleased;
        Surface.PointerCaptureLost += OnCaptureLost;
        Surface.PointerWheelChanged += OnWheel;
        Surface.AddHandler(InputElement.PinchEvent, OnPinch);
        Surface.AddHandler(InputElement.PinchEndedEvent, OnPinchEnded);
        Surface.SizeChanged += (_, _) => Refit();
    }

    private MapViewModel ViewModel => (MapViewModel)DataContext!;

    private void OnModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapViewModel.ShownMap)) ShowMapScene(ViewModel.ShownMap);
    }

    private float Density => (float)(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MapViewModel viewModel) _canvas.Model = viewModel;
    }

    public override void OnAppearing()
    {
        _canvas.Density = Density;
        ViewModel.PointDensity = Density;

        // Paired with OnDisappearing: the view model is a singleton and outlives this page.
        ViewModel.Changed += OnChanged;

        // The band above the map shows the map's loading screen, picked or stood in.
        ViewModel.PropertyChanged += OnModelPropertyChanged;
        ShowMapScene(ViewModel.ShownMap);

        // Propped up while both hands are on the game, exactly like the compass.
        _awake.KeepScreenOn = true;

        _tilt.Start();

        _tilts = new DispatcherTimer { Interval = TiltInterval };
        _tilts.Tick += OnTilt;
        _tilts.Start();

        ViewModel.Start();

        Refit();
    }

    public override void OnDisappearing()
    {
        ViewModel.PropertyChanged -= OnModelPropertyChanged;

        ViewModel.Changed -= OnChanged;

        StopFrames();

        if (_tilts is not null)
        {
            _tilts.Tick -= OnTilt;
            _tilts.Stop();
            _tilts = null;
        }

        _tilt.Stop();

        _awake.KeepScreenOn = false;

        // Stops the stream and lets go of every tile: forty decoded squares is about fifteen
        // megabytes, and nothing is looking at them once this screen is not on top.
        ViewModel.Stop();
    }

    /// <summary>
    /// Fits the map to the viewport as the map sees it — the screen with its sides swapped once
    /// the phone is sideways. Needs the canvas measured, which is why it also runs on resize.
    /// </summary>
    private void Refit()
    {
        if (DataContext is not MapViewModel viewModel || Surface.Bounds.Width <= 0) return;

        _canvas.Density = Density;
        ViewModel.PointDensity = Density;

        var (width, height) = viewModel.Extent(Surface.Bounds.Width * Density, Surface.Bounds.Height * Density);

        viewModel.Fit(width, height);
    }

    /// <summary>
    /// Follows the phone, a quarter at a time, re-fitting on the way: a portrait map and a
    /// landscape one hold different amounts of the world.
    /// </summary>
    private void OnTilt(object? sender, EventArgs e)
    {
        if (!_snap.Update(_tilt.Radians)) return;

        if (!ViewModel.Turn(_snap.Quarter)) return;

        Refit();

        Surface.Invalidate();
    }

    /// <summary>
    /// Draws again, and starts the frame loop if what changed set something moving: a zoom, or the
    /// character leaving the middle of the screen. Raised from tile loads on pool threads too, so
    /// the loop is started on the UI thread.
    /// </summary>
    private void OnChanged()
    {
        Surface.Invalidate();

        if (Dispatcher.UIThread.CheckAccess()) StartFrames();
        else Dispatcher.UIThread.Post(StartFrames);
    }

    private void StartFrames()
    {
        if (_frames is not null || DataContext is not MapViewModel { IsMoving: true }) return;

        _clock.Restart();

        _frames = new DispatcherTimer { Interval = FrameInterval };
        _frames.Tick += OnFrame;
        _frames.Start();
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var seconds = _clock.Elapsed.TotalSeconds;

        _clock.Restart();

        ViewModel.Advance(seconds);

        Surface.Invalidate();

        // Settled: nothing is drawn again until something changes.
        if (!ViewModel.IsMoving) StopFrames();
    }

    private void StopFrames()
    {
        if (_frames is null) return;

        _frames.Tick -= OnFrame;
        _frames.Stop();
        _frames = null;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var at = e.GetPosition(Surface);

        _pointers[e.Pointer.Id] = at;

        if (_pointers.Count != 1) return;

        e.Pointer.Capture(Surface);

        _pressedAt = at;
        _lastAt = at;
        _panning = false;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!_pointers.ContainsKey(e.Pointer.Id)) return;

        var at = e.GetPosition(Surface);

        _pointers[e.Pointer.Id] = at;

        // Two fingers are a pinch; neither of them pans.
        if (_pointers.Count != 1 || _pinching) return;

        if (!_panning && Distance(at, _pressedAt) < TapSlop) return;

        _panning = true;

        // Into tile pixels, the space the view model pans in.
        ViewModel.Pan((at.X - _lastAt.X) * Density, (at.Y - _lastAt.Y) * Density);

        _lastAt = at;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var wasOnlyOne = _pointers.Count == 1;

        _pointers.Remove(e.Pointer.Id);

        if (!wasOnlyOne || _panning || _pinching) return;

        // A press that never travelled: a tap.
        var at = e.GetPosition(Surface);

        ViewModel.TapAt(at.X * Density, at.Y * Density, Surface.Bounds.Width * Density, Surface.Bounds.Height * Density,
            Density);
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _pointers.Remove(e.Pointer.Id);
        _panning = false;
    }

    /// <summary>A desktop's mouse wheel, as one zoom level per notch, about the pointer.</summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        _pinchAt = e.GetPosition(Surface);

        if (e.Delta.Y > 0) Step(1);
        else if (e.Delta.Y < 0) Step(-1);

        e.Handled = true;
    }

    /// <summary>A pinch, turned into whole zoom levels.</summary>
    private void OnPinch(object? sender, PinchEventArgs e)
    {
        if (!_pinching)
        {
            _pinching = true;
            _pinch = 1;
            _pinchSeen = 1;
            _pinchAt = e.ScaleOrigin;
        }

        if (e.Scale <= 0) return;

        _pinch *= e.Scale / _pinchSeen;
        _pinchSeen = e.Scale;

        // A loop rather than an if: a fast pinch can cross two levels between two reports.
        while (_pinch >= 2)
        {
            Step(1);
            _pinch /= 2;
        }

        while (_pinch <= 0.5)
        {
            Step(-1);
            _pinch *= 2;
        }
    }

    /// <summary>
    /// The end of a pinch — and, on Android, of every single touch: the recognizer raises this
    /// when a lone finger lifts too, before the release reaches this page. Acting on that one
    /// marked every tap as the tail of a pinch, and the zoom buttons stopped answering.
    /// </summary>
    private void OnPinchEnded(object? sender, PinchEndedEventArgs e)
    {
        if (!_pinching) return;

        _pinching = false;
        _panning = true; // The fingers still down are what is left of a pinch, not a new pan.
    }

    private void Step(int levels) =>
        ViewModel.ZoomBy(levels, _pinchAt.X * Density, _pinchAt.Y * Density,
            Surface.Bounds.Width * Density, Surface.Bounds.Height * Density);

    private void OnRouteClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is RouteChoice choice) ViewModel.PickRouteCommand.Execute(choice);
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
