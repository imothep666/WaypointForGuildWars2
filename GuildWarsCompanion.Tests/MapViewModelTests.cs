using System.Net;
using System.Text;

using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.ViewModels;

using Microsoft.Maui.Graphics;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The map screen's own half: starting a load, moving the centre, and telling the canvas to draw.
///
/// Everything pure was taken out of <see cref="MapViewModel"/> long ago and tested where it went;
/// what stayed is the wiring between those parts, which was checked on a phone and nowhere else.
/// Here the whole of it runs with no network and no PC. Queensdale is stored in a fresh cache
/// before each test, so a load reads disk the way a phone offline does; the API answers 404 to
/// anything, so a map that is not stored cannot be opened. The bridge is the test itself, calling
/// <see cref="MapViewModel.Apply"/> with reports the way the stream would.
///
/// Tyria's reference zoom is 7, where a continent unit is a pixel. Queensdale is 3584 by 2432
/// units, so on a 1080 by 1920 portrait screen the whole map fits at zoom 5, four units a pixel.
/// </summary>
public sealed class MapViewModelTests : IDisposable
{
    private const int Queensdale = 15;
    private const int Frostgorge = 30;
    private const int Nowhere = 99;
    private const int MistlockSanctuary = 1206;

    private const double Width = 1080;
    private const double Height = 1920;
    private const double Density = 3;

    private static readonly MapInfo QueensdaleMap = new(
        Queensdale, "Queensdale", 4, 1, 1, [[42624, 28032], [46208, 30464]], Type: "Public");

    /// <summary>The middle of Queensdale, where a map with nobody on it is centred.</summary>
    private const double MiddleX = 44416;
    private const double MiddleY = 29248;

    private static readonly CompassTarget Waypoint =
        new("Shaemoor Waypoint", TargetKind.Waypoint, Queensdale, "Queensdale", 44000, 29000);

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "gw2-mapvm-tests-" + Guid.NewGuid().ToString("N"));

    private readonly Api _api = new();
    private readonly MemoryChoices _choices = new();
    private readonly Objective _objective = new();
    private readonly Gw2Cache _cache;
    private readonly MapViewModel _map;

    public MapViewModelTests()
    {
        Directory.CreateDirectory(_folder);

        var environment = new AppEnvironment(_folder, _folder, "test");
        var journal = new Journal(Path.Combine(_folder, "journal.log"));
        var client = new Gw2Client(new HttpClient(_api) { BaseAddress = new Uri(Gw2Client.BaseUrl) })
        {
            Language = Gw2Language.English
        };
        var cache = _cache = new Gw2Cache(Path.Combine(_folder, "cache.db"));

        cache.SaveMapsAsync([QueensdaleMap]).GetAwaiter().GetResult();
        cache.SaveContinentAsync(new ContinentInfo(1, "Tyria", [81920, 114688], 0, 8)).GetAwaiter().GetResult();
        cache.SaveMapDetailAsync(Queensdale, new MapDetail(
            new Dictionary<string, MapTask>
            {
                ["1"] = new(1, "Help Farmer Diah", 1, [45000, 29500])
            },
            new Dictionary<string, MapPoi>
            {
                ["1"] = new(1, Waypoint.Name, "waypoint", [Waypoint.X, Waypoint.Y])
            },
            [])).GetAwaiter().GetResult();

        // No PC paired, so the stream ends at once and the tests are the only bridge.
        var network = new FakeNetwork();
        var finder = new BridgeFinder(network, network, new KnownBridges(new MemoryBridgeStore()), journal);
        var markers = new MarkerStore(environment);

        _map = new MapViewModel(
            new GameStream(new DefaultStreamBinding(), finder, journal, new LocalMapNames(client, cache)),
            new CompassTargets(client, markers, cache), client, cache,
            new Gw2Tiles(new Decoder(), environment, _api, _ => Task.CompletedTask),
            new MapIcons(client, new Decoder(), environment), _objective, journal, markers,
            new TrailShare(new BridgeClient(network, finder, journal)), _choices,
            new MapCatalogue(client, cache), new Navigator(), new Inline());
    }

    public void Dispose()
    {
        _map.Stop();

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // SQLite's pool can still hold the file for a moment; the temp folder will do.
        }
    }

    // --- starting a load -----------------------------------------------------

    [Fact]
    public async Task A_map_picked_by_name_opens_with_no_game_and_no_network()
    {
        await PinnedAsync();

        Assert.True(_map.IsPinned);
        Assert.Equal("Queensdale", _map.MapLine);
        Assert.Contains(_map.Marks, mark => mark.Name == Waypoint.Name);
        Assert.Null(_map.Player);
        Assert.Equal((MiddleX, MiddleY), (_map.CentreX, _map.CentreY));
    }

    /// <summary>The layout and the map arrive in either order, and the second to arrive fits.</summary>
    [Fact]
    public async Task A_map_that_loads_before_the_screen_is_measured_is_fitted_when_it_is()
    {
        _map.Pin(Queensdale, "Queensdale");
        _map.Start();
        await LoadedAsync();

        Assert.Equal(6, _map.Zoom);

        _map.Fit(Width, Height);

        Assert.Equal(5, _map.Zoom);
        Assert.Equal(1, _map.Level);
        Assert.Equal(3, _map.Levels);
    }

    [Fact]
    public async Task The_character_walking_into_a_map_loads_it()
    {
        await FollowingAsync();

        Assert.Equal("Queensdale", _map.MapLine);
        Assert.Equal((44500.0, 29300.0), _map.Player);
        Assert.Equal(5, _map.Zoom);
    }

    [Fact]
    public async Task A_pinned_map_stays_when_the_character_is_somewhere_else()
    {
        await PinnedAsync();
        var asked = _api.Count;

        _map.Apply(Playing(Frostgorge, 1000, 1000));
        await Task.Delay(200);

        Assert.Equal("Queensdale", _map.MapLine);
        Assert.Null(_map.Player);
        Assert.Equal(asked, _api.Count);
    }

    [Fact]
    public async Task The_character_walking_into_the_pinned_map_is_shown_on_it()
    {
        await PinnedAsync();

        _map.Apply(Playing(Queensdale, 44500, 29300));

        Assert.Equal((44500.0, 29300.0), _map.Player);
    }

    /// <summary>Letting go of the pin hands the screen back to the game at the next report.</summary>
    [Fact]
    public async Task Following_again_loads_the_map_the_character_is_on()
    {
        await PinnedAsync();

        _map.Follow();
        _map.Apply(Playing(Nowhere, 1000, 1000));
        await ErrorAsync();

        Assert.False(_map.IsPinned);
        Assert.Contains(_api.Asked, path => path.Contains($"maps/{Nowhere}") || path.Contains($"ids={Nowhere}"));
    }

    /// <summary>
    /// A pin to a map that cannot be opened leaves the old one in place, named as it was: the
    /// tiles go on showing Queensdale, so the line has to go on saying it.
    /// </summary>
    [Fact]
    public async Task A_picked_map_that_cannot_open_puts_the_last_one_back()
    {
        await PinnedAsync();

        _map.Pin(Nowhere, "Nowhere");
        await ErrorAsync();

        Assert.True(_map.IsPinned);
        Assert.Equal("Queensdale", _map.MapLine);
        Assert.Contains(_map.Marks, mark => mark.Name == Waypoint.Name);
    }

    /// <summary>
    /// Mistlock Sanctuary offline, on a phone that has never stored the Mists' continent. Its map
    /// row is stored, so the load got as far as setting the continent before it failed, and the
    /// screen went blank: Queensdale's name and pin over tiles asked for on the Mists. Seen on the
    /// phone on 2026-10-13.
    /// </summary>
    [Fact]
    public async Task A_map_on_a_continent_not_stored_leaves_the_last_map_drawable()
    {
        await _cache.SaveMapsAsync([new MapInfo(
            MistlockSanctuary, "Mistlock Sanctuary", 37, 2, 1, [[16000, 16000], [17000, 17000]], Type: "Public")]);
        await PinnedAsync();
        var frames = Frames();

        _map.Pin(MistlockSanctuary, "Mistlock Sanctuary");
        await ErrorAsync();

        Assert.Equal("Queensdale", _map.MapLine);
        Assert.Equal((1, 1, 7), (_map.ContinentId, _map.Floor, _map.Reference));
        Assert.Equal(new TileKey(1, 1, 5, 2, 3), _map.Key(2, 3));
        Assert.True(frames() > 0, "nothing asked for Queensdale's tiles again");
    }

    /// <summary>A failed load used to be asked for again on every report, ten a second.</summary>
    [Fact]
    public async Task A_map_that_cannot_open_is_not_asked_for_on_every_report()
    {
        _map.Fit(Width, Height);
        _map.Apply(Playing(Nowhere, 1000, 1000));
        await ErrorAsync();
        var asked = _api.Count;

        _map.Apply(Playing(Nowhere, 1000, 1000));
        _map.Apply(Playing(Nowhere, 1000, 1000));
        await Task.Delay(200);

        Assert.Equal(asked, _api.Count);
    }

    // --- moving the centre ---------------------------------------------------

    [Fact]
    public async Task A_drag_moves_the_map_under_the_finger()
    {
        await PinnedAsync();

        _map.Pan(100, -50);

        // Four units a pixel at zoom 5; the map moves with the finger, so the middle moves against it.
        Assert.Equal((MiddleX - 400, MiddleY + 200), (_map.CentreX, _map.CentreY));
    }

    [Fact]
    public async Task A_drag_stops_at_the_edge_of_the_map()
    {
        await PinnedAsync();

        _map.Pan(1_000_000, -1_000_000);

        Assert.Equal((42624.0, 30464.0), (_map.CentreX, _map.CentreY));
    }

    [Fact]
    public async Task A_character_leaving_the_middle_is_glided_after()
    {
        await FollowingAsync();

        _map.Apply(Playing(Queensdale, 45900, 29300));

        Assert.True(_map.IsMoving);

        Run();

        Assert.Equal((45900.0, 29300.0), (_map.CentreX, _map.CentreY));
    }

    [Fact]
    public async Task A_character_inside_the_middle_moves_nothing()
    {
        await FollowingAsync();

        _map.Apply(Playing(Queensdale, 44600, 29350));

        Assert.False(_map.IsMoving);
        Assert.Equal((44500.0, 29300.0), (_map.CentreX, _map.CentreY));
    }

    /// <summary>A drag says "this is what I am looking at"; the character walking off does not undo it.</summary>
    [Fact]
    public async Task After_a_drag_the_map_no_longer_follows_until_recentred()
    {
        await FollowingAsync();

        _map.Pan(100, 0);
        var dragged = (_map.CentreX, _map.CentreY);

        _map.Apply(Playing(Queensdale, 45900, 29300));

        Assert.False(_map.IsMoving);
        Assert.Equal(dragged, (_map.CentreX, _map.CentreY));

        _map.Recentre();

        Assert.Equal((45900.0, 29300.0), (_map.CentreX, _map.CentreY));
    }

    [Fact]
    public async Task Recentring_with_nobody_on_the_map_goes_to_its_middle()
    {
        await PinnedAsync();

        _map.Pan(100, 100);
        _map.Recentre();

        Assert.Equal((MiddleX, MiddleY), (_map.CentreX, _map.CentreY));
    }

    // --- zooming -------------------------------------------------------------

    [Fact]
    public async Task The_zoom_stops_at_the_whole_map_and_at_the_deepest_level()
    {
        await PinnedAsync();

        Assert.False(_map.CanZoomOut);
        Assert.False(_map.ZoomBy(-1, 0, 0, Width, Height));

        Assert.True(_map.ZoomBy(5, 0, 0, Width, Height));
        Assert.Equal(7, _map.Zoom);
        Assert.False(_map.CanZoomIn);
    }

    /// <summary>The level being left is kept underneath, so a tile not yet fetched is a blur and not a hole.</summary>
    [Fact]
    public async Task A_zoom_eases_over_the_last_level()
    {
        await PinnedAsync();

        Assert.True(_map.ZoomBy(1, 0, 0, Width, Height, aboutCentre: true));

        Assert.Equal(6, _map.Zoom);
        Assert.Equal(5, _map.Underlay);
        Assert.True(_map.IsMoving);

        Run();

        Assert.False(_map.IsMoving);
        Assert.Equal(1, _map.Scale);
        Assert.Equal((MiddleX, MiddleY), (_map.CentreX, _map.CentreY));
    }

    /// <summary>A pinch holds the point between the fingers still, and the middle moves to do it.</summary>
    [Fact]
    public async Task A_pinch_holds_the_point_under_the_fingers()
    {
        await PinnedAsync();
        var held = Under(Width / 2 + 200, Height / 2);

        _map.ZoomBy(1, Width / 2 + 200, Height / 2, Width, Height);
        Run();

        Assert.Equal(held, Under(Width / 2 + 200, Height / 2));
    }

    // --- taps ----------------------------------------------------------------

    [Fact]
    public async Task A_tap_near_a_marker_makes_it_the_objective()
    {
        await PinnedAsync();
        var at = _map.ToView(Waypoint.X, Waypoint.Y, Width, Height);

        _map.TapAt(at.X + 30, at.Y - 20, Width, Height, Density);

        Assert.Equal(Waypoint.Name, _objective.Chosen?.Name);
        Assert.Equal(Waypoint.Name, _map.ObjectiveLine);
        Assert.True(_map.HasObjective);
    }

    /// <summary>Panning with a finger held a little too still reads as a tap, and must not cost the objective.</summary>
    [Fact]
    public async Task A_tap_on_empty_ground_keeps_the_objective()
    {
        await PinnedAsync();
        _objective.Choose(Waypoint);

        _map.TapAt(100, 100, Width, Height, Density);

        Assert.Equal(Waypoint, _objective.Chosen);
    }

    [Fact]
    public async Task A_tap_on_the_pad_zooms_about_the_middle()
    {
        await PinnedAsync();
        var plus = ZoomPad.Plus(Width, Height, Density);

        _map.TapAt(plus.CentreX, plus.CentreY, Width, Height, Density);
        Run();

        Assert.Equal(6, _map.Zoom);
        Assert.Null(_objective.Chosen);
        Assert.Equal((MiddleX, MiddleY), (_map.CentreX, _map.CentreY));
    }

    [Fact]
    public async Task An_objective_on_another_map_is_named_rather_than_measured()
    {
        await FollowingAsync();

        _objective.Choose(new CompassTarget(
            "Frostgorge Sound waypoint", TargetKind.Waypoint, Frostgorge, "Frostgorge Sound", 1, 1));
        _map.Apply(Playing(Queensdale, 44500, 29300));

        Assert.Contains("Frostgorge Sound", _map.ObjectiveDetail);
    }

    // --- asking for a frame --------------------------------------------------

    /// <summary>Ten reports a second from a character standing still are ten frames nobody can see.</summary>
    [Fact]
    public async Task A_report_that_moves_nothing_draws_nothing()
    {
        await FollowingAsync();
        var frames = Frames();

        _map.Apply(Playing(Queensdale, 44500, 29300));
        _map.Apply(Playing(Queensdale, 44501, 29300));

        Assert.Equal(0, frames());

        // Four units is a pixel at zoom 5.
        _map.Apply(Playing(Queensdale, 44510, 29300));

        Assert.Equal(1, frames());
    }

    [Fact]
    public async Task Choosing_a_route_category_is_remembered_and_drawn()
    {
        await PinnedAsync();
        var frames = Frames();

        _map.ToggleRoute("tw.trails.main");

        Assert.Contains("tw.trails.main", _choices.Saved);
        Assert.Equal(1, frames());
    }

    // --- helpers -------------------------------------------------------------

    /// <summary>Queensdale picked by name on a measured screen, loaded.</summary>
    private async Task PinnedAsync()
    {
        _map.Pin(Queensdale, "Queensdale");
        _map.Start();
        _map.Fit(Width, Height);
        await LoadedAsync();
    }

    /// <summary>A character standing in Queensdale, followed, with the map centred on them.</summary>
    private async Task FollowingAsync()
    {
        _map.Fit(Width, Height);
        _map.Apply(Playing(Queensdale, 44500, 29300));
        await LoadedAsync();

        _map.Apply(Playing(Queensdale, 44500, 29300));
        _map.Recentre();
    }

    private async Task LoadedAsync()
    {
        await Until(() => !_map.IsBusy && _map.Marks.Count > 0);

        Assert.False(_map.HasError, _map.ErrorMessage);
    }

    private Task ErrorAsync() => Until(() => !_map.IsBusy && _map.HasError);

    private static async Task Until(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (!done())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("the map never got there");

            await Task.Delay(10);
        }
    }

    /// <summary>Lets any zoom or glide run to its end.</summary>
    private void Run()
    {
        for (var frame = 0; frame < 200 && _map.IsMoving; frame++) _map.Advance(0.05);
    }

    /// <summary>The continent point under a place on the screen.</summary>
    private (double X, double Y) Under(double x, double y)
    {
        var units = TileGeometry.UnitsPerPixel(_map.Zoom, _map.Reference) / _map.Scale;

        return (Math.Round(_map.CentreX + (x - Width / 2) * units, 6),
            Math.Round(_map.CentreY + (y - Height / 2) * units, 6));
    }

    private Func<int> Frames()
    {
        var count = 0;

        _map.Changed += () => count++;

        return () => count;
    }

    private static GameReport Playing(int mapId, float x, float y) =>
        new(new GameState(1200, "Imothep", 4, 0, 3, false, mapId, 1, 1, 0, UiState.GameHasFocus, x, y),
            IsLive: true, Note: "playing", MapName: mapId == Queensdale ? "Queensdale" : null);

    /// <summary>The API and the tile service with no network: every request is counted and
    /// answered 404, which is what an unknown map id gets.</summary>
    private sealed class Api : HttpMessageHandler
    {
        private readonly List<string> _asked = [];

        public IReadOnlyList<string> Asked
        {
            get
            {
                lock (_asked) return [.. _asked];
            }
        }

        public int Count => Asked.Count;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (_asked) _asked.Add(request.RequestUri!.PathAndQuery);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{ "text": "no such id" }""", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class MemoryChoices : IRouteChoiceStore
    {
        public IReadOnlySet<string> Saved { get; private set; } = new HashSet<string>();

        public IReadOnlySet<string> Read() => Saved;

        public void Write(IReadOnlySet<string> chosen) => Saved = chosen;
    }

    private sealed class Navigator : INavigator
    {
        public Task GoToAsync(string route) => Task.CompletedTask;

        public Task AlertAsync(string title, string message, string close) => Task.CompletedTask;
    }

    /// <summary>The test thread is the UI thread.</summary>
    private sealed class Inline : IMainThread
    {
        public void BeginInvokeOnMainThread(Action action) => action();

        public Task InvokeOnMainThreadAsync(Action action)
        {
            action();

            return Task.CompletedTask;
        }

        public Task InvokeOnMainThreadAsync(Func<Task> action) => action();
    }

    private sealed class Decoder : IImageDecoder
    {
        public IImage? Decode(Stream stream) => new BlankImage();
    }
}
