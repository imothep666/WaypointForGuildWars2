using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.ViewModels;

/// <summary>
/// One thing the compass can point at, as the picker lists it.
/// </summary>
public sealed partial class TargetRow(CompassTarget target, double distance) : ObservableObject
{
    public CompassTarget Target { get; } = target;

    public string Name => Target.Name;

    /// <summary>How far it was when last measured. Set again when the list is opened; see
    /// <see cref="CompassViewModel.TogglePicker"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail))]
    private double _distance = distance;

    /// <summary>"Heart · 240" or "World boss · in 12 min". The kind first, because the list is
    /// long and the eye scans for a kind before it reads a name.</summary>
    public string Detail =>
        Target.Note is { Length: > 0 } note
            ? $"{Target.KindLabel} · {note}"
            : $"{Target.KindLabel} · {Distance:0} away";

    /// <summary>
    /// The list's order: what is on this map nearest first, the heart you are standing next to
    /// before the one across the map, then the world bosses in the order they were given — by when
    /// they spawn, which is the question about a boss, not how far away it is.
    /// </summary>
    public static IReadOnlyList<TargetRow> Ranked(IEnumerable<TargetRow> rows)
    {
        var all = rows.ToList();

        return all.Where(row => row.Target.Kind != TargetKind.WorldBoss)
            .OrderBy(row => row.Distance)
            .Concat(all.Where(row => row.Target.Kind == TargetKind.WorldBoss))
            .ToList();
    }

    [ObservableProperty] private bool _isChosen;

    /// <summary>Dimmed and labelled in the filter sheet rather than dropped from it, or there
    /// would be nothing left to tap to bring it back.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DismissLabel))]
    [NotifyPropertyChangedFor(nameof(Fade))]
    private bool _isDismissed;

    /// <summary>What the button on this row does next, rather than what the row is.</summary>
    public string DismissLabel => IsDismissed ? "SHOW" : "HIDE";

    /// <summary>A plain double rather than a converter in XAML: a Converter there forces a
    /// reflection-based binding, which the trimmer cannot see through.</summary>
    public double Fade => IsDismissed ? 0.4 : 1;
}

/// <summary>
/// One switch in the filter sheet: a whole kind of thing, on or off.
/// </summary>
public sealed partial class KindRow(TargetKind kind, string label) : ObservableObject
{
    public TargetKind Kind { get; } = kind;

    public string Label { get; } = label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    private bool _isShown = true;

    /// <summary>What the kind is now, not what the button does — the row above it names the kind,
    /// so "HIDDEN" reads as a state and "HIDE" would read as a second thing to tap.</summary>
    public string StateLabel => IsShown ? "SHOWN" : "HIDDEN";

    /// <summary>How many of this kind the current map has, so a switch that would change nothing
    /// says so instead of looking broken.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail))]
    private int _count;

    public string Detail => Count switch
    {
        0 => "none on this map",
        1 => "1 on this map",
        _ => $"{Count} on this map"
    };
}

/// <summary>
/// The live screen: where the character is, where the objective is, and which way to turn.
///
/// The only screen in the app driven by a held connection rather than a refresh. Everything on
/// it comes from two places that cannot be combined anywhere else — MumbleLink on the PC, for
/// the character's position and facing, and the Guild Wars 2 API for where anything is. The
/// phone is the only device that has both.
/// </summary>
public sealed partial class CompassViewModel(
    GameStream stream, CompassTargets targets, MapIcons icons,
    ICompassVisibilityStore visibility, Objective objective,
    Bridge.Protocol.Journal journal, TrailShare trails,
    IMainThread mainThread, TestEvidence evidence) : BaseViewModel
{
    /// <summary>
    /// How many marks the dial carries at once.
    ///
    /// Queensdale alone has around fifty landmarks, and a dial holding all of them is a texture
    /// rather than an instrument. Nearest first, so what is cut is what was least worth walking
    /// to — plus the chosen one, which is never cut whatever its range.
    ///
    /// Twelve rather than the twenty-four this started at, and the second reason is the stronger
    /// one. Each mark is a stamped image, and every stamp is an <c>ICanvas.DrawImage</c> call on
    /// every frame: twenty-four of them at sixty frames a second is fourteen hundred a second,
    /// which was measured on the phone as about 9 MB/min of memory the process never got back.
    /// Six hours of that is the SIGABRT in the log — <c>Scudo ERROR: internal map failure
    /// (error desc=Out of memory)</c>. Halving the count halves the calls, and the reading
    /// barely changes: past a dozen marks the ring is a texture again anyway.
    ///
    /// Measured three ways on one device, same stream, to be sure it was the stamps and not the
    /// stream: twenty-four stamped marks grew 12.4 MB/min, twenty-four plain dots 3.3 MB/min,
    /// and no marks at all 0.6 MB/min. See also <c>CompassPage.FrameInterval</c>, which is the
    /// other half of the same sum.
    /// </summary>
    private const int MostBlips = 12;

    private CancellationTokenSource? _watching;
    private int _loadedMap = -1;
    private string? _loadedCharacter;

    /// <summary>How many finished things are being kept off the dial, for a line that explains
    /// why it is emptier than the in-game map.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HiddenLine))]
    [NotifyPropertyChangedFor(nameof(HasHidden))]
    private int _hidden;

    /// <summary>How many the filter is keeping off the dial, as opposed to the API's own answer
    /// about what is finished. Counted separately because the two are undone in different
    /// places — one by walking to the thing, the other by a switch.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HiddenLine))]
    [NotifyPropertyChangedFor(nameof(HasHidden))]
    [NotifyPropertyChangedFor(nameof(FilterLine))]
    private int _filtered;

    public bool HasHidden => Hidden > 0 || Filtered > 0;

    /// <summary>
    /// Says what is missing and, just as importantly, what is not.
    ///
    /// Without the second half the dial looks broken to anyone who notices their completed
    /// hearts are still on it: the honest answer is that the API publishes no heart progress,
    /// and the screen should say so rather than let it read as a bug.
    /// </summary>
    public string HiddenLine =>
        (Hidden, Filtered) switch
        {
            (0, 0) => string.Empty,
            (0, var filter) => $"{filter} hidden by the filter.",
            (var done, 0) =>
                $"{done} finished hero points and bosses hidden. Hearts, waypoints and vistas " +
                "stay: the API publishes no completion for them — hide them yourself under Filter.",
            var (done, filter) =>
                $"{done} finished hero points and bosses hidden, {filter} by the filter."
        };

    /// <summary>What the filter sheet's own footer says about the pile it is holding back.</summary>
    public string FilterLine =>
        Filtered == 0 ? "Nothing hidden by hand." : $"{Filtered} hidden by hand.";

    /// <summary>The rose's own state, handed to the drawable each frame.</summary>
    public double? Relative { get; private set; }

    public double Facing { get; private set; }

    public bool IsPrecise { get; private set; } = true;

    /// <summary>
    /// Everything the dial draws this frame: direction and range for each.
    ///
    /// An array and a count rather than a list, because the array is one of two that are filled
    /// alternately and handed over again and again. Read <see cref="BlipCount"/> entries: what
    /// follows is the frame before last's, left where it was.
    /// </summary>
    public Blip[] Blips { get; private set; } = [];

    /// <summary>How many of <see cref="Blips"/> belong to this frame.</summary>
    public int BlipCount { get; private set; }

    /// <summary>Where the character has just walked, projected into the dial's frame. Same
    /// arrangement as <see cref="Blips"/>; read <see cref="TrailCount"/> entries.</summary>
    public (double Relative, double Distance)[] Trail { get; private set; } = [];

    /// <summary>How many of <see cref="Trail"/> belong to this frame.</summary>
    public int TrailCount { get; private set; }

    /// <summary>The trail still to walk to the objective, projected like <see cref="Trail"/>;
    /// read <see cref="GuideCount"/> entries. Empty when no trail leads there.</summary>
    public (double Relative, double Distance)[] Guide { get; private set; } = [];

    /// <summary>How many of <see cref="Guide"/> belong to this frame.</summary>
    public int GuideCount { get; private set; }

    /// <summary>
    /// The most guide points drawn. Thinning leaves a trail point every fifteen metres or so, so
    /// this is several thousand units of trail — past the dial's outer ring, where more points
    /// would all land on the rim.
    /// </summary>
    private const int MostGuidePoints = 400;

    private readonly (double X, double Y)[] _guideBuffer = new (double X, double Y)[MostGuidePoints];

    private readonly (double Relative, double Distance)[][] _guides =
    [
        new (double Relative, double Distance)[MostGuidePoints],
        new (double Relative, double Distance)[MostGuidePoints]
    ];

    /// <summary>
    /// Two of everything, used turn and turn about.
    ///
    /// The dial is repainted from these arrays after <see cref="Advance"/> has returned, not
    /// during it: Invalidate only asks for a draw, and the draw happens when the platform gets
    /// round to it. Filling one array in place would therefore let a late draw read a frame being
    /// written underneath it - not a crash, but a trail flickering between two shapes.
    /// Alternating gives the reader a whole frame of grace and still allocates nothing.
    ///
    /// Both are cut to the largest they can ever need - the ring buffer's capacity, and the blip
    /// cut plus the chosen one - so they are built once and never grown.
    /// </summary>
    private readonly (double X, double Y)[] _walkedBuffer =
        new (double X, double Y)[WalkedPath.Capacity];

    private readonly (double Relative, double Distance)[][] _trails =
    [
        new (double Relative, double Distance)[WalkedPath.Capacity],
        new (double Relative, double Distance)[WalkedPath.Capacity]
    ];

    private readonly Blip[][] _plots = [new Blip[MostBlips + 1], new Blip[MostBlips + 1]];

    /// <summary>Scratch for ranking this frame's targets by range. Cleared and refilled rather
    /// than rebuilt, so it stops allocating once it has seen its busiest map.</summary>
    private readonly List<(double Distance, CompassTarget Target)> _ranked = [];

    private static readonly Comparison<(double Distance, CompassTarget Target)> Nearest =
        (a, b) => a.Distance.CompareTo(b.Distance);

    private int _side;

    private readonly WalkedPath _walked = new();

    /// <summary>The game's own marker art, for the dial to stamp.</summary>
    public IImage? Symbol(TargetKind kind) => icons.For(kind);

    /// <summary>Raised once per frame, so the page can invalidate the drawing without every
    /// value on it being a bound property redrawing the whole layout.</summary>
    public event Action? Moved;

    [ObservableProperty] private string _characterLine = "Waiting for the bridge…";

    [ObservableProperty] private string _mapLine = string.Empty;

    [ObservableProperty] private string _instruction = string.Empty;

    [ObservableProperty] private string _distanceLine = string.Empty;

    /// <summary>
    /// Which waypoint to port to, when the objective is far enough that walking is not what
    /// anybody would actually do. Empty the rest of the time — see <see cref="PortRoute"/>.
    /// </summary>
    [ObservableProperty] private string _routeLine = string.Empty;

    [ObservableProperty] private string _targetLine = "No objective chosen";

    /// <summary>Shown under the needle when the target is only placed to the nearest map.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrecisionWarning))]
    private string? _precisionWarning;

    /// <summary>A plain bool rather than a null check in XAML: a converter there would force a
    /// reflection-based binding, which the trimmer cannot see through.</summary>
    public bool HasPrecisionWarning => !string.IsNullOrWhiteSpace(PrecisionWarning);

    [ObservableProperty] private bool _isLive;

    [ObservableProperty] private bool _isPickerOpen;

    /// <summary>What the dial draws and the picker lists: everything the filter lets through.</summary>
    public ObservableCollection<TargetRow> Rows { get; } = [];

    /// <summary>Everything on this map, filter or no filter. The filter sheet lists this, so a
    /// target hidden by hand is still somewhere the player can reach to bring it back.</summary>
    public ObservableCollection<TargetRow> AllRows { get; } = [];

    /// <summary>One switch per kind, rebuilt with the map so the counts mean this map.</summary>
    public ObservableCollection<KindRow> KindRows { get; } = [];

    /// <summary>Which half of the sheet is showing: the target list, or the filter.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTargetsOpen))]
    private bool _isFilterOpen;

    /// <summary>The other half. A plain bool rather than a converter in XAML, for the reason
    /// given on <see cref="HasPrecisionWarning"/>.</summary>
    public bool IsTargetsOpen => !IsFilterOpen;

    /// <summary>
    /// The sheet's two halves, as the same tab strip every other screen with two halves uses.
    ///
    /// This was a pair of buttons that swapped their own labels — FILTER became TARGETS — which
    /// told nobody which half they were looking at and matched nothing else in the app. The
    /// settings and character screens both carry a <see cref="PageTab"/> strip; so does this.
    /// </summary>
    /// <summary>
    /// Marked at birth rather than on the first change. OnIsFilterOpenChanged only runs when the
    /// value moves, and the sheet opens on Objectives — so without this neither tab carried the
    /// brass rule until the player had switched to Filter and back.
    /// </summary>
    public ObservableCollection<PageTab> Tabs { get; } =
    [
        new("Objectives", 0) { IsSelected = true }, new("Filter", 1)
    ];

    /// <summary>Switches which half of the sheet is on screen.</summary>
    public void Select(PageTab tab) => IsFilterOpen = tab.Index == 1;

    /// <summary>
    /// Keeps the strip in step with the sheet, whichever moved first.
    ///
    /// The sheet is opened and closed from three places — the rail, the tabs and Android's back
    /// gesture — and only one of them is a tab. Marking the strip here means none of the others
    /// has to remember to.
    /// </summary>
    partial void OnIsFilterOpenChanged(bool value)
    {
        foreach (var tab in Tabs) tab.IsSelected = tab.Index == (value ? 1 : 0);
    }

    private CompassVisibility _visibility = CompassVisibility.Everything;

    private CompassTarget? _chosen;

    /// <summary>The port-then-walk advice for <see cref="_chosen"/>, worked out once when it is
    /// chosen. Null when there is none to give.</summary>
    private PortHop? _hop;

    /// <summary>The words for <see cref="_hop"/>, built with it rather than every frame.</summary>
    private string _hopLine = string.Empty;

    /// <summary>Every trail the installed packs have for the map the character is on. What the
    /// guide is chosen from; see <see cref="TrailGuide"/>.</summary>
    private IReadOnlyList<RouteLine> _routes = [];

    /// <summary>The trail that leads to <see cref="_chosen"/>, or null when none goes near it.</summary>
    private TrailGuide? _guide;

    /// <summary>The port advice while a trail is followed: the waypoint nearest the trail's way in,
    /// not the one nearest the objective. See <see cref="PortRoute.ToTrail"/>.</summary>
    private TrailHop? _trailHop;

    private string _trailHopLine = string.Empty;

    /// <summary>The map the character is on, for turning the objective into the game's metres
    /// when the trail is handed to Blish HUD.</summary>
    private Gw2.MapInfo? _map;

    /// <summary>The height as last said, so the route line is only rebuilt when it moves.</summary>
    private string _guideHeight = string.Empty;

    /// <summary>The route line for the guide, as last built.</summary>
    private string _guideLine = string.Empty;

    private GameState? _last;

    /// <summary>
    /// Starts listening. Called when the page appears.
    ///
    /// The stream is held only while the screen is on top: it keeps a socket open and drives the
    /// bridge's MumbleLink poll up to ten a second, and neither should outlive somebody looking
    /// at it.
    /// </summary>
    public void Start()
    {
        Stop();

        // The kind switches before anything has connected. Read without a character, which gives
        // the kinds — those are the player's answer, not the character's — and no dismissals;
        // the first map load reads it again once there is a name to read it under.
        _visibility = visibility.Read(_loadedCharacter ?? string.Empty);

        // The map screen can have chosen one while this screen was not on top. Adopting it here
        // rather than only listening means the dial is already pointing when it appears.
        objective.Changed += OnObjectiveChanged;

        if (!ReferenceEquals(objective.Chosen, _chosen)) Choose(objective.Chosen);

        BuildKinds();

        _watching = new CancellationTokenSource();

        _ = WatchAsync(_watching.Token);
    }

    /// <summary>Forgets the walked trail. The screen offers it because a tail from half an hour
    /// ago is clutter once somebody has settled somewhere new.</summary>
    [RelayCommand]
    public void ClearTrail()
    {
        _walked.Clear();

        TrailCount = 0;

        Moved?.Invoke();
    }

    public void Stop()
    {
        objective.Changed -= OnObjectiveChanged;

        _watching?.Cancel();
        _watching?.Dispose();
        _watching = null;

        // The map load as well as the stream. LoadAsync runs under RunAsync's own token, not this
        // one, so cancelling the watch alone left a load in flight that finished after the page
        // had gone - and then poured rows into collections the dead page was still bound to. That
        // is not a quiet no-op: realising a row's Label asks the page's service scope for the
        // font manager, and that scope is already disposed.
        CancelInFlight();

        IsLive = false;
    }

    private async Task WatchAsync(CancellationToken ct)
    {
        journal.Write("compass", "screen opened, streaming");

        try
        {
            await foreach (var report in stream.WatchAsync(ct)) Apply(report);
        }
        catch (OperationCanceledException)
        {
            // The screen closed.
        }

        IsLive = false;

        journal.Write("compass", $"screen closed after state {stream.State}");
    }

    /// <summary>
    /// One frame.
    ///
    /// Text properties are only assigned when they change. At ten frames a second a blind
    /// assignment would raise forty property-changed events every second for values that are
    /// usually identical, and MAUI would relayout for each.
    /// </summary>
    private void Apply(GameReport report)
    {
        IsLive = report.IsLive;

        // Reached in the game: the objective is done, here and on the map.
        if (trails.HasArrived(report))
        {
            ClearObjective();
            evidence.Saw("trail-arrival");
        }

        if (report.State is not { } state)
        {
            Set(nameof(CharacterLine), CharacterLine, report.Note, v => CharacterLine = v);
            Relative = null;
            Moved?.Invoke();

            return;
        }

        _last = state;

        Set(nameof(CharacterLine), CharacterLine, state.Line, v => CharacterLine = v);
        Set(nameof(MapLine), MapLine, report.MapLabel, v => MapLine = v);

        // The reported position, not the eased one: the trail is a record of where the character
        // actually went, and smoothing it would be recording the drawing rather than the walk.
        _walked.Add(state.MapId, state.PlayerX, state.PlayerY);

        // The newest truth, not what is drawn. Advance eases towards this at display rate; see
        // the remarks there for why the two are separate.
        _targetFacing = state.HasHeading ? state.HeadingRadians : 0;
        _targetX = state.PlayerX;
        _targetY = state.PlayerY;

        if (!_started)
        {
            Facing = _targetFacing;
            _renderX = _targetX;
            _renderY = _targetY;
            _started = true;
        }

        if (state.MapId != _loadedMap || state.CharacterName != _loadedCharacter)
            _ = LoadAsync(state.MapId, state.CharacterName);

    }

    /// <summary>
    /// Moves what is drawn a little closer to what was last reported, and rebuilds the dial.
    ///
    /// Called at display rate rather than at the rate frames arrive, which is the whole point.
    /// The bridge sends ten frames a second; painting only on those made the needle and the marks
    /// step visibly, because each frame is a discrete jump and nothing filled the ninety
    /// milliseconds between them. Easing at sixty frames a second turns the same data into
    /// motion.
    ///
    /// Exponential rather than linear, so it converges without overshooting and without needing
    /// to know when the next frame will arrive — a dropped frame just means one more tick of the
    /// same easing rather than a stall followed by a jerk.
    /// </summary>
    public void Advance(double seconds)
    {
        if (!_started) return;

        // 1 - e^(-dt/tau): frame-rate independent, so a stutter in the UI thread does not change
        // how fast the dial catches up.
        var follow = 1 - Math.Exp(-seconds / Smoothing);

        // The short way round, or the dial spins a whole turn whenever the heading crosses south.
        Facing = Bearing.Normalise(Facing + Bearing.Normalise(_targetFacing - Facing) * follow);

        _renderX += (_targetX - _renderX) * follow;
        _renderY += (_targetY - _renderY) * follow;

        // Turn and turn about; see the remarks on the buffers.
        _side ^= 1;

        var trail = _trails[_side];
        TrailCount = Project(_renderX, _renderY, trail);
        Trail = trail;

        var plot = _plots[_side];
        BlipCount = Plot(_renderX, _renderY, plot);
        Blips = plot;

        var guide = _guides[_side];
        GuideCount = ProjectGuide(_renderX, _renderY, guide);
        Guide = guide;

        Aim(_renderX, _renderY);

        Moved?.Invoke();
    }

    /// <summary>
    /// How quickly the drawing chases the last reported position, as a time constant in seconds.
    ///
    /// Eighty milliseconds is a little under the gap between frames, so the dial has almost
    /// caught up by the time the next one lands: fast enough not to feel like lag, slow enough to
    /// hide the step.
    /// </summary>
    private const double Smoothing = 0.08;

    private double _targetFacing;
    private double _targetX;
    private double _targetY;
    private double _renderX;
    private double _renderY;
    private bool _started;

    /// <summary>
    /// Works out the turn for the chosen target, from the eased position rather than the last
    /// reported one, so the words change in step with the dial.
    /// </summary>
    private void Aim(double fromX, double fromY)
    {
        if (_chosen is not { } target)
        {
            Relative = null;
            Instruction = string.Empty;
            DistanceLine = string.Empty;
            RouteLine = string.Empty;

            return;
        }

        if (_last is { } here && !target.CanPointFrom(here.MapId))
        {
            Relative = null;

            Set(nameof(Instruction), Instruction, "you are on this map", v => Instruction = v);
            Set(nameof(DistanceLine), DistanceLine, string.Empty, v => DistanceLine = v);
            Set(nameof(RouteLine), RouteLine, string.Empty, v => RouteLine = v);

            return;
        }

        var away = Bearing.Distance(fromX, fromY, target.X, target.Y);

        IsPrecise = target.Precise;

        if (_guide is { } guide && _last?.MapId == guide.Route.MapId)
        {
            FollowTrail(guide, fromX, fromY);

            return;
        }

        Relative = Bearing.Relative(fromX, fromY, Facing, target.X, target.Y);

        Set(nameof(Instruction), Instruction,
            _last?.HasHeading == true
                ? Bearing.Describe(Relative.Value)
                : "no heading from the PC",
            v => Instruction = v);

        Set(nameof(DistanceLine), DistanceLine, $"{away:0} away", v => DistanceLine = v);

        // Only the decision moves with the character; the words were built when the objective was
        // chosen. Advice that appears as the walk grows and goes again as it shrinks, without
        // formatting a string on every frame of the dial.
        Set(nameof(RouteLine), RouteLine,
            _hop?.BeatsWalking(away) == true ? _hopLine : string.Empty,
            v => RouteLine = v);
    }

    /// <summary>
    /// Points along a trail somebody walked, instead of straight at the objective.
    ///
    /// The needle aims a little way ahead on the trail — or back to it, once the character has
    /// wandered off — and the distance is what is left to walk, not the straight line, because the
    /// straight line is the thing that was no use: it is how a hidden chamber stays hidden. The
    /// port advice still wins the route line when it applies; porting beats any walk it replaces.
    /// It is the trail's port advice, measured onto the trail and along it, not the straight line's.
    /// </summary>
    private void FollowTrail(TrailGuide guide, double fromX, double fromY)
    {
        var leg = guide.Leg(fromX, fromY);

        Relative = Bearing.Relative(fromX, fromY, Facing, leg.AimX, leg.AimY);

        Set(nameof(Instruction), Instruction,
            _last?.HasHeading == true
                ? leg.IsOnTrail ? Bearing.Describe(Relative.Value) : $"back to the trail, {Bearing.Describe(Relative.Value)}"
                : "no heading from the PC",
            v => Instruction = v);

        Set(nameof(DistanceLine), DistanceLine,
            leg.IsOnTrail ? $"{leg.Along:0} along the trail" : $"{leg.OffTrail:0} to the trail",
            v => DistanceLine = v);

        if (_trailHop?.BeatsWalking(leg) == true)
        {
            Set(nameof(RouteLine), RouteLine, _trailHopLine, v => RouteLine = v);

            return;
        }

        // Only rebuilt when the height moves by a whole metre, not every frame of the dial.
        var height = TrailGuide.Height(leg.Climb);

        if (height != _guideHeight || _guideLine.Length == 0)
        {
            _guideHeight = height;
            _guideLine = height.Length > 0
                ? $"following {guide.Route.Name} · {height}"
                : $"following {guide.Route.Name}";
        }

        Set(nameof(RouteLine), RouteLine, _guideLine, v => RouteLine = v);
    }

    /// <summary>
    /// Loads what can be pointed at, for the map the character is standing in.
    ///
    /// Landmarks are the map's own; world bosses are added whatever map they are on, because the
    /// useful question about a boss is which way it is from here, and the answer is a direction
    /// across the continent rather than a reason to hide it.
    /// </summary>
    private async Task LoadAsync(int mapId, string character) => await RunAsync(async ct =>
    {
        _loadedMap = mapId;
        _loadedCharacter = character;

        await icons.LoadAsync(ct);

        // What this character has already done, so the dial empties as they work a map. Only
        // hero points and world bosses can be filtered; see CompassFilter for why hearts cannot.
        var filter = await targets.ProgressAsync(character, ct);

        var here = (await targets.OnMapAsync(mapId, ct)).Where(filter.Keep).ToList();
        var bosses = (await targets.BossesAsync(DateTimeOffset.UtcNow, TimeSpan.FromHours(2), ct))
            .Where(filter.Keep)
            .ToList();

        Hidden = filter.Done.Count;

        // Local work on packs already read, so no reason to fail the load over it.
        var routes = await targets.RoutesAsync(mapId, ct);
        var info = await targets.InfoAsync(mapId, ct);

        journal.Write("compass", $"{character} on map {mapId}: {targets.Note ?? "no progress read"}");

        var from = _last;

        // Nearest first, which is almost always the order somebody wants; see TargetRow.Ranked.
        var rows = TargetRow.Ranked(here.Concat(bosses).Select(t => new TargetRow(t, Away(from, t))));

        // Read with the character rather than once at startup: half of what is stored is that
        // character's own answer about what it has finished.
        _visibility = visibility.Read(character);

        // Rows is bound to a CollectionView, and frames arrive on the stream's thread. Mutating
        // an ObservableCollection from there raises CollectionChanged off the UI thread, which on
        // Android is a crash waiting for the moment the list happens to be on screen.
        await mainThread.InvokeOnMainThreadAsync(() =>
        {
            // The screen can close between the fetch and this dispatch, and the check has to be
            // here rather than before the await: it is the mutation that is dangerous, not the
            // work that led to it.
            if (ct.IsCancellationRequested) return;

            AllRows.Clear();

            foreach (var row in rows) AllRows.Add(row);

            _routes = routes;
            _map = info;

            BuildKinds();
            Sift();
        });

        // A target on the old map stays selected — walking through a map border must not silently
        // drop the thing being walked towards, and continent coordinates are continent-wide, so
        // the bearing and the range across a border are the same arithmetic as within one.
        //
        // It used to be dropped here, and the reason was a real one: no row holds it any more, so
        // it is gone from the picker and from the list the dial is plotted from. Both are handled
        // where they arise — Plot adds it back, and the readout names its map — and neither was
        // worth forgetting an objective over. The way to put it down is CLEAR OBJECTIVE, which is
        // the same way as on its own map.
        //
        // On the UI thread: this runs on the stream's thread, and TargetLine is bound.
        if (_chosen is not null)
            await mainThread.InvokeOnMainThreadAsync(() =>
            {
                if (ct.IsCancellationRequested) return;

                NameObjective();

                // An objective chosen on the map screen before this one had its rows — or before
                // the character had walked onto its map at all — has no route yet. This is the
                // moment its waypoints are in hand.
                if (_chosen?.MapId == mapId) PlanRoute();
            });
    });

    /// <summary>
    /// Turns the recorded world positions into the dial's own frame.
    ///
    /// Re-done every frame on purpose. The dial is drawn relative to where the character is
    /// looking, so both the bearing and the distance of every past point change as they walk and
    /// turn — a trail cached as angles would be wrong the moment they moved.
    /// </summary>
    /// <returns>How many entries of <paramref name="into"/> this frame filled.</returns>
    private int Project(double fromX, double fromY, (double Relative, double Distance)[] into)
    {
        var taken = _walked.Snapshot(_walkedBuffer);

        for (var i = 0; i < taken; i++)
            into[i] = (
                Bearing.Relative(fromX, fromY, Facing, _walkedBuffer[i].X, _walkedBuffer[i].Y),
                Bearing.Distance(fromX, fromY, _walkedBuffer[i].X, _walkedBuffer[i].Y));

        return taken;
    }

    /// <summary>
    /// The trail still to walk, in the dial's frame. The dial has no needle — the only direction it
    /// drew to the objective was a line straight at it, which is exactly the wrong one when a trail
    /// goes round — so the way itself is drawn instead.
    /// </summary>
    private int ProjectGuide(double fromX, double fromY, (double Relative, double Distance)[] into)
    {
        if (_guide is not { } guide || _last?.MapId != guide.Route.MapId) return 0;

        var taken = guide.Copy(guide.Leg(fromX, fromY).At, _guideBuffer);

        for (var i = 0; i < taken; i++)
            into[i] = (
                Bearing.Relative(fromX, fromY, Facing, _guideBuffer[i].X, _guideBuffer[i].Y),
                Bearing.Distance(fromX, fromY, _guideBuffer[i].X, _guideBuffer[i].Y));

        return taken;
    }

    /// <summary>
    /// The nearest handful, plus whatever is chosen.
    ///
    /// Sorted by distance and cut to <see cref="MostBlips"/>. The chosen target is appended when
    /// the cut would have dropped it — a world boss two maps away is exactly the thing somebody
    /// picked on purpose and would be first out of a nearest-first list.
    /// </summary>
    /// <returns>How many entries of <paramref name="into"/> this frame filled.</returns>
    private int Plot(double fromX, double fromY, Blip[] into)
    {
        // Ranked by hand rather than by an OrderBy/Take/Concat/DistinctBy chain. The answer is
        // the same one; what is gone is five iterators, a lambda's closure and a hash set built
        // and dropped thirty times a second for a list of a few dozen.
        _ranked.Clear();

        var holdsChosen = false;

        foreach (var row in Rows)
        {
            var target = row.Target;

            // A boss on this very map has no direction worth drawing; see
            // CompassTarget.CanPointFrom.
            if (_last is { } here && !target.CanPointFrom(here.MapId)) continue;

            if (ReferenceEquals(target, _chosen)) holdsChosen = true;

            _ranked.Add((Bearing.Distance(fromX, fromY, target.X, target.Y), target));
        }

        // The objective can be on a map the character has since left, in which case no row holds
        // it — the list is this map's. It is still where it was and still worth a mark, so it
        // joins the ranking on its own. See LoadAsync for why it is not dropped at the border.
        if (!holdsChosen && _chosen is { } away &&
            (_last is not { } from || away.CanPointFrom(from.MapId)))
            _ranked.Add((Bearing.Distance(fromX, fromY, away.X, away.Y), away));

        _ranked.Sort(Nearest);

        var taken = Math.Min(MostBlips, _ranked.Count);

        for (var i = 0; i < taken; i++) into[i] = Mark(_ranked[i], fromX, fromY);

        if (_chosen is null) return taken;

        // Only past the cut: inside it the loop above has already drawn it.
        for (var i = taken; i < _ranked.Count; i++)
            if (ReferenceEquals(_ranked[i].Target, _chosen))
            {
                into[taken] = Mark(_ranked[i], fromX, fromY);

                return taken + 1;
            }

        return taken;
    }

    /// <summary>One ranked target as the dial draws it. The range is the one the ranking already
    /// worked out, rather than the same square root a second time.</summary>
    private Blip Mark((double Distance, CompassTarget Target) ranked, double fromX, double fromY) =>
        new(ranked.Target.Kind,
            Bearing.Relative(fromX, fromY, Facing, ranked.Target.X, ranked.Target.Y),
            ranked.Distance,
            ReferenceEquals(ranked.Target, _chosen));

    private static double Away(GameState? from, CompassTarget target) =>
        from is null ? 0 : Bearing.Distance(from.PlayerX, from.PlayerY, target.X, target.Y);

    [RelayCommand]
    public void Pick(TargetRow? row)
    {
        Choose(row?.Target);

        IsPickerOpen = false;
    }

    [RelayCommand]
    public void TogglePicker()
    {
        IsPickerOpen = !IsPickerOpen;

        if (!IsPickerOpen) return;

        // Always back to the target list. The filter is a detour, and a sheet that reopens on
        // the page it was last closed on hides the thing it exists to offer.
        IsFilterOpen = false;

        Remeasure();
    }

    /// <summary>
    /// Measures every row again from where the character is now, and puts the list back in order.
    ///
    /// The rows are built once per map, so their distances were the ones from when the map loaded:
    /// after a port within the same map the list went on saying how far everything was from where
    /// the character used to be, nearest-first by the wrong point. Seen with the simulator moved
    /// within the Silverwastes — 448,000 away beside a dial that was right. When the list is opened
    /// rather than every frame: it is only read while open, and re-sorting it while open would move
    /// a row out from under the finger reaching for it.
    /// </summary>
    private void Remeasure()
    {
        if (_last is not { } from || AllRows.Count == 0) return;

        foreach (var row in AllRows) row.Distance = Away(from, row.Target);

        // Moved rather than cleared and refilled, for the same reason as Reconcile.
        var ranked = TargetRow.Ranked(AllRows);

        for (var i = 0; i < ranked.Count; i++)
        {
            var at = AllRows.IndexOf(ranked[i]);

            if (at != i) AllRows.Move(at, i);
        }

        Sift();
    }

    [RelayCommand]
    public void ToggleFilter() => IsFilterOpen = !IsFilterOpen;

    /// <summary>
    /// Takes one thing off the dial, or puts it back.
    ///
    /// The only way a finished heart ever leaves this screen. See
    /// <see cref="CompassVisibility"/> for why the API cannot be asked instead.
    /// </summary>
    [RelayCommand]
    public void ToggleDismissed(TargetRow? row)
    {
        if (row is null) return;

        _visibility = _visibility.Dismissing(row.Target, !_visibility.IsDismissed(row.Target));

        Save();
        Sift();
    }

    /// <summary>Shows or hides a whole kind.</summary>
    [RelayCommand]
    public void ToggleKind(KindRow? kind)
    {
        if (kind is null) return;

        _visibility = _visibility.ShowingKind(kind.Kind, !_visibility.ShowsKind(kind.Kind));

        Save();
        Sift();
    }

    /// <summary>
    /// Brings back everything hidden by hand, on every map, for this character.
    ///
    /// Deliberately not limited to this map. Something dismissed by accident is invisible and
    /// there is nothing on the dial to undo it with, so the way out has to be reachable from
    /// wherever the player notices — which is usually a map away from where it happened.
    /// </summary>
    [RelayCommand]
    public void RestoreHidden()
    {
        _visibility = _visibility.RestoringAll();

        Save();
        Sift();
    }

    private void Save() => visibility.Write(_loadedCharacter ?? string.Empty, _visibility);

    /// <summary>
    /// Brings the switch list up to date, building it the first time.
    ///
    /// Every kind every time, including the ones this map has none of. A switch that disappears
    /// when its count is zero is a switch nobody can find again, and the count is already on the
    /// row to say so.
    ///
    /// Built before any map has loaded, not with the first one: the sheet can be opened while the
    /// screen still says "waiting for the bridge", and an empty filter there reads as a broken
    /// one. Only the counts need a map.
    /// </summary>
    private void BuildKinds()
    {
        if (KindRows.Count == 0)
            foreach (var kind in CompassVisibility.AllKinds)
                KindRows.Add(new KindRow(kind, CompassVisibility.Plural(kind)));

        foreach (var row in KindRows)
        {
            row.IsShown = _visibility.ShowsKind(row.Kind);
            row.Count = AllRows.Count(r => r.Target.Kind == row.Kind);
        }
    }

    /// <summary>
    /// Pours <see cref="AllRows"/> through the filter into <see cref="Rows"/>.
    ///
    /// One choke point on purpose: the dial is plotted from <see cref="Rows"/> every frame, so
    /// filtering here is the whole of what the marks obey and there is nowhere else for the two
    /// lists to drift apart.
    ///
    /// The chosen objective is never filtered out. Hiding a kind is a statement about the dial's
    /// clutter; walking towards one particular thing is an explicit instruction, and the second
    /// beats the first.
    /// </summary>
    private void Sift()
    {
        var wanted = new List<TargetRow>(AllRows.Count);

        var filtered = 0;

        foreach (var row in AllRows)
        {
            row.IsDismissed = _visibility.IsDismissed(row.Target);

            if (_visibility.Shows(row.Target) || ReferenceEquals(row.Target, _chosen))
                wanted.Add(row);
            else
                filtered++;
        }

        Reconcile(wanted);

        Filtered = filtered;

        foreach (var kind in KindRows) kind.IsShown = _visibility.ShowsKind(kind.Kind);
    }

    /// <summary>
    /// Moves <see cref="Rows"/> to match a list, one row at a time.
    ///
    /// Not Clear-then-refill, and this is the whole reason the method exists. Every caller of
    /// <see cref="Sift"/> is a tap inside the picker - HIDE on a row, a kind switch, choosing an
    /// objective - so the collection is re-poured while the CollectionView that is bound to it is
    /// part-way through dispatching that tap. Clearing raises a Reset, which on Android tells the
    /// RecyclerView its adapter emptied underneath the view holder it is still working with:
    /// "Inconsistency detected. Invalid view holder adapter position".
    ///
    /// Removing and inserting the rows that actually changed leaves every other view holder
    /// where it was, so the list stays consistent with its adapter throughout. It also stops the
    /// sheet flickering the whole list back in on every tap.
    /// </summary>
    private void Reconcile(List<TargetRow> wanted)
    {
        var keep = new HashSet<TargetRow>(wanted);

        for (var i = Rows.Count - 1; i >= 0; i--)
            if (!keep.Contains(Rows[i]))
                Rows.RemoveAt(i);

        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < Rows.Count && ReferenceEquals(Rows[i], wanted[i])) continue;

            var at = Rows.IndexOf(wanted[i]);

            if (at >= 0) Rows.Move(at, i);
            else Rows.Insert(i, wanted[i]);
        }
    }

    /// <summary>
    /// Puts the objective down without choosing another.
    ///
    /// The counterpart to picking one, and missing until now: once a target was chosen the only
    /// way to stop pointing at it was to point at something else. Walking somewhere for its own
    /// sake is a normal thing to do.
    /// </summary>
    [RelayCommand]
    public void ClearObjective() => Choose(null);

    /// <summary>
    /// Asks the API again what has been finished.
    ///
    /// Manual because the answer changes while the screen is open — a hero point taken thirty
    /// seconds ago should be able to leave the dial — and polling two endpoints on a timer to
    /// catch it would spend the rate limit on a question that is usually unchanged.
    /// </summary>
    [RelayCommand]
    public Task RefreshProgressAsync()
    {
        targets.ForgetProgress();

        return _last is { } state ? LoadAsync(state.MapId, state.CharacterName) : Task.CompletedTask;
    }

    /// <summary>
    /// Another screen chose the objective — today that means the map.
    ///
    /// On the UI thread because <see cref="Choose"/> re-pours a bound collection, and the caller
    /// is whatever screen raised it. The reference check stops the pair echoing: see
    /// <see cref="Objective.Choose"/>.
    /// </summary>
    private void OnObjectiveChanged(CompassTarget? target)
    {
        if (ReferenceEquals(target, _chosen)) return;

        mainThread.BeginInvokeOnMainThread(() => Choose(target));
    }

    /// <summary>
    /// Works out the port-then-walk advice for the objective, from the map that is loaded.
    ///
    /// AllRows rather than Rows: a waypoint the player has hidden from the dial is still the
    /// right place to port to. Hiding is about clutter on the instrument, not about where the
    /// map's waypoints are.
    ///
    /// Only meaningful while the objective's own map is the one in hand, since its waypoints are
    /// what <see cref="PortRoute"/> chooses from. Once the character walks away the answer is
    /// kept rather than recomputed: the objective has not moved and neither have the waypoints
    /// near it.
    /// </summary>
    private void PlanRoute()
    {
        _hop = _chosen is null ? null : PortRoute.To(_chosen, AllRows.Select(row => row.Target));
        _hopLine = _hop is null ? string.Empty : PortRoute.Line(_hop);

        // Only for a target that is where it says it is, on the map whose trails are in hand. A
        // world boss is placed at its map's middle, and a trail that happens to pass the middle of
        // a map leads nowhere in particular — see CompassTarget.Precise.
        _guide = _chosen is { Precise: true } chosen && chosen.MapId == _loadedMap
            ? TrailGuide.Find(_routes, chosen.X, chosen.Y, _last is { } here ? (here.PlayerX, here.PlayerY) : null)
            : null;

        _trailHop = _guide is { } found ? PortRoute.ToTrail(found, _chosen!, AllRows.Select(row => row.Target)) : null;
        _trailHopLine = _trailHop is null ? string.Empty : PortRoute.Line(_trailHop);

        _guideHeight = string.Empty;
        _guideLine = string.Empty;

        // To the PC, for Blish HUD to draw in the world — or to take down the last line.
        trails.Share(_guide, _chosen, _map, _last is { } me ? (me.PlayerX, me.PlayerY) : null);

        if (_guide is { } guide)
            journal.Write("compass", $"trail to {_chosen!.Name}: {guide.Route.Name}, {guide.Offset:0} off it");
    }

    private void Choose(CompassTarget? target)
    {
        _chosen = target;

        objective.Choose(target);

        // AllRows rather than Rows: the chosen one is exempt from the filter, so it can be a row
        // the visible list does not hold, and the mark has to come off it again either way.
        foreach (var row in AllRows) row.IsChosen = ReferenceEquals(row.Target, target);

        // Choosing something hidden by hand puts it back on the dial for as long as it is the
        // objective, which means the visible list has to be poured again.
        if (AllRows.Count > 0) Sift();

        if (target is null)
        {
            Relative = null;
            TargetLine = "No objective chosen";
            Instruction = string.Empty;
            DistanceLine = string.Empty;
            RouteLine = string.Empty;
            PrecisionWarning = null;
            _hop = null;
            _hopLine = string.Empty;
            _guide = null;
            _trailHop = null;
            _trailHopLine = string.Empty;

            trails.Share(null, null, null, null);

            return;
        }

        NameObjective();

        PlanRoute();

        PrecisionWarning = target.Precise
            ? null
            : $"Pointing at the middle of {target.MapName}. The API publishes no spawn " +
              "coordinate for a world boss, so this is the map, not the arena.";

        if (_started) Aim(_renderX, _renderY);

        Moved?.Invoke();
    }

    /// <summary>
    /// Names the objective, and says which map it is on once that stops being this one.
    ///
    /// Only when it differs. On its own map the name is enough and the map line above already
    /// says where that is; across a border the bare name of a landmark is exactly the thing a
    /// player cannot place.
    ///
    /// Assigned here rather than worked out in <see cref="Aim"/>, which runs at display rate: the
    /// answer only moves when the objective or the map does, and both are events.
    /// </summary>
    private void NameObjective()
    {
        if (_chosen is not { } target)
        {
            TargetLine = "No objective chosen";

            return;
        }

        TargetLine = _last is { } here && target.MapId != here.MapId
            ? $"{target.Name} · {target.MapName}"
            : target.Name;
    }

    /// <summary>Assigns only on a real change, and raises the notification by hand.</summary>
    private void Set(string name, string? current, string value, Action<string> assign)
    {
        if (current == value) return;

        assign(value);

        OnPropertyChanged(name);
    }

    protected override string DefaultEmptyMessage =>
        "Nothing on this map has coordinates the API will give.";
}
