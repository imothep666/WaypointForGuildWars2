using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Host;
using GuildWarsCompanion.Services;
using GuildWarsCompanion.Services.Alerts;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// An alert found while somebody plays goes to the game, through the PC's Blish HUD module, and
/// only to the phone when the game did not take it.
/// </summary>
public class AlertsInGameTests : IDisposable
{
    private static readonly AccountAlert Price = new("price:19976", "Mystic Coin is at 1g 20s", "Your watch: at or under 1g 25s");

    private readonly string _database = Path.Combine(Path.GetTempPath(), $"gw2-alerts-{Guid.NewGuid():N}.db3");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_database);
    }

    private (AlertService Alerts, Shade Shade, FakeNetwork Network) Build(bool playing, int? notifyStatus)
    {
        var secret = BridgeProtocol.NewPairingSecret();
        var desk = new FakePc("DESKID", "Desktop", secret, "192.168.1.20:47623")
        {
            Playing = playing,
            Character = playing ? "Imothep" : null,
            NotifyStatus = notifyStatus
        };
        var network = new FakeNetwork().With(desk);
        var known = new KnownBridges(new MemoryBridgeStore());
        var journal = new Journal(Path.Combine(Path.GetTempPath(), "gw2-bridge-tests.log"));

        known.PairAsync(desk.Card).GetAwaiter().GetResult();

        var settings = new MemorySettings();
        var shade = new Shade();
        var alerts = new AlertService(
            [new OneAlert(Price)],
            new AlertStore(_database),
            new ApiKeySession(new Gw2Client(new HttpClient()), new NoSecrets()),
            shade,
            new NotifySchedule(settings),
            new BridgeClient(network, new BridgeFinder(network, network, known, journal), journal),
            journal,
            new TestEvidence(settings));

        return (alerts, shade, network);
    }

    [Fact]
    public void Shown_in_the_game_it_is_not_said_on_the_phone_too()
    {
        var (alerts, shade, _) = Build(playing: true, notifyStatus: 204);

        var run = alerts.CheckAndNotifyAsync().GetAwaiter().GetResult();

        Assert.Empty(shade.Shown);
        Assert.Equal((1, 1), (run.Sent, run.InGame));
    }

    [Fact]
    public void With_no_module_to_show_it_the_phone_says_it()
    {
        var (alerts, shade, _) = Build(playing: true, notifyStatus: 409);

        var run = alerts.CheckAndNotifyAsync().GetAwaiter().GetResult();

        Assert.Equal([Price.Key], shade.Shown.Select(alert => alert.Key));
        Assert.Equal(0, run.InGame);
    }

    [Fact]
    public void A_bridge_from_before_alerts_leaves_it_to_the_phone()
    {
        var (alerts, shade, _) = Build(playing: true, notifyStatus: null);

        alerts.CheckAndNotifyAsync().GetAwaiter().GetResult();

        Assert.Single(shade.Shown);
    }

    [Fact]
    public void Nobody_playing_means_the_game_is_not_asked()
    {
        var (alerts, shade, network) = Build(playing: false, notifyStatus: 204);

        alerts.CheckAndNotifyAsync().GetAwaiter().GetResult();

        Assert.Single(shade.Shown);
        lock (network.Asked) Assert.DoesNotContain(network.Asked, url => url.EndsWith(BridgeProtocol.NotifyPath));
    }

    private sealed class OneAlert(AccountAlert alert) : IAccountWatcher
    {
        public string Name => "prices";

        public IReadOnlyList<string> RequiredPermissions => [];

        public bool NeedsBaseline => false;

        public Task<IReadOnlyList<AccountAlert>> CheckAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AccountAlert>>([alert]);
    }

    private sealed class Shade : INotifier
    {
        public List<AccountAlert> Shown { get; } = [];

        public Task<bool> RequestPermissionAsync() => Task.FromResult(true);

        public Task NotifyAsync(AccountAlert alert, CancellationToken ct = default)
        {
            Shown.Add(alert);
            return Task.CompletedTask;
        }
    }

    private sealed class NoSecrets : ISecrets
    {
        public Task<string?> GetAsync(string key) => Task.FromResult<string?>(null);

        public Task SetAsync(string key, string value) => Task.CompletedTask;

        public bool Remove(string key) => false;
    }

    private sealed class MemorySettings : ISettings
    {
        private readonly Dictionary<string, object?> _values = [];

        public T Get<T>(string key, T defaultValue) =>
            _values.TryGetValue(key, out var value) && value is T typed ? typed : defaultValue;

        public void Set<T>(string key, T value) => _values[key] = value;

        public void Remove(string key) => _values.Remove(key);
    }
}

/// <summary>The alert's way from the phone to the module, across the bridge.</summary>
public class NotifyWireTests
{
    [Fact]
    public void The_body_is_the_title_and_then_the_line_under_it()
    {
        var alert = BridgeProtocol.ParseNotify(BridgeProtocol.NotifyBody("Shadow Behemoth in 5 minutes", "Queensdale"))!.Value;

        Assert.Equal(("Shadow Behemoth in 5 minutes", "Queensdale"), (alert.Title, alert.Detail));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \nonly a body")]
    public void An_alert_needs_a_title(string? body) => Assert.Null(BridgeProtocol.ParseNotify(body));

    [Fact]
    public void A_body_too_long_to_be_an_alert_is_refused() =>
        Assert.Null(BridgeProtocol.ParseNotify("Title\n" + new string('x', BridgeProtocol.LongestNotify)));

    [Fact]
    public void The_module_says_whether_it_shows_alerts()
    {
        Assert.Equal("on", ModuleWire.Parse(ModuleWire.Alerts(true))!.Text);
        Assert.Equal("off", ModuleWire.Parse(ModuleWire.Alerts(false))!.Text);
        Assert.Equal(ModuleLineKind.Alerts, ModuleWire.Parse(ModuleWire.Alerts(true))!.Kind);
        Assert.Null(ModuleWire.Parse("alerts maybe"));
    }
}
