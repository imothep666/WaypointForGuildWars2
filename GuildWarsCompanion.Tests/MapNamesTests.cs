using GuildWarsCompanion.Bridge.Protocol;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Turning the map id MumbleLink gives into the name the Guild Wars 2 API knows.
///
/// No request is made here. <see cref="MapNames"/> is handed whatever asks the API, so what is
/// under test is the part worth testing — how often it asks, and what it does with an answer it
/// does not like. The real one is <c>Gw2Maps</c>, which is a single <c>GetAsync</c> and not
/// compiled into this assembly for the same reason nothing else with a socket in it is.
/// </summary>
public class MapNamesTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The API, minus the API: answers from a table, remembers every id it was asked about, and
    /// can be made to fail or to hang on demand. A null in the table is a 404 — the endpoint
    /// answering that there is no such map.
    /// </summary>
    private sealed class FakeApi(params (int Id, string? Name)[] names)
    {
        private readonly Dictionary<int, string?> _names =
            names.ToDictionary(entry => entry.Id, entry => entry.Name);

        public List<int> Asked { get; } = [];

        /// <summary>Set to make the next request fail the way a missing network does.</summary>
        public Exception? Fails { get; set; }

        /// <summary>Set to leave the request outstanding until the test completes it.</summary>
        public TaskCompletionSource<string?>? Gate { get; set; }

        public MapNames Names() => new(Fetch);

        private Task<string?> Fetch(int mapId, CancellationToken ct)
        {
            Asked.Add(mapId);

            if (Gate is not null) return Gate.Task;
            if (Fails is not null) return Task.FromException<string?>(Fails);

            return Task.FromResult(_names.GetValueOrDefault(mapId));
        }
    }

    [Fact]
    public async Task A_map_id_becomes_a_map_name()
    {
        var maps = new FakeApi((28, "Wayfarer Foothills")).Names();

        await maps.ResolveAsync(28, Noon);

        Assert.Equal("Wayfarer Foothills", maps.Lookup(28, Noon));
    }

    [Fact]
    public async Task Surrounding_space_is_not_part_of_the_name()
    {
        var maps = new FakeApi((28, "  Wayfarer Foothills\n")).Names();

        await maps.ResolveAsync(28, Noon);

        Assert.Equal("Wayfarer Foothills", maps.Lookup(28, Noon));
    }

    [Fact]
    public async Task A_map_is_only_asked_about_once_however_often_it_is_looked_up()
    {
        // The poll comes round every two seconds for as long as somebody is playing, and map
        // names are static data — a second request could only return the same answer.
        var api = new FakeApi((28, "Wayfarer Foothills"));
        var maps = api.Names();

        await maps.ResolveAsync(28, Noon);
        await maps.ResolveAsync(28, Noon.AddSeconds(2));
        maps.Lookup(28, Noon.AddSeconds(4));
        maps.Lookup(28, Noon.AddHours(3));

        Assert.Equal([28], api.Asked);
    }

    [Fact]
    public async Task A_map_the_api_has_never_heard_of_is_not_asked_about_again()
    {
        // A 404 is an answer, and a final one. Retrying it would cost a request per poll for as
        // long as the game sat in whatever the id turned out to be.
        var api = new FakeApi((9999, null));
        var maps = api.Names();

        await maps.ResolveAsync(9999, Noon);
        await maps.ResolveAsync(9999, Noon.AddHours(1));

        Assert.Null(maps.Lookup(9999, Noon.AddHours(2)));
        Assert.Equal([9999], api.Asked);
    }

    [Fact]
    public async Task A_failed_request_is_tried_again_but_not_at_once()
    {
        var api = new FakeApi((28, "Wayfarer Foothills")) { Fails = new HttpRequestException("no network") };
        var maps = api.Names();

        await maps.ResolveAsync(28, Noon);
        Assert.Equal([28], api.Asked);

        // Still inside the backoff: a PC that woke up without a network must not spend the poll
        // loop on requests that cannot work.
        await maps.ResolveAsync(28, Noon + MapNames.RetryAfter - TimeSpan.FromSeconds(1));
        Assert.Equal([28], api.Asked);

        api.Fails = null;
        await maps.ResolveAsync(28, Noon + MapNames.RetryAfter);

        Assert.Equal([28, 28], api.Asked);
        Assert.Equal("Wayfarer Foothills", maps.Lookup(28, Noon + MapNames.RetryAfter));
    }

    [Fact]
    public async Task Character_select_is_not_a_map_to_ask_about()
    {
        // Zero is what the block holds before a character is loaded. It is not a map, and the
        // API has nothing to say about it.
        var api = new FakeApi();
        var maps = api.Names();

        await maps.ResolveAsync(0, Noon);
        maps.Lookup(0, Noon);
        await maps.ResolveAsync(-1, Noon);

        Assert.Empty(api.Asked);
    }

    [Fact]
    public async Task A_slow_request_does_not_become_several()
    {
        var api = new FakeApi { Gate = new TaskCompletionSource<string?>() };
        var maps = api.Names();

        var first = maps.ResolveAsync(28, Noon);
        var second = maps.ResolveAsync(28, Noon.AddSeconds(2));

        // One request outstanding, not one per poll while the API is unwell.
        Assert.Equal([28], api.Asked);
        Assert.Null(maps.Lookup(28, Noon.AddSeconds(4)));

        api.Gate.SetResult("Wayfarer Foothills");
        await Task.WhenAll(first, second);

        Assert.Equal("Wayfarer Foothills", maps.Lookup(28, Noon.AddSeconds(6)));
    }

    [Fact]
    public async Task Waiting_on_a_lookup_already_under_way_waits_for_its_answer()
    {
        // Found by running the real thing: the poll had dropped a request for this id, so an
        // awaited resolve saw one in flight and returned — reporting "not yet" about a name
        // that was seconds away. Joining the outstanding request is what makes awaiting mean
        // anything.
        var api = new FakeApi { Gate = new TaskCompletionSource<string?>() };
        var maps = api.Names();

        maps.Lookup(28, Noon);
        var joined = maps.ResolveAsync(28, Noon);

        Assert.False(joined.IsCompleted);

        api.Gate.SetResult("Wayfarer Foothills");
        await joined;

        Assert.Equal("Wayfarer Foothills", maps.Lookup(28, Noon));
        Assert.Equal([28], api.Asked);
    }

    // --- what the phone and the window are handed ---------------------------

    private static GameState InMap(int mapId) =>
        new(42, "Sweet Child O Night", 5, 58, 2, false, mapId, 1, 1, 0, UiState.GameHasFocus, 0, 0);

    [Fact]
    public void The_label_is_the_id_until_the_name_arrives()
    {
        // The lookup happens off the poll thread, so the first report after a map change has the
        // id and not yet the name. Saying "Map 28" for a moment beats making the window wait.
        var waiting = new GameReport(InMap(28), true, "Reading Guild Wars 2 now.");
        var named = waiting with { MapName = "Wayfarer Foothills" };

        Assert.Equal("Map 28", waiting.MapLabel);
        Assert.Equal("Wayfarer Foothills", named.MapLabel);
    }

    [Fact]
    public void With_no_game_there_is_no_map_to_name()
    {
        Assert.Equal(string.Empty, GameReport.Nothing("Guild Wars 2 is not running").MapLabel);
    }
}
