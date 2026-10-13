using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Data;
using GuildWarsCompanion.Drawing;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// The game's own map, on the phone, with the character on it.
///
/// The compass answers "which way, and how far" in half a second. This answers the other
/// question — "what is around here, and where am I in it" — which a dial cannot, because a dial
/// has no shape. Both point at the same objective: see <see cref="Objective"/>.
///
/// <b>Why this is not part of the compass screen.</b> The dial is an instrument on a held stream
/// that repaints at display rate. A map is panned, looked at, and left alone. Two lifetimes and
/// two very different memory profiles on one page would put a tile cache next to the redraw loop
/// that has already killed this app once — see <see cref="Gw2Tiles"/>.
/// </summary>
public sealed partial class MapViewModel(
    GameStream stream, CompassTargets targets, Gw2Client api, Gw2Cache cache, Gw2Tiles tiles,
    MapIcons icons, Objective objective, Journal journal, MarkerStore markers, TrailShare trails,
    IRouteChoiceStore choices, MapCatalogue catalogue,
    INavigator navigator, IMainThread mainThread) : BaseViewModel, IMapScene
{
    /// <summary>
    /// How close a tap has to land to count as picking a marker, in the same tile pixels
    /// everything else here is measured in — which on a phone are device pixels.
    ///
    /// Sized against what is actually drawn rather than against a feeling. A marker is 22 MAUI
    /// units across, and this display has about three device pixels to the unit, so the symbol a
    /// player is aiming at is roughly sixty pixels wide. The first value here was thirty — half
    /// the symbol — and tapping a heart squarely did nothing at all.
    ///
    /// The nearest marker inside the radius wins, which is a far better answer than "nothing was
    /// hit" for a tap that was obviously aimed at something.
    /// </summary>
    private const double TapRadius = 60;

    private CancellationTokenSource? _watching;
    private int _loadedMap = -1;

    /// <summary>
    /// A map that could not be opened, and when. A failed load puts the old map back, so the next
    /// frame asked for the failed one again: several times a second, each failure flashing its
    /// sentence into the panel for a moment and sending another request. It waits now.
    /// </summary>
    private (int Map, DateTimeOffset At)? _failed;

    private static readonly TimeSpan RetryFailedMap = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The map somebody asked for by name, or null when the screen follows the character.
    ///
    /// This is the whole difference between a screen that needs a PC and one that does not. With
    /// a pin the bridge is still listened to — the dot appears the moment the character walks
    /// into the map being looked at — but it is no longer allowed to change which map is drawn.
    /// Walking through a border while reading Frostgorge on a train should not drag the screen
    /// somewhere else, and neither should logging in.
    /// </summary>
    private int? _pinned;

    /// <summary>The pinned map's name, so the line at the bottom can say where this is without
    /// waiting for the map itself to load.</summary>
    private string _pinnedName = string.Empty;

    /// <summary>
    /// The pin the map on screen was opened under, null when it was opened by following.
    ///
    /// What a failed load puts back. It used to put back <see cref="_pinned"/> as the load found
    /// it, but <see cref="Pin"/> sets that before the load starts, so a map that could not be
    /// opened was "put back" as itself: its name on the line, over the last map's tiles.
    /// </summary>
    private int? _drawnPin;

    private string _drawnPinName = string.Empty;

    /// <summary>
    /// Whether the map walks with the character.
    ///
    /// On until the map is dragged, because a screen propped against the monitor while somebody
    /// plays should keep showing where they are without being touched. A pan is a deliberate
    /// statement that this is the part of the map being looked at, so it stops — a map that
    /// snapped back to the character two seconds after every drag would be unusable. RECENTRE
    /// takes it back.
    /// </summary>
    private bool _following = true;

    /// <summary>Where the middle of the viewport is, in continent coordinates.</summary>
    public double CentreX { get; private set; }

    public double CentreY { get; private set; }

    public int Zoom { get; private set; } = 6;

    /// <summary>The zoom eased between levels, and the glide after the character; see
    /// <see cref="MapMotion"/>. The page draws frames only while it says something is moving.</summary>
    private readonly MapMotion _motion = new();

    /// <summary>How much bigger than its level's own pixels the map is drawn: one at rest, between
    /// a half and two while a zoom eases. Every conversion to the screen below applies it.</summary>
    public double Scale => _motion.Scale;

    /// <summary>Whether a zoom or a glide is under way, so the page keeps asking for frames.</summary>
    public bool IsMoving => _motion.IsMoving;

    /// <summary>Device pixels per screen point, set by the page. How far the character's dot must
    /// move before a report is worth a frame; see MapRedraw.</summary>
    public double PointDensity { get; set; } = 1;

    /// <summary>
    /// Where the character's dot was when a report last asked for a frame. Compared against rather
    /// than the previous report: a character walking less than a step per report would otherwise
    /// never be redrawn, and the dot would stand still while they walked away from it.
    /// </summary>
    private (double X, double Y)? _shownPlayer;

    /// <summary>
    /// The level that was on screen before the last zoom, drawn scaled under any tile of the new
    /// level that has not arrived yet. A blurred square for a moment is a far better thing to look
    /// at than a hole, and it sharpens as the tile lands.
    /// </summary>
    public int? Underlay { get; private set; }

    /// <summary>
    /// One frame's worth of motion: the time since the last frame, from the page's frame loop.
    /// Silent; the page redraws after calling this.
    /// </summary>
    public void Advance(double seconds)
    {
        if (!_motion.IsMoving) return;

        (CentreX, CentreY) =
            _motion.Advance(seconds, CentreX, CentreY, TileGeometry.UnitsPerPixel(Zoom, Reference));

        Clamp();
    }

    /// <summary>Brings a zoom under way to its end at once, for a gesture that cannot wait for it.
    /// Stops a glide where it is.</summary>
    private void Settle()
    {
        if (_motion.IsZooming)
            (CentreX, CentreY) = _motion.Advance(
                MapMotion.ZoomSeconds, CentreX, CentreY, TileGeometry.UnitsPerPixel(Zoom, Reference));

        _motion.Stop();
    }

    /// <summary>
    /// Quarter turns clockwise from upright, 0 to 3, so the map can sit the right way up in a
    /// window that never turns.
    ///
    /// Snapped rather than continuous, unlike the dial. A compass needle has to ease because it
    /// is read while it moves; a map is read while it is still, and a map at twenty-three degrees
    /// is just a map with unreadable labels. Quarters also keep the tile grid axis-aligned, so
    /// nothing is resampled.
    /// </summary>
    public int Quarter { get; private set; }

    /// <summary>
    /// The zoom at which continent coordinates are pixels.
    ///
    /// One below the <c>max_zoom</c> the API publishes, which is not a guess — see
    /// <see cref="TileGeometry"/> for the six tiles that established it. Reading max_zoom as the
    /// reference draws a whole screen of open sea with every marker in the right place on top
    /// of it.
    /// </summary>
    public int Reference { get; private set; } = 7;

    public int ContinentId { get; private set; } = 1;

    public int Floor { get; private set; } = 1;

    /// <summary>
    /// The zoom as a step, counting from one at the whole map.
    ///
    /// The pad shows this rather than the service's own zoom number. Seven means nothing to
    /// anybody; "3 of 4" says how far in you are and how much further you can go.
    /// </summary>
    public int Level => Zoom - _fitZoom + 1;

    /// <summary>How many steps there are between the whole map and the deepest rendered
    /// level.</summary>
    public int Levels => Reference - _fitZoom + 1;

    public bool CanZoomIn => Zoom < Reference;

    public bool CanZoomOut => Zoom > _fitZoom;

    /// <summary>The tile grid's size at the current zoom, so a pan cannot ask for a tile that
    /// was never rendered.</summary>
    public int GridWidth { get; private set; } = 1;

    public int GridHeight { get; private set; } = 1;

    /// <summary>Everything on the loaded map, unfiltered. The map is a map; the dial is the
    /// screen that hides things.</summary>
    public IReadOnlyList<CompassTarget> Marks { get; private set; } = [];

    /// <summary>
    /// The routes being drawn: what a pack carries for this map, minus the categories turned off.
    ///
    /// Unlike the markers, these are filtered. A marker is one symbol and the map shows all of
    /// them; a route covers the whole map, and a pack carrying a dozen for one zone draws a
    /// thicket nobody can read — Teh's Trails has 22 categories and files main trails, highlight
    /// trails and hero point trails separately precisely so they can be chosen between.
    /// </summary>
    public IReadOnlyList<RouteLine> Routes { get; private set; } = [];

    /// <summary>Every route this map has, filtered or not, so the chooser can list what is
    /// available rather than only what is on.</summary>
    public IReadOnlyList<RouteLine> AllRoutes { get; private set; } = [];

    /// <summary>The categories somebody has asked to see, remembered between runs.</summary>
    private IReadOnlySet<string> _chosen = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>What the chooser lists for the map on screen.</summary>
    public IReadOnlyList<RouteChoice> RouteChoices => RoutePicks.Options(AllRoutes, _chosen);

    /// <summary>Whether this map has any route at all, so a screen can leave the control out
    /// rather than offering an empty list.</summary>
    public bool HasRoutes => AllRoutes.Count > 0;

    /// <summary>Whether the list of routes is open. Closed by default: it is a list of a
    /// stranger's category names and the map is what the screen is for.</summary>
    [ObservableProperty] private bool _choosingRoutes;

    /// <summary>Opens or closes the route list.</summary>
    [RelayCommand]
    public void ChooseRoutes() => ChoosingRoutes = !ChoosingRoutes;

    /// <summary>Turns one category on or off, from a tap on its row.</summary>
    [RelayCommand]
    public void PickRoute(RouteChoice? choice)
    {
        if (choice is not null) ToggleRoute(choice.Type);
    }

    /// <summary>
    /// Turns one category of route on or off, and remembers it.
    /// </summary>
    public void ToggleRoute(string type)
    {
        _chosen = RoutePicks.Toggle(_chosen, type);

        choices.Write(_chosen);

        Routes = RoutePicks.Shown(AllRoutes, _chosen);

        OnPropertyChanged(nameof(RouteChoices));

        Changed?.Invoke();
    }

    /// <summary>Where the character is, or null with no live game.</summary>
    public (double X, double Y)? Player { get; private set; }

    /// <summary>The trail that leads to the objective, or null when none goes near it. See
    /// <see cref="TrailGuide"/>.</summary>
    public TrailGuide? Guide { get; private set; }

    /// <summary>Where on <see cref="Guide"/> the drawn stretch starts: the point nearest the
    /// character, or the start of the approach when nobody is on this map.</summary>
    public int GuideFrom { get; private set; }

    /// <summary>How much of the approach is drawn with nobody on the map, in continent units.</summary>
    private const double Approach = 600;

    /// <summary>
    /// Whether <see cref="Guide"/> was chosen knowing where the character is.
    ///
    /// The map loads before the first frame arrives, so the first choice is made blind — the
    /// trail passing closest, which on the Silverwastes was the mounted run at 1927 along while the
    /// compass, choosing from the character's position, followed the walking run at 1309. The two
    /// screens pointing different ways at one objective is worse than either answer, so a blind
    /// choice is made again as soon as there is a position to make it from.
    /// </summary>
    private bool _guideSighted;

    /// <summary>The map on screen, for turning the objective into the game's metres.</summary>
    private Gw2.MapInfo? _map;

    public CompassTarget? Chosen => objective.Chosen;

    /// <summary>The game's own marker art, the same set the dial stamps.</summary>
    public IImage? Symbol(TargetKind kind) => icons.For(kind);

    /// <summary>A tile if it has arrived, null while it has not.</summary>
    public IImage? Tile(int x, int y) => tiles.Held(new TileKey(ContinentId, Floor, Zoom, x, y));

    /// <summary>A tile of another level, only if it is already in hand. Never fetched: the
    /// underlay is whatever is left of the last level, not a second map to download.</summary>
    public IImage? TileAt(int zoom, int x, int y) => tiles.Held(new TileKey(ContinentId, Floor, zoom, x, y));

    /// <summary>Starts the fetch for a tile the drawing wants and does not have.</summary>
    public void WantTile(int x, int y) => tiles.Want(new TileKey(ContinentId, Floor, Zoom, x, y));

    /// <summary>Drops every tile outside a set, which is the only thing keeping this screen's
    /// memory bounded. See <see cref="Gw2Tiles.Keep"/>.</summary>
    public void KeepTiles(IReadOnlySet<TileKey> visible) => tiles.Keep(visible);

    public TileKey Key(int x, int y) => new(ContinentId, Floor, Zoom, x, y);

    /// <summary>Raised when the drawing needs to happen again.</summary>
    public event Action? Changed;

    /// <summary>
    /// Turns the map to a quarter, if it is not already there.
    /// </summary>
    /// <returns>True when it moved, so the page can re-fit rather than doing it every tick.</returns>
    public bool Turn(int quarter)
    {
        if (quarter == Quarter) return false;

        Quarter = quarter;

        return true;
    }

    /// <summary>
    /// The viewport as the map sees it: the screen with its sides swapped on a quarter turn.
    ///
    /// Everything else in this class works in that space, so a sideways map is the same
    /// arithmetic as an upright one with a different rectangle, rather than a second set of cases
    /// running through the drawing, the fitting and the hit test alike.
    /// </summary>
    public (double Width, double Height) Extent(double screenWidth, double screenHeight) =>
        QuarterTurn.Extent(Quarter, screenWidth, screenHeight);

    /// <summary>
    /// A point on the screen, in the map's own space.
    ///
    /// The inverse of what the drawing does. See <see cref="QuarterTurn"/>, which holds the
    /// arithmetic so that the drawing and the hit test cannot disagree about it.
    /// </summary>
    public (double X, double Y) FromScreen(
        double x, double y, double screenWidth, double screenHeight) =>
        QuarterTurn.FromScreen(Quarter, x, y, screenWidth, screenHeight);

    [ObservableProperty] private string _mapLine = L.T("Waiting for the bridge…");

    [ObservableProperty] private string _objectiveLine = L.T("No objective chosen");

    /// <summary>
    /// How far the objective is and which way, in the words a map wants.
    ///
    /// A point of the compass rather than the dial's "bear left": the map is north-up and the
    /// marker is on it, so the useful words are the ones matching what the eye is already doing.
    /// Empty with no objective, or with no live game to measure from.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasObjectiveDetail))]
    private string _objectiveDetail = string.Empty;

    /// <summary>The port-then-walk advice, the same line the dial shows and from the same
    /// arithmetic — see <see cref="PortRoute"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoute))]
    private string _routeLine = string.Empty;

    public bool HasObjectiveDetail => !string.IsNullOrWhiteSpace(ObjectiveDetail);

    public bool HasRoute => !string.IsNullOrWhiteSpace(RouteLine);

    /// <summary>The port-then-walk advice for the chosen objective, worked out when it is chosen
    /// rather than per frame: the waypoints near it do not move.</summary>
    private PortHop? _hop;

    private string _hopLine = string.Empty;

    /// <summary>The same advice while a trail is followed, measured onto the trail and along it;
    /// see <see cref="PortRoute.ToTrail"/>.</summary>
    private TrailHop? _trailHop;

    private string _trailHopLine = string.Empty;

    [ObservableProperty] private bool _isLive;

    /// <summary>True while a map is being shown because somebody picked it.</summary>
    [ObservableProperty] private bool _isPinned;

    /// <summary>The English name of the map on screen, picked or stood in, once it has loaded;
    /// its loading screen goes in the band above it, and the wiki files those under English
    /// names whatever language the phone is in. Null before any map has, or when the English
    /// name cannot be had.</summary>
    [ObservableProperty] private string? _shownMap;

    /// <summary>
    /// Draws a map because somebody asked for it, rather than because a character is standing
    /// in it.
    ///
    /// Called before the screen is navigated to, so the load is already under way by the time
    /// the page appears. Safe with no bridge and safe with no connection: everything it needs is
    /// either on disk or about to be fetched, and <see cref="Start"/> is what listens for a game.
    /// </summary>
    public void Pin(int mapId, string name)
    {
        _pinned = mapId;
        _pinnedName = name;

        IsPinned = true;
        MapLine = name;

        // A pinned map is a place being looked at, not a character being watched. Without this a
        // character who walked into the pinned map would start dragging the view around under
        // somebody reading it.
        _following = false;

        journal.Write("map", $"showing {name} ({mapId}) by name, no character needed");

        Player = null;

        // Only when the screen is already up. The ordinary path is a pin taken from the picker
        // before navigating here, and a load started now would be cancelled by the Stop() that
        // Start() begins with — so in that case Start does it instead.
        if (_watching is not null) _ = LoadAsync(mapId);
    }

    /// <summary>
    /// Lets go of the pin and goes back to following the character.
    ///
    /// The map on screen is left alone until the bridge says otherwise, because the alternative
    /// is a screen that empties itself the moment this is tapped with no game running.
    /// </summary>
    [RelayCommand]
    public void Follow()
    {
        _pinned = null;
        _pinnedName = string.Empty;

        IsPinned = false;
        _following = true;

        if (!IsLive) MapLine = L.T("Waiting for the bridge…");

        Changed?.Invoke();
    }

    /// <summary>Opens the list of every map, by name.</summary>
    [RelayCommand]
    public Task ChooseMapAsync() => navigator.GoToAsync("mappicker");

    /// <summary>Starts listening. Called when the page appears.</summary>
    public void Start()
    {
        Stop();

        objective.Changed += OnObjectiveChanged;
        tiles.Redraw += OnTilesChanged;

        // Read here rather than in the constructor: this is a singleton that outlives the page,
        // and a choice made on a second visit has to survive the first one's Stop.
        _chosen = choices.Read();

        NameObjective();

        _watching = new CancellationTokenSource();

        _ = WatchAsync(_watching.Token);

        // A map picked by name, drawn without waiting for a game to say anything. Also the
        // reload after a visit to another screen: Stop() lets go of every tile, so coming back
        // to a pinned map has to ask for them again.
        if (_pinned is { } pinned) _ = LoadAsync(pinned);
    }

    /// <summary>
    /// A tile came in, or a failed one may be asked for again: draw again, so that it shows or
    /// is asked for.
    ///
    /// Nothing listened for this for a long time, and it did not show, because a paired phone
    /// redraws on every bridge poll anyway. Without a bridge nothing else asks for a frame, so a
    /// fetched tile stayed invisible until the next zoom or pan.
    /// </summary>
    private void OnTilesChanged() => Changed?.Invoke();

    /// <summary>
    /// Stops, and lets go of every tile.
    ///
    /// The tiles are the point. Sixty decoded squares is around fifteen megabytes, and a screen
    /// that kept them while the player went off to look at their wallet would be holding that for
    /// no reason at all.
    /// </summary>
    public void Stop()
    {
        objective.Changed -= OnObjectiveChanged;
        tiles.Redraw -= OnTilesChanged;

        _watching?.Cancel();
        _watching?.Dispose();
        _watching = null;

        CancelInFlight();

        _motion.Stop();
        Underlay = null;
        _shownPlayer = null;

        tiles.Forget();

        IsLive = false;
    }

    /// <summary>
    /// Sets the zoom to whatever holds the whole map, once the viewport's size is known.
    ///
    /// Called from the page's layout rather than at load: the map's footprint is known from the
    /// API and the viewport's is not known until MAUI has measured it.
    /// </summary>
    public void Fit(double viewWidth, double viewHeight)
    {
        // Kept, because the two halves of this sum arrive in either order: the layout can measure
        // before the map has loaded, and the map can load long after the layout has settled. The
        // one that arrives second calls this again with the other's answer.
        if (viewWidth > 0 && viewHeight > 0) (_viewWidth, _viewHeight) = (viewWidth, viewHeight);

        if (_rect is not { } rect || viewWidth <= 0 || viewHeight <= 0) return;

        var zoom = TileGeometry.FitZoom(
            rect.Right - rect.Left, rect.Bottom - rect.Top, viewWidth, viewHeight, Reference);

        // The zoom that holds the whole map is also the furthest out anybody has a reason to go:
        // past it the map is a stamp in the middle of an empty screen. So the fit doubles as the
        // floor for pinching.
        _fitZoom = zoom;

        if (zoom == Zoom && _fitted) return;

        _motion.Stop();
        Underlay = null;

        Zoom = zoom;
        _fitted = true;

        Regrid();

        Changed?.Invoke();
    }

    /// <summary>
    /// Drags the map under the finger.
    ///
    /// The deltas are pixels at the current zoom, which is what a gesture reports; the centre is
    /// continent coordinates, which is what everything else here speaks.
    /// </summary>
    public void Pan(double dxPixels, double dyPixels)
    {
        // A finger on the map takes it over from any motion: the zoom lands, the glide stops.
        Settle();

        var units = TileGeometry.UnitsPerPixel(Zoom, Reference);

        // A drag says "this is the part of the map I am looking at", and the character walking
        // out of shot does not change that. See _following.
        _following = false;

        // Through the same turn as a tap. A finger dragged down the screen moves the map down the
        // screen whichever way up the phone is, which is the only behaviour that does not feel
        // broken in the hand.
        var (dx, dy) = QuarterTurn.Unturn(Quarter, dxPixels, dyPixels);

        CentreX -= dx * units;
        CentreY -= dy * units;

        Clamp();

        Changed?.Invoke();
    }

    /// <summary>
    /// Steps the zoom, keeping whatever is under the fingers under the fingers.
    ///
    /// Whole levels only. The service renders discrete levels and anything between them would have
    /// to be scaled from one of them, which is a blurred map — and the game's own map is the whole
    /// reason this screen is worth having. A pinch therefore accumulates until it has earned a
    /// level; see <c>MapPage</c>.
    ///
    /// Bounded by the fit at one end and the deepest rendered level at the other. Below the fit
    /// the map is a stamp in an empty screen; above <see cref="Reference"/> the service has
    /// nothing to serve and every tile would come back NoSuchKey.
    /// </summary>
    /// <param name="aboutCentre">
    /// True for the pad, false for a pinch. A pinch holds the point between the fingers still,
    /// because that is the point being pinched. A button in the corner has no such point — holding
    /// the corner still would throw whatever the player was looking at off the other side of the
    /// screen — so it zooms about the middle, which is where the eye already is.
    /// </param>
    /// <returns>True when the zoom moved.</returns>
    public bool ZoomBy(
        int steps, double focusX, double focusY, double screenWidth, double screenHeight,
        bool aboutCentre = false)
    {
        // A second tap while the first zoom is still easing starts from where the first ends.
        Settle();

        var wanted = Math.Clamp(Zoom + steps, _fitZoom, Reference);

        if (wanted == Zoom) return false;

        var (width, height) = Extent(screenWidth, screenHeight);

        // Where the fingers are, as a place in the world rather than a place on the glass. That
        // is the thing being held still: zooming about the middle of the screen instead makes a
        // map feel like it is fighting the hand.
        var focus = aboutCentre
            ? (X: width / 2, Y: height / 2)
            : FromScreen(focusX, focusY, screenWidth, screenHeight);

        var offsetX = focus.X - width / 2;
        var offsetY = focus.Y - height / 2;

        // The held point, found at the level being left. The motion eases the scale from this
        // level's to the next and keeps the point under the same pixel all the way; it ends where
        // TileGeometry.Recentre would have jumped to.
        var units = TileGeometry.UnitsPerPixel(Zoom, Reference);
        var held = (CentreX + offsetX * units, CentreY + offsetY * units);

        _motion.Zoom(Zoom, wanted, held, (offsetX, offsetY), TileGeometry.UnitsPerPixel(wanted, Reference));

        Underlay = Zoom;
        Zoom = wanted;

        Regrid();

        Changed?.Invoke();

        return true;
    }

    /// <summary>
    /// Back to the character, or to the middle of the map with no live game.
    ///
    /// Also how following is taken back up after a pan — the button that puts the character back
    /// on screen is the button that means "show me the character", so making it a second control
    /// would be splitting one intention in two.
    /// </summary>
    [RelayCommand]
    public void Recentre()
    {
        _following = true;

        Settle();

        if (Player is { } player) (CentreX, CentreY) = player;
        else if (_rect is { } rect) (CentreX, CentreY) = ((rect.Left + rect.Right) / 2,
            (rect.Top + rect.Bottom) / 2);

        Changed?.Invoke();
    }

    /// <summary>Puts the objective down, the same command the compass offers.</summary>
    [RelayCommand]
    public void ClearObjective() => objective.Choose(null);

    /// <summary>
    /// A tap on the map, in viewport pixels. <paramref name="density"/> is how many of those a
    /// density-independent unit is, which the zoom pad is sized in; see <see cref="ZoomPad"/>.
    ///
    /// The nearest marker within <see cref="TapRadius"/> becomes the objective. A tap on empty
    /// ground does nothing at all — it is not a request to clear, because panning with a slightly
    /// too-still finger reads as a tap, and losing the objective to that would be maddening.
    /// </summary>
    public void TapAt(double x, double y, double screenWidth, double screenHeight, double density)
    {
        var (viewWidth, viewHeight) = Extent(screenWidth, screenHeight);

        // Into the map's space before anything is compared, or a tap on a sideways map tests
        // against marker positions from an upright one.
        var (tapX, tapY) = FromScreen(x, y, screenWidth, screenHeight);

        // The pad first. It sits on top of the map, so a marker underneath it must not answer
        // instead — and the pad is laid out in this same space, so it turns with everything else.
        if (ZoomPad.Plus(viewWidth, viewHeight, density).Holds(tapX, tapY))
        {
            ZoomBy(1, x, y, screenWidth, screenHeight, aboutCentre: true);

            return;
        }

        if (ZoomPad.Minus(viewWidth, viewHeight, density).Holds(tapX, tapY))
        {
            ZoomBy(-1, x, y, screenWidth, screenHeight, aboutCentre: true);

            return;
        }

        CompassTarget? nearest = null;
        var best = TapRadius;

        foreach (var mark in Marks)
        {
            var at = ToView(mark.X, mark.Y, viewWidth, viewHeight);
            var away = Math.Sqrt(
                (at.X - tapX) * (at.X - tapX) + (at.Y - tapY) * (at.Y - tapY));

            if (away > best) continue;

            best = away;
            nearest = mark;
        }

        if (nearest is null) return;

        objective.Choose(nearest);

        NameObjective();

        Changed?.Invoke();
    }

    /// <summary>A continent point in viewport pixels, which is the one conversion the drawing
    /// needs and the hit test needs to agree with.</summary>
    public (double X, double Y) ToView(
        double continentX, double continentY, double viewWidth, double viewHeight) =>
        (TileGeometry.ToPixels(continentX - CentreX, Zoom, Reference) * Scale + viewWidth / 2,
            TileGeometry.ToPixels(continentY - CentreY, Zoom, Reference) * Scale + viewHeight / 2);

    /// <summary>The pixel at the left edge of the viewport, in the tile grid's own space.</summary>
    public double Left(double viewWidth) =>
        TileGeometry.ToPixels(CentreX, Zoom, Reference) * Scale - viewWidth / 2;

    public double Top(double viewHeight) =>
        TileGeometry.ToPixels(CentreY, Zoom, Reference) * Scale - viewHeight / 2;

    private (double Left, double Top, double Right, double Bottom)? _rect;
    private bool _fitted;

    /// <summary>The zoom that holds the whole map, which is as far out as a pinch may go.</summary>
    private int _fitZoom;

    /// <summary>The viewport as the page last measured it, so a map that loads afterwards can
    /// still be fitted to it.</summary>
    private double _viewWidth;

    private double _viewHeight;

    private async Task WatchAsync(CancellationToken ct)
    {
        journal.Write("map", "screen opened, streaming");

        try
        {
            await foreach (var report in stream.WatchAsync(ct)) Apply(report);
        }
        catch (OperationCanceledException)
        {
            // The screen closed.
        }

        IsLive = false;
    }

    /// <summary>One report from the bridge. Internal so a test can be the bridge: the stream itself
    /// is a held SignalR connection, and a test host has no PC to hold one to.</summary>
    internal void Apply(GameReport report)
    {
        // What the canvas draws from a report, as it was, so a report that changed none of it does
        // not cost a frame; see MapRedraw.
        var (guide, guideFrom, moving) = (Guide, GuideFrom, _motion.IsMoving);

        IsLive = report.IsLive;

        // Reached in the game: the objective is done, here and on the compass.
        if (trails.HasArrived(report)) ClearObjective();

        // Which map, and what it is called. The rules are in MapFocus, where a test can reach
        // them; what is left here is the part only a screen can do.
        var frame = MapFocus.Decide(report, _pinned, _pinnedName, _loadedMap);

        MapLine = frame.Line;

        Player = frame.ShowsPlayer && report.State is { } state
            ? (state.PlayerX, state.PlayerY)
            : null;

        if (frame.Load is { } wanted &&
            !(_failed is { } failed && failed.Map == wanted && DateTimeOffset.UtcNow - failed.At < RetryFailedMap))
            _ = LoadAsync(wanted);

        // Chosen before there was a position: choose again now there is one. See _guideSighted.
        if (!_guideSighted && Player is not null && objective.Chosen is not null) PlanRoute();

        Track();

        Describe();

        if (MapRedraw.PlayerMoved(_shownPlayer, Player, TileGeometry.UnitsPerPixel(Zoom, Reference) / Scale * PointDensity) ||
            !ReferenceEquals(guide, Guide) || guideFrom != GuideFrom || moving != _motion.IsMoving)
        {
            _shownPlayer = Player;

            Changed?.Invoke();
        }
    }

    private Task LoadAsync(int mapId) => RunAsync(async ct =>
    {
        // What to go back to if this map cannot be opened at all. The screen keeps drawing the
        // last map until a new one is ready, so the identity has to travel with the drawing: a
        // half-applied load is how the line came to name a map the tiles did not show. The pin
        // comes from _drawnPin rather than from _pinned, which Pin has already moved on by now.
        var wasLoaded = _loadedMap;
        var wasFitted = _fitted;

        _loadedMap = mapId;
        _fitted = false;

        try
        {
            await OpenAsync(mapId, ct);

            _failed = null;

            (_drawnPin, _drawnPinName) = (_pinned, _pinnedName);
        }
        catch (Gw2ApiException)
        {
            _failed = (mapId, DateTimeOffset.UtcNow);

            _loadedMap = wasLoaded;
            _fitted = wasFitted;

            // Only a pin is taken back. A map the character walked into failing to open is no
            // reason to re-pin one that FOLLOW let go of.
            if (_pinned == mapId)
            {
                _pinned = _drawnPin;
                _pinnedName = _drawnPinName;

                IsPinned = _pinned is not null;

                if (_pinned is not null) MapLine = _pinnedName;
                else if (!IsLive) MapLine = L.T("Waiting for the bridge…");
            }

            // Draw the map that is still here. Start let go of its tiles before this load, and
            // without a frame nothing asks for them again: the screen stayed blank.
            Changed?.Invoke();

            // Out to RunAsync, which turns it into the sentence the screen shows.
            throw;
        }
    });

    /// <summary>The name the map's loading screen is filed under, unless another map has been
    /// opened while it was being found.</summary>
    private async Task NameForArtAsync(MapInfo map)
    {
        var english = await catalogue.EnglishNameAsync(map);

        if (_map == map) ShownMap = english;
    }

    /// <summary>Everything one map needs, in the order it can be asked for.</summary>
    private async Task OpenAsync(int mapId, CancellationToken ct)
    {
        await icons.LoadAsync(ct);

        var map = await targets.InfoAsync(mapId, ct);

        // Two numbers, one request, then kept on disk: the zoom at which continent coordinates
        // are pixels, and the size of the grid. Nothing can be addressed without them — a phone
        // with every tile of Frostgorge cached and no stored continent still draws nothing.
        //
        // Asked for before anything on screen is touched. Setting the continent first left a
        // failed load half-applied: offline, picking Mistlock Sanctuary over Queensdale kept
        // Queensdale's name and pin but asked for its tiles on the Mists' continent, so the map
        // went blank (2026-10-13).
        var continent = await ContinentAsync(map.ContinentId, ct);

        _map = map;

        // Not awaited: on a phone in another language the first map can cost a request for every
        // map's English name, and the map itself should not wait on its painting.
        _ = NameForArtAsync(map);

        ContinentId = map.ContinentId;
        Floor = map.DefaultFloor;

        Reference = Math.Max(0, continent.MaxZoom - 1);
        _dims = continent.Dims;

        _rect = map.ContinentRect is [[var left, var top], [var right, var bottom]]
            ? (left, top, right, bottom)
            : null;

        // Markers are the one part a map can open without.
        //
        // Found offline, and it was drawing a lie: the landmarks for a map never opened with a
        // connection are not stored, the fetch threw, and the load stopped one line short of
        // publishing anything — so the screen said "Snowden Drifts" underneath Frostgorge's
        // tiles and Frostgorge's markers. A map named after one place and showing another is
        // worse than either an empty map or an error.
        //
        // So the map is always published, and a missing marker set is said in words. What it
        // publishes may well be a blank grid — a map never visited has no tiles stored either —
        // but a blank grid called Snowden Drifts is a true statement about what this phone has.
        IReadOnlyList<CompassTarget> marks = [];

        try
        {
            marks = await targets.OnMapAsync(mapId, ct);
        }
        catch (Gw2ApiException ex)
            when (ex.Kind is Gw2ErrorKind.Network or Gw2ErrorKind.ServiceUnavailable)
        {
            Notice = L.F("Nothing stored for {0} yet, and no connection to fetch it. " +
                         "Maps opened once with a connection stay available offline.", map.Name);
        }

        // Whatever the downloaded packs have for this map. Local work on files already read —
        // no request, no fetch — so it sits with the markers rather than behind its own failure
        // path, and a map with no packs installed answers empty.
        var routes = markers.RoutesOnMap(map);

        await mainThread.InvokeOnMainThreadAsync(() =>
        {
            if (ct.IsCancellationRequested) return;

            Marks = marks;

            AllRoutes = routes;
            Routes = RoutePicks.Shown(routes, _chosen);

            OnPropertyChanged(nameof(RouteChoices));
            OnPropertyChanged(nameof(HasRoutes));

            // The waypoints this map publishes are what the route is chosen from, so the advice
            // can only be worked out once they are here.
            PlanRoute();
            Describe();

            Regrid();

            // Now that the footprint is known. Before this the page had measured itself and been
            // turned away, so the screen sat at whatever zoom the field was initialised to.
            Fit(_viewWidth, _viewHeight);

            Recentre();
        });
    }

    /// <summary>
    /// Moves the map to keep up with the character, when it is following and they have walked
    /// out of the middle of the screen.
    ///
    /// The check is what makes this bearable to look at: the bridge reports ten times a second,
    /// and a map centred on the character every time slides continuously while they walk — see
    /// <see cref="FollowBox"/>. Nothing happens at all while they stay in the box, which is most
    /// of the time, so the steady cost of following is two subtractions and a comparison.
    ///
    /// Silent with no viewport measured yet, with no live character, and on a map the character
    /// is not standing in — <see cref="Player"/> is already null for the last of those.
    /// </summary>
    private void Track()
    {
        if (!_following || Player is not { } player) return;

        if (_viewWidth <= 0 || _viewHeight <= 0) return;

        // Anything under way owns the centre until it lands, a glide included: steering a glide at
        // every report kept it from ever arriving while the character walked. See MapMotion.
        if (_motion.IsMoving) return;

        // Already the map's own space: the page turns the screen through Extent before it calls
        // Fit, so these are the sides as the map sees them and turning them again would swap a
        // sideways phone back to upright.
        var at = ToView(player.X, player.Y, _viewWidth, _viewHeight);

        // Tighter and quicker close in; see FollowBox.ShareAt.
        if (!FollowBox.Escaped(at.X, at.Y, _viewWidth, _viewHeight, FollowBox.ShareAt(Level, Levels))) return;

        // Glided rather than jumped. The jump was the whole of what made following distracting:
        // the map moving a quarter of the screen in one frame reads as having gone somewhere else.
        _motion.GlideTo(ClampX(player.X), ClampY(player.Y), MapMotion.GlideSecondsAt(Level, Levels));
    }

    /// <summary>The continent from disk when it is there, otherwise from the API and then
    /// stored. Static data, dropped by <see cref="Gw2Cache"/> when the build moves.</summary>
    private async Task<ContinentInfo> ContinentAsync(int continentId, CancellationToken ct)
    {
        if (await cache.GetContinentAsync(continentId, ct) is { } stored) return stored;

        var continent = await api.GetContinentAsync(continentId, ct);

        await cache.SaveContinentAsync(continent, ct);

        return continent;
    }

    private (double Width, double Height)? _dims;

    /// <summary>Recomputes the tile grid for the current zoom.</summary>
    private void Regrid()
    {
        if (_dims is not { } dims) return;

        GridWidth = TileGeometry.GridTiles(dims.Width, Zoom, Reference);
        GridHeight = TileGeometry.GridTiles(dims.Height, Zoom, Reference);
    }

    /// <summary>
    /// Keeps the view over the map it is showing.
    ///
    /// The continent is mostly unrendered ocean, and a pan that wanders off into it shows a grey
    /// field with no way back that does not involve guessing. The map's own footprint is the
    /// bound.
    /// </summary>
    private void Clamp()
    {
        CentreX = ClampX(CentreX);
        CentreY = ClampY(CentreY);
    }

    private double ClampX(double x) => _rect is { } rect ? Math.Clamp(x, rect.Left, rect.Right) : x;

    private double ClampY(double y) => _rect is { } rect ? Math.Clamp(y, rect.Top, rect.Bottom) : y;

    private void OnObjectiveChanged(CompassTarget? _)
    {
        mainThread.BeginInvokeOnMainThread(() =>
        {
            NameObjective();

            Changed?.Invoke();
        });
    }

    private void NameObjective()
    {
        ObjectiveLine = objective.Chosen is { } chosen ? chosen.Name : L.T("No objective chosen");

        OnPropertyChanged(nameof(HasObjective));

        PlanRoute();
        Describe();
    }

    /// <summary>
    /// Whether there is an objective to clear.
    ///
    /// The button used to sit there with nothing to do, which is a control that teaches somebody
    /// it does nothing. Asked of <see cref="Objective"/> rather than kept as a field, because the
    /// compass sets it too and a copy here would be a second answer to the same question.
    /// </summary>
    public bool HasObjective => objective.Chosen is not null;

    /// <summary>
    /// Works out the port-then-walk advice, from the markers this map has in hand.
    ///
    /// The same rule the dial uses and deliberately the same code: a waypoint hidden from the dial
    /// is still the right place to port to, and a map centre still refuses to answer.
    /// </summary>
    private void PlanRoute()
    {
        _hop = objective.Chosen is { } chosen ? PortRoute.To(chosen, Marks) : null;
        _hopLine = _hop is null ? string.Empty : PortRoute.Line(_hop);

        // All routes, not only the ones switched on for display: choosing what the map shows must
        // not change which way somebody is sent. The same rule the compass follows.
        Guide = objective.Chosen is { Precise: true } target && target.MapId == _loadedMap
            ? TrailGuide.Find(AllRoutes, target.X, target.Y, Player)
            : null;

        _trailHop = Guide is { } guide && objective.Chosen is { } aim ? PortRoute.ToTrail(guide, aim, Marks) : null;
        _trailHopLine = _trailHop is null ? string.Empty : PortRoute.Line(_trailHop);

        _guideSighted = Player is not null;

        GuideFrom = Guide?.Back(Approach) ?? 0;

        // The same line the compass sends; TrailShare keeps the two from sending it twice.
        trails.Share(Guide, objective.Chosen, _map, Player);
    }

    /// <summary>
    /// The distance and direction to the objective, and the port advice when it is worth giving.
    ///
    /// Assigned only on a change. This runs on every frame the bridge sends — ten a second — and
    /// a blind assignment would have MAUI re-measure the panel for a string that is usually the
    /// one already there.
    /// </summary>
    private void Describe()
    {
        if (objective.Chosen is not { } chosen)
        {
            Set(string.Empty, string.Empty);

            return;
        }

        // The objective can belong to a map the character has walked off, because the compass
        // keeps it across a border. Nothing on these tiles then, and saying where it is beats
        // measuring to it from somewhere it is not.
        if (chosen.MapId != _loadedMap)
        {
            Set(L.FIn("map", "on {0}", chosen.MapName), _hopLine);

            return;
        }

        if (Player is not { } player)
        {
            Set(string.Empty, string.Empty);

            return;
        }

        var away = Bearing.Distance(player.X, player.Y, chosen.X, chosen.Y);
        var way = Bearing.Cardinal(Bearing.To(player.X, player.Y, chosen.X, chosen.Y));
        if (Guide is { } guide)
        {
            var leg = guide.Leg(player.X, player.Y);

            GuideFrom = leg.At;

            // The trail's port advice, not the straight line's: see PortRoute.ToTrail.
            var port = _trailHop?.BeatsWalking(leg) == true ? _trailHopLine : string.Empty;

            var height = TrailGuide.Height(leg.Climb);
            var trail = leg.IsOnTrail
                ? L.F("{0:0} along the trail", leg.Along)
                : L.F("{0:0} to the trail, then {1:0} along it", leg.OffTrail, leg.Along);

            Set(height.Length > 0 ? $"{trail} · {height}" : trail,
                port.Length > 0 ? port : L.F("following {0}", guide.Route.Name));

            return;
        }

        Set(L.F("{0:0} away · {1}", away, way), _hop?.BeatsWalking(away) == true ? _hopLine : string.Empty);
    }

    private void Set(string detail, string route)
    {
        if (ObjectiveDetail != detail) ObjectiveDetail = detail;

        if (RouteLine != route) RouteLine = route;
    }

    protected override string DefaultEmptyMessage =>
        L.T("No game to follow. Tap CHOOSE MAP to look at any map by name.");
}
