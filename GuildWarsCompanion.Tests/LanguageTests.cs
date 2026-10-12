using System.Globalization;
using System.Net;
using System.Text;

using GuildWarsCompanion.Bridge.Protocol;
using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;

using Microsoft.Data.Sqlite;

using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The API answering in the phone's language, and the cache keeping up with it.
///
/// What matters most is the search: somebody playing in German who types the English name from a
/// guide or the wiki has to land on the item, shown with its German name.
/// </summary>
public sealed class LanguageTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"gw2-language-{Guid.NewGuid():N}.db3");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    [Theory]
    [InlineData("de-AT", "de")]
    [InlineData("de-DE", "de")]
    [InlineData("fr-CA", "fr")]
    [InlineData("es-MX", "es")]
    [InlineData("zh-CN", "zh")]
    [InlineData("en-GB", "en")]
    [InlineData("ja-JP", "en")]
    [InlineData("ko-KR", "en")]
    public void The_device_language_is_used_when_the_API_speaks_it(string culture, string expected) =>
        Assert.Equal(expected, Gw2Language.For(new CultureInfo(culture)));

    [Fact]
    public void An_invariant_culture_asks_for_English() =>
        Assert.Equal("en", Gw2Language.For(CultureInfo.InvariantCulture));

    [Fact]
    public async Task Every_request_names_the_language()
    {
        var asked = new List<string>();
        var client = Client(asked, _ => "[]");
        client.Language = "fr";

        await client.GetItemsAsync([19721]);
        await client.GetEnglishItemsAsync([19721]);

        Assert.Contains("lang=fr", asked[0]);
        Assert.Contains("lang=en", asked[1]);
    }

    [Fact]
    public async Task An_English_name_finds_the_item_and_it_comes_back_German()
    {
        var cache = new Gw2Cache(_path, "de");

        await cache.SaveItemsAsync([new Item(19721, "Ektoplasmakugel", null, "Exotic")]);
        await cache.SaveEnglishItemNamesAsync([new Item(19721, "Glob of Ectoplasm", null, "Exotic")]);

        var byEnglish = Assert.Single(await cache.SearchItemsAsync("ecto"));
        var byGerman = Assert.Single(await cache.SearchItemsAsync("ekto"));

        Assert.Equal("Ektoplasmakugel", byEnglish.Name);
        Assert.Equal("Ektoplasmakugel", byGerman.Name);
    }

    [Fact]
    public async Task Storing_an_item_again_keeps_its_English_name()
    {
        var cache = new Gw2Cache(_path, "de");

        await cache.SaveItemsAsync([new Item(19721, "Ektoplasmakugel", null, "Exotic")]);
        await cache.SaveEnglishItemNamesAsync([new Item(19721, "Glob of Ectoplasm", null, "Exotic")]);
        await cache.SaveItemsAsync([new Item(19721, "Ektoplasmakugel", "icon", "Exotic")]);

        Assert.Single(await cache.SearchItemsAsync("glob of"));
        Assert.Contains(19721, await cache.GetItemIdsWithEnglishAsync());
    }

    [Fact]
    public async Task A_store_from_before_languages_keeps_its_names_as_the_English_ones()
    {
        OldStore(("items", "19721, 'Glob of Ectoplasm', NULL, 'Exotic'"));

        var cache = new Gw2Cache(_path, "de");

        // Still searchable, and still shown offline, but due to be renamed.
        Assert.Single(await cache.SearchItemsAsync("ecto"));
        Assert.Equal("Glob of Ectoplasm", (await cache.GetItemsAsync([19721]))[19721].Name);
        Assert.Empty(await cache.GetItemsAsync([19721], currentOnly: true));
        Assert.Empty(await cache.GetKnownItemIdsAsync());
        Assert.Equal(1, await cache.CountItemsInOtherLanguageAsync());

        // Its English name is not fetched again.
        Assert.Contains(19721, await cache.GetItemIdsWithEnglishAsync());
    }

    [Fact]
    public async Task A_store_from_before_languages_opened_in_English_is_current()
    {
        OldStore(("items", "19721, 'Glob of Ectoplasm', NULL, 'Exotic'"));

        var cache = new Gw2Cache(_path, "en");

        Assert.Contains(19721, await cache.GetKnownItemIdsAsync());
        Assert.Equal(0, await cache.CountItemsInOtherLanguageAsync());
    }

    [Fact]
    public async Task Maps_and_currencies_are_dropped_when_the_language_changes_and_only_then()
    {
        await new Gw2Cache(_path, "en").SaveCurrenciesAsync([new Currency(1, "Coin", "Money", 1, "icon")]);

        Assert.Single(await new Gw2Cache(_path, "en").GetCurrenciesAsync());
        Assert.Empty(await new Gw2Cache(_path, "de").GetCurrenciesAsync());

        await new Gw2Cache(_path, "de").SaveCurrenciesAsync([new Currency(1, "Münze", "Geld", 1, "icon")]);

        Assert.Equal("Münze", Assert.Single(await new Gw2Cache(_path, "de").GetCurrenciesAsync()).Name);
    }

    [Fact]
    public async Task A_lookup_renames_an_item_stored_in_another_language()
    {
        var english = new Gw2Cache(_path, "en");
        await english.SaveItemsAsync([new Item(19721, "Glob of Ectoplasm", null, "Exotic")]);

        var asked = new List<string>();
        var client = Client(asked, _ => """[{ "id": 19721, "name": "Ektoplasmakugel", "rarity": "Exotic" }]""");
        client.Language = "de";

        var found = await new ItemLookup(client, new Gw2Cache(_path, "de")).ResolveAsync([19721]);

        Assert.Equal("Ektoplasmakugel", found[19721].Name);

        // It was held already, with its English name: one request, in German.
        Assert.Single(asked);
        Assert.Contains("lang=de", asked[0]);
    }

    [Fact]
    public async Task Offline_a_lookup_falls_back_to_the_earlier_language()
    {
        await new Gw2Cache(_path, "en").SaveItemsAsync([new Item(19721, "Glob of Ectoplasm", null, "Exotic")]);

        var client = Client([], _ => throw new HttpRequestException("offline"));
        client.Language = "de";

        var found = await new ItemLookup(client, new Gw2Cache(_path, "de")).ResolveAsync([19721]);

        Assert.Equal("Glob of Ectoplasm", found[19721].Name);
    }

    [Fact]
    public async Task An_item_seen_for_the_first_time_gets_its_English_name_too()
    {
        var client = Client([], path => path.Contains("lang=en")
            ? """[{ "id": 19721, "name": "Glob of Ectoplasm", "rarity": "Exotic" }]"""
            : """[{ "id": 19721, "name": "Ektoplasmakugel", "rarity": "Exotic" }]""");
        client.Language = "de";

        var cache = new Gw2Cache(_path, "de");
        await new ItemLookup(client, cache).ResolveAsync([19721]);

        Assert.Equal("Ektoplasmakugel", Assert.Single(await cache.SearchItemsAsync("glob")).Name);
    }

    [Fact]
    public async Task Mastery_points_are_asked_for_in_English_whatever_the_language()
    {
        var asked = new List<string>();
        var client = Client(asked, _ => """{ "totals": [], "unlocked": [] }""");
        client.Language = "de";

        await client.GetMasteryPointsAsync();

        Assert.Contains("lang=en", Assert.Single(asked));
    }

    [Fact]
    public void A_German_name_goes_to_the_German_wiki_and_a_Chinese_one_to_the_English()
    {
        Assert.Equal(
            "https://wiki-de.guildwars2.com/index.php?search=Ektoplasmakugel",
            Wiki.UrlFor("Ektoplasmakugel", "de"));
        Assert.StartsWith("https://wiki.guildwars2.com/", Wiki.UrlFor("球形灵质", "zh"));
    }

    [Fact]
    public void A_featured_reward_asks_the_wiki_for_its_banner_in_English()
    {
        var row = new VaultShopRow(1, "Mantel des Wardjägers Skin", null, 1, 100, null, null, true,
            Featured: true, EnglishName: "Ward Hunter's Coat Skin");

        Assert.Equal("Ward Hunter's Coat Skin", row.BannerName);
    }

    [Fact]
    public async Task The_bridge_map_name_is_shown_in_the_phone_language_and_kept_in_English()
    {
        var client = Client([], _ => """{ "id": 15, "name": "Königintal", "region_id": 4, "continent_id": 1, "default_floor": 1 }""");
        client.Language = "de";

        var report = new GameReport(Playing(15), true, string.Empty, "Queensdale");
        var named = await new LocalMapNames(client, new Gw2Cache(_path, "de")).NameAsync(report);

        Assert.Equal("Königintal", named.MapLabel);
        Assert.Equal("Queensdale", named.MapName);
    }

    [Fact]
    public async Task Offline_the_bridge_map_name_stands()
    {
        var client = Client([], _ => throw new HttpRequestException("offline"));
        client.Language = "de";

        var report = new GameReport(Playing(15), true, string.Empty, "Queensdale");
        var named = await new LocalMapNames(client, new Gw2Cache(_path, "de")).NameAsync(report);

        Assert.Equal("Queensdale", named.MapLabel);
    }

    private static GameState Playing(int mapId) =>
        new(42, "Imothep", 5, 58, 2, false, mapId, 1, 1, 0, UiState.GameHasFocus, 0, 0);

    /// <summary>A cache file as the app wrote it before it asked for a language.</summary>
    private void OldStore(params (string Table, string Values)[] rows)
    {
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT NOT NULL, icon TEXT, rarity TEXT);" +
            string.Concat(rows.Select(row => $"INSERT INTO {row.Table} VALUES ({row.Values});"));
        command.ExecuteNonQuery();
    }

    private static Gw2Client Client(List<string> asked, Func<string, string> answer) =>
        new(new HttpClient(new Scripted(asked, answer)) { BaseAddress = new Uri(Gw2Client.BaseUrl) });

    private sealed class Scripted(List<string> asked, Func<string, string> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery;
            asked.Add(path);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(answer(path), Encoding.UTF8, "application/json")
            });
        }
    }
}
