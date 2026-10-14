using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;

using GuildWarsCompanion.Services;

namespace GuildWarsCompanion.Gw2;

/// <summary>Raised for anything the API tells us went wrong, mapped to an actionable reason.</summary>
public sealed class Gw2ApiException(string message, Gw2ErrorKind kind) : Exception(message)
{
    public Gw2ErrorKind Kind { get; } = kind;
}

public enum Gw2ErrorKind
{
    Network,
    InvalidKey,        // 400 "invalid key" — missing/malformed/revoked. Not 401.
    MissingScope,      // 403 — key lacks the required permission
    NotFound,          // 404
    BadRequest,        // 400 that is not about the key
    RateLimited,       // 429 — back off, see Gw2Client.RateLimit remarks
    ServiceUnavailable // 503 — "API not available", happens during game builds
}

/// <summary>
/// Thin typed wrapper over https://api.guildwars2.com/v2/.
///
/// Rate limit: ~600 requests/minute per IP (leaky bucket, ~300 burst). Bulk-expand with
/// ids= (200 ids max per call) instead of looping single-id requests, and cache static
/// data (items/skins/recipes) locally — invalidate when GET /v2/build changes.
/// </summary>
public sealed class Gw2Client(HttpClient http)
{
    public const string BaseUrl = "https://api.guildwars2.com/v2/";

    /// <summary>
    /// Pin the schema so ANet's breaking changes land when you choose. GET /v2.json lists
    /// known versions; "latest" also works but moves under you.
    /// </summary>
    public const string SchemaVersion = "2024-07-20T01:00:00.000Z";

    /// <summary>The API rejects bulk requests carrying more ids than this.</summary>
    public const int MaxIdsPerRequest = 200;

    /// <summary>
    /// One bucket for the whole app, because the API meters per IP rather than per screen.
    ///
    /// Nothing here needs to think about pacing: every request goes through
    /// <see cref="PaceAsync"/>, so a screen firing a dozen calls, a background poll and a
    /// catalogue fill all draw on the same allowance. The failure this prevents was a Today
    /// tracker vanishing after a fill had spent the bucket.
    /// </summary>
    private readonly TokenBucket _limit = TokenBucket.ForGuildWars2();

    private readonly SemaphoreSlim _limitGate = new(1, 1);

    /// <summary>
    /// Waits until this request is within the rate limit.
    ///
    /// Taking a token is serialised so concurrent callers queue in order rather than all
    /// reading the same balance and all deciding they may go.
    /// </summary>
    private async Task PaceAsync(CancellationToken ct)
    {
        TimeSpan wait;

        await _limitGate.WaitAsync(ct);
        try
        {
            wait = _limit.Take();
        }
        finally
        {
            _limitGate.Release();
        }

        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
    }

    private string? _apiKey;

    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        BaseAddress = new Uri(BaseUrl),
        Timeout = TimeSpan.FromSeconds(20)
    };

    /// <summary>
    /// What the API is asked to write its names and descriptions in. The device's language by
    /// default; see <see cref="Gw2Language"/>. Settable for tests, which must not depend on the
    /// machine they run on.
    /// </summary>
    public string Language { get; set; } = Gw2Language.Current;

    /// <summary>Set the key used for authenticated endpoints. Null clears it.</summary>
    public void UseApiKey(string? apiKey) => _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();

    // --- endpoints ---------------------------------------------------------

    public Task<TokenInfo> GetTokenInfoAsync(CancellationToken ct = default) =>
        GetAsync("tokeninfo", Gw2JsonContext.Default.TokenInfo, ct);

    public Task<Account> GetAccountAsync(CancellationToken ct = default) =>
        GetAsync("account", Gw2JsonContext.Default.Account, ct);

    /// <summary>Character names only — cheap, one request.</summary>
    public Task<List<string>> GetCharacterNamesAsync(CancellationToken ct = default) =>
        GetAsync("characters", Gw2JsonContext.Default.ListString, ct);

    /// <summary>
    /// Every character, fully expanded. One request, but a large payload on big accounts —
    /// prefer GetCharacterNamesAsync + characters/{name}/core when you only need a list.
    /// </summary>
    public Task<List<Character>> GetCharactersAsync(CancellationToken ct = default) =>
        GetAsync("characters?ids=all", Gw2JsonContext.Default.ListCharacter, ct);

    /// <summary>One character's level and playtime: the small answer, for a poll that runs every
    /// half hour in the background.</summary>
    public Task<CharacterCore> GetCharacterCoreAsync(string character, CancellationToken ct = default) =>
        GetAsync($"characters/{Uri.EscapeDataString(character)}/core", Gw2JsonContext.Default.CharacterCore, ct);

    // --- static / cacheable ------------------------------------------------

    /// <summary>Current game build. Bump the local cache whenever this changes.</summary>
    public Task<GameBuild> GetBuildAsync(CancellationToken ct = default) =>
        GetAsync("build", Gw2JsonContext.Default.GameBuild, ct);

    /// <summary>
    /// The hero points this character has already taken.
    ///
    /// Ids are the floor endpoint's skill challenge ids, so the two line up directly. Needs the
    /// characters scope; a key without it is a key that shows every hero point, which is the
    /// behaviour before this existed rather than a failure.
    /// </summary>
    public Task<List<string>> GetCharacterHeroPointsAsync(
        string character, CancellationToken ct = default) =>
        GetAsync($"characters/{Uri.EscapeDataString(character)}/heropoints",
            Gw2JsonContext.Default.ListString, ct);

    /// <summary>
    /// The world bosses this account has already killed since the daily reset.
    ///
    /// Ids match <see cref="Services.EventSchedule"/>'s ApiId exactly, which is checked by a test
    /// rather than assumed.
    /// </summary>
    public Task<List<string>> GetAccountWorldBossesAsync(CancellationToken ct = default) =>
        GetAsync("account/worldbosses", Gw2JsonContext.Default.ListString, ct);

    /// <summary>
    /// Game asset files by id — the map marker icons, among other things.
    ///
    /// Ids are names rather than numbers here, which is why this takes a string: /v2/files is the
    /// one endpoint keyed that way.
    /// </summary>
    public Task<List<Gw2File>> GetFilesAsync(string ids, CancellationToken ct = default) =>
        GetAsync($"files?ids={ids}", Gw2JsonContext.Default.ListGw2File, ct);

    /// <summary>One map's own description, including the region and floor the landmarks live under.</summary>
    public Task<MapInfo> GetMapAsync(int mapId, CancellationToken ct = default) =>
        GetAsync($"maps/{mapId}", Gw2JsonContext.Default.MapInfo, ct);

    /// <summary>
    /// Every map the game has, in one request.
    ///
    /// <c>ids=all</c> rather than a page walk: this endpoint allows it, and the alternative is
    /// eight requests for a list that only changes with an expansion. About 800 maps and a
    /// megabyte of JSON, which is why nothing calls this per screen — see
    /// <see cref="Services.MapCatalogue"/>, which stores the answer and asks again only when the
    /// game build moves.
    /// </summary>
    public Task<List<MapInfo>> GetAllMapsAsync(CancellationToken ct = default) =>
        GetAsync("maps?ids=all", Gw2JsonContext.Default.ListMapInfo, ct);

    /// <summary>
    /// Every map's English name, by id. The wiki files its loading screens under those, so a
    /// page showing "Königintal" still has to ask it for "Queensdale loading screen.jpg".
    /// </summary>
    public async Task<Dictionary<int, string>> GetEnglishMapNamesAsync(CancellationToken ct = default) =>
        (await GetAsync("maps?ids=all", Gw2JsonContext.Default.ListMapInfo, ct, Gw2Language.English))
        .ToDictionary(map => map.Id, map => map.Name);

    /// <summary>
    /// One continent's dimensions and zoom range, which is what the map tile service needs before
    /// it can be addressed at all. See <see cref="ContinentInfo"/> and
    /// <see cref="Services.TileGeometry"/>.
    /// </summary>
    public Task<ContinentInfo> GetContinentAsync(int continentId, CancellationToken ct = default) =>
        GetAsync($"continents/{continentId}", Gw2JsonContext.Default.ContinentInfo, ct);

    /// <summary>
    /// Everything placed on one map: hearts, waypoints, landmarks, vistas.
    ///
    /// Needs the continent, floor and region from <see cref="GetMapAsync"/> first, which is why
    /// this takes them rather than a map id alone. There is no flatter endpoint — the floor
    /// hierarchy is how the API publishes coordinates.
    ///
    /// The map's own default floor first, and the rest of its floors after. A map's
    /// <c>default_floor</c> does not always hold it: Crystal Oasis names floor 1 and answers 404
    /// there ("no such id"), and its hearts and waypoints are on floor 49. Every Path of Fire map
    /// may be the same. Without the fallback such a map came back empty, and the compass and the
    /// map screen showed no icons on it at all.
    /// </summary>
    public async Task<MapDetail> GetMapDetailAsync(MapInfo map, CancellationToken ct = default)
    {
        try
        {
            return await DetailOnFloorAsync(map, map.DefaultFloor, ct);
        }
        catch (Gw2ApiException ex) when (ex.Kind is Gw2ErrorKind.NotFound)
        {
            // A cached MapInfo can predate the floor list being read; ask for it afresh.
            var floors = map.Floors ?? (await GetMapAsync(map.Id, ct)).Floors ?? [];

            foreach (var floor in floors.Where(floor => floor != map.DefaultFloor))
            {
                try
                {
                    return await DetailOnFloorAsync(map, floor, ct);
                }
                catch (Gw2ApiException next) when (next.Kind is Gw2ErrorKind.NotFound)
                {
                    // Not on this one either.
                }
            }

            throw;
        }
    }

    private Task<MapDetail> DetailOnFloorAsync(MapInfo map, int floor, CancellationToken ct) =>
        GetAsync(
            $"continents/{map.ContinentId}/floors/{floor}/regions/{map.RegionId}/maps/{map.Id}",
            Gw2JsonContext.Default.MapDetail, ct);

    public Task<List<Currency>> GetCurrenciesAsync(CancellationToken ct = default) =>
        GetAsync("currencies?ids=all", Gw2JsonContext.Default.ListCurrency, ct);

    public Task<List<MaterialCategory>> GetMaterialCategoriesAsync(CancellationToken ct = default) =>
        GetAsync("materials?ids=all", Gw2JsonContext.Default.ListMaterialCategory, ct);

    /// <summary>
    /// Items by id, chunked to the API's 200-id ceiling. Requests run sequentially on
    /// purpose: a resolve of a full material storage is ~10 calls, and hammering them in
    /// parallel is the fastest way to trip the rate limit.
    /// </summary>
    /// <summary>
    /// Essence of luck consumed. Requires "progression" and "unlocks".
    ///
    /// Returns a single-entry list, or an empty one for an account that has never consumed
    /// any — which is a real answer, not a failure.
    /// </summary>
    public Task<List<AccountLuck>> GetAccountLuckAsync(CancellationToken ct = default) =>
        GetAsync("account/luck", Gw2JsonContext.Default.ListAccountLuck, ct);

    // --- achievements ------------------------------------------------------

    /// <summary>
    /// Every achievement this account has made progress on. Requires "progression".
    ///
    /// Returns thousands of lines but only ids and counts, so it is one cheap request — which
    /// is what makes it possible to rank first and fetch names only for the few being shown.
    /// </summary>
    public Task<List<AccountAchievement>> GetAccountAchievementsAsync(CancellationToken ct = default) =>
        GetAsync("account/achievements", Gw2JsonContext.Default.ListAccountAchievement, ct);

    /// <summary>
    /// The home instance catalogues and what this account has of them.
    ///
    /// Cheap on every side: 57 nodes and 35 cats, and the node ids are self-describing slugs
    /// that need no name lookup at all.
    /// </summary>
    public Task<List<string>> GetHomeNodesAsync(CancellationToken ct = default) =>
        GetAsync("home/nodes", Gw2JsonContext.Default.ListString, ct);

    public Task<List<string>> GetAccountHomeNodesAsync(CancellationToken ct = default) =>
        GetAsync("account/home/nodes", Gw2JsonContext.Default.ListString, ct);

    public Task<List<HomeCat>> GetHomeCatsAsync(CancellationToken ct = default) =>
        GetAsync("home/cats?ids=all", Gw2JsonContext.Default.ListHomeCat, ct);

    public Task<List<int>> GetAccountHomeCatsAsync(CancellationToken ct = default) =>
        GetAsync("account/home/cats", Gw2JsonContext.Default.ListInt32, ct);

    /// <summary>
    /// What this much coin buys in gems, and what this many gems fetch in coin.
    ///
    /// Both are public — no key, no permission — and both price the trade you ask about
    /// rather than quoting a rate, so the amounts asked for have to be the real ones.
    /// </summary>
    public Task<GemQuote> GetGemsForCoinAsync(long copper, CancellationToken ct = default) =>
        GetAsync($"commerce/exchange/coins?quantity={copper}", Gw2JsonContext.Default.GemQuote, ct);

    public Task<GemQuote> GetCoinForGemsAsync(int gems, CancellationToken ct = default) =>
        GetAsync($"commerce/exchange/gems?quantity={gems}", Gw2JsonContext.Default.GemQuote, ct);

    /// <summary>
    /// Every mastery track, and how far this account is along each.
    ///
    /// The catalogue changes only with a game build; the account half needs "progression".
    /// </summary>
    public Task<List<Mastery>> GetMasteriesAsync(CancellationToken ct = default) =>
        GetAsync("masteries?ids=all", Gw2JsonContext.Default.ListMastery, ct);

    public Task<List<AccountMastery>> GetAccountMasteriesAsync(CancellationToken ct = default) =>
        GetAsync("account/masteries", Gw2JsonContext.Default.ListAccountMastery, ct);

    /// <summary>
    /// Which of the unlockable emotes this account has bought. Needs only "account".
    ///
    /// Narrower than it looks, and the emote screen is built around the gap: the ids returned
    /// can only ever come from /v2/emotes, which lists 14 of the game's 43 unlockable emotes
    /// and none of the 25 free ones. An id absent from here is therefore not evidence that the
    /// account lacks the emote — see <see cref="Services.Emotes"/>.
    /// </summary>
    public Task<List<string>> GetAccountEmotesAsync(CancellationToken ct = default) =>
        GetAsync("account/emotes", Gw2JsonContext.Default.ListString, ct);

    /// <summary>Dyes, so a character's colours can be named rather than only shown.</summary>
    public Task<List<DyeColor>> GetColorsAsync(
        IEnumerable<int> ids, CancellationToken ct = default) =>
        GetBulkAsync("colors", ids, Gw2JsonContext.Default.ListDyeColor, ct);

    /// <summary>
    /// Every id an endpoint has, which is how a total is arrived at.
    ///
    /// One request whatever the size — /v2/skins answers with fourteen thousand integers in
    /// about a hundred kilobytes — so a category's "of how many" costs the same as its name.
    /// Bulk detail is the expensive half, and that is a separate call on purpose.
    /// </summary>
    public Task<List<int>> GetIdsAsync(string path, CancellationToken ct = default) =>
        GetAsync(path, Gw2JsonContext.Default.ListInt32, ct);

    /// <summary>
    /// The ids an account has unlocked, for any of the endpoints that answer with a plain list.
    ///
    /// <paramref name="path"/> rather than a method each. Eleven of the twelve unlock endpoints
    /// differ only in their url and return the same shape, and eleven identical methods would
    /// be eleven places to keep in step with <see cref="Services.Unlocks.All"/>. Finishers are
    /// the exception and have their own — see <see cref="GetAccountFinishersAsync"/>.
    /// </summary>
    public Task<List<int>> GetAccountUnlocksAsync(string path, CancellationToken ct = default) =>
        GetAsync(path, Gw2JsonContext.Default.ListInt32, ct);

    /// <summary>
    /// Finishers, which alone among the unlock endpoints answer with objects rather than ids.
    ///
    /// Each carries whether it is permanent and how many uses are left, because most finishers
    /// are consumable. Only the id is used for counting — an account that owns a finisher owns
    /// it whether or not it has charges — but the shape still has to be parsed as published.
    /// </summary>
    public Task<List<AccountFinisher>> GetAccountFinishersAsync(CancellationToken ct = default) =>
        GetAsync("account/finishers", Gw2JsonContext.Default.ListAccountFinisher, ct);

    /// <summary>
    /// Names for any of the cosmetic catalogues.
    ///
    /// They are all id, name and icon once the parts this screen does not draw are ignored, so
    /// one record and one call covers dyes' neighbours — outfits, gliders, titles, mailcarriers,
    /// novelties, finishers, jade bot skins, skiff skins and mount skins. Dyes themselves go
    /// through <see cref="GetColorsAsync"/>, which carries the channels a swatch needs.
    /// </summary>
    public Task<List<UnlockItem>> GetUnlockItemsAsync(
        string path, IEnumerable<int> ids, CancellationToken ct = default) =>
        GetBulkAsync(path, ids, Gw2JsonContext.Default.ListUnlockItem, ct);

    /// <summary>
    /// PvP amulets, which live in their own id space rather than among items — the reason a
    /// PvP loadout cannot be read from the equipment array alone.
    /// </summary>
    public Task<List<PvpAmulet>> GetAmuletsAsync(
        IEnumerable<int> ids, CancellationToken ct = default) =>
        GetBulkAsync("pvp/amulets", ids, Gw2JsonContext.Default.ListPvpAmulet, ct);

    /// <summary>Skin names, for the collections that are made of them.</summary>
    public Task<List<Skin>> GetSkinsAsync(IEnumerable<int> ids, CancellationToken ct = default) =>
        GetBulkAsync("skins", ids, Gw2JsonContext.Default.ListSkin, ct);

    /// <summary>Miniature names, likewise.</summary>
    public Task<List<Mini>> GetMinisAsync(IEnumerable<int> ids, CancellationToken ct = default) =>
        GetBulkAsync("minis", ids, Gw2JsonContext.Default.ListMini, ct);

    /// <summary>Names and tiers, fetched only for the achievements actually on screen.</summary>
    public Task<List<Achievement>> GetAchievementsAsync(
        IEnumerable<int> ids, CancellationToken ct = default) =>
        GetBulkAsync("achievements", ids, Gw2JsonContext.Default.ListAchievement, ct);

    /// <summary>
    /// Achievements as English names them, whatever <see cref="Language"/> is: the daily fractals
    /// are told apart by their names alone ("Daily Tier 4 Aetherblade"), and the English ones are
    /// the ones with a shape worth reading.
    /// </summary>
    public Task<List<Achievement>> GetEnglishAchievementsAsync(
        IEnumerable<int> ids, CancellationToken ct = default) =>
        GetBulkAsync("achievements", ids, Gw2JsonContext.Default.ListAchievement, ct, Gw2Language.English);

    /// <summary>The fractal dailies, today's and tomorrow's. Unauthenticated; changes at the
    /// daily reset.</summary>
    public Task<DailyCategory> GetDailyFractalsAsync(CancellationToken ct = default) =>
        GetAsync("achievements/categories/88", Gw2JsonContext.Default.DailyCategory, ct);

    // --- crafting ----------------------------------------------------------

    /// <summary>
    /// Recipes that produce this item. Unauthenticated; usually none or one, occasionally a
    /// handful where several disciplines make the same thing.
    /// </summary>
    public Task<List<int>> FindRecipesForAsync(int itemId, CancellationToken ct = default) =>
        GetAsync($"recipes/search?output={itemId}", Gw2JsonContext.Default.ListInt32, ct);

    public Task<List<Recipe>> GetRecipesAsync(IEnumerable<int> ids, CancellationToken ct = default) =>
        GetBulkAsync("recipes", ids, Gw2JsonContext.Default.ListRecipe, ct);

    /// <summary>
    /// Every recipe id the game knows — 13,371 of them, in one response.
    ///
    /// The diffing mechanism for the recipe catalogue, exactly as /v2/items is for items.
    /// </summary>
    public Task<List<int>> GetRecipeIdsAsync(CancellationToken ct = default) =>
        GetAsync("recipes", Gw2JsonContext.Default.ListInt32, ct);

    /// <summary>
    /// Recipe ids this account has unlocked. Requires "unlocks".
    ///
    /// This is the honest universe for levelling advice: the API can list all thirteen
    /// thousand recipes, but only over sixty-seven paged requests, and most of them are things
    /// this account cannot make.
    /// </summary>
    public Task<List<int>> GetAccountRecipesAsync(CancellationToken ct = default) =>
        GetAsync("account/recipes", Gw2JsonContext.Default.ListInt32, ct);

    // --- personal story ----------------------------------------------------

    /// <summary>
    /// Quests this character has completed. Requires "progression" and "characters".
    ///
    /// Known to be imperfect: some Heart of Thorns and Icebrood Saga steps do not appear even
    /// when finished, and a story replayed from the journal can disturb what follows it. The
    /// screen says so rather than presenting an absence as a fact.
    /// </summary>
    public Task<List<int>> GetCharacterQuestsAsync(string character, CancellationToken ct = default) =>
        GetAsync($"characters/{Uri.EscapeDataString(character)}/quests",
            Gw2JsonContext.Default.ListInt32, ct);

    /// <summary>The steps themselves, including the first-person account of each.</summary>
    public Task<List<Quest>> GetQuestsAsync(IEnumerable<int> ids, CancellationToken ct = default) =>
        GetBulkAsync("quests", ids, Gw2JsonContext.Default.ListQuest, ct);

    /// <summary>Every story arc. Unauthenticated and static; 142 of them, 10 KB gzipped.</summary>
    public Task<List<Story>> GetStoriesAsync(CancellationToken ct = default) =>
        GetAsync("stories?ids=all", Gw2JsonContext.Default.ListStory, ct);

    public Task<List<StorySeason>> GetStorySeasonsAsync(CancellationToken ct = default) =>
        GetAsync("stories/seasons?ids=all", Gw2JsonContext.Default.ListStorySeason, ct);

    /// <summary>
    /// The full record for a single item, for the detail screen. Not cached: it is one
    /// request for one tap, and the cache deliberately keeps only what lists need.
    /// </summary>
    public async Task<ItemDetail?> GetItemDetailAsync(int id, CancellationToken ct = default) =>
        (await GetAsync($"items?ids={id}", Gw2JsonContext.Default.ListItemDetail, ct))
        .FirstOrDefault();

    /// <summary>
    /// Every item id the game knows — about 74,000 of them, in one 665 KB response.
    ///
    /// This is the whole diffing mechanism for the catalogue: there is no "changed since"
    /// endpoint, so the id list is compared against what is already stored and only the
    /// difference is fetched.
    /// </summary>
    public Task<List<int>> GetItemIdsAsync(CancellationToken ct = default) =>
        GetAsync("items", Gw2JsonContext.Default.ListInt32, ct);

    /// <summary>
    /// The ids the trading post actually trades — about 28,000. GET /v2/commerce/prices with
    /// no ids is the market's own list of what has a price, which is exactly the set a price
    /// watch can ever fire on. Unauthenticated.
    /// </summary>
    public Task<List<int>> GetTradableItemIdsAsync(CancellationToken ct = default) =>
        GetAsync("commerce/prices", Gw2JsonContext.Default.ListInt32, ct);

    public Task<List<Item>> GetItemsAsync(IEnumerable<int> ids, CancellationToken ct = default) =>
        GetItemsAsync(ids, null, ct);

    /// <summary>
    /// Items as English names them, whatever <see cref="Language"/> is: what a search has to
    /// match when somebody playing in German types the name every guide and the wiki use.
    /// </summary>
    public Task<List<Item>> GetEnglishItemsAsync(IEnumerable<int> ids, CancellationToken ct = default) =>
        GetItemsAsync(ids, Gw2Language.English, ct);

    private async Task<List<Item>> GetItemsAsync(IEnumerable<int> ids, string? language, CancellationToken ct)
    {
        var result = new List<Item>();
        foreach (var chunk in ids.Distinct().Chunk(MaxIdsPerRequest))
        {
            var batch = await GetAsync(
                $"items?ids={string.Join(',', chunk)}", Gw2JsonContext.Default.ListItem, ct, language);
            result.AddRange(batch);
        }

        return result;
    }

    // --- authenticated -----------------------------------------------------

    /// <summary>
    /// Hero challenges this character has completed. Requires "characters" and
    /// "progression". Returns null when unreadable, so the screen can omit the number
    /// instead of showing a misleading zero.
    /// </summary>
    public async Task<List<string>?> GetHeroPointsAsync(string character, CancellationToken ct = default)
    {
        try
        {
            return await GetAsync(
                $"characters/{Uri.EscapeDataString(character)}/heropoints",
                Gw2JsonContext.Default.ListString, ct);
        }
        catch (Gw2ApiException)
        {
            return null;
        }
    }

    /// <summary>
    /// Mastery points, account-wide. Requires "progression". Returns null when the key
    /// cannot read it, so callers fall back to showing the level.
    ///
    /// Always in English. Its regions are names ("Central Tyria", "Path of Fire"), not ids, and
    /// <see cref="Services.Masteries"/> pairs them with the tracks' region ids through a table
    /// of those English names; a "Zentraltyria" would match nothing, and every track would read
    /// as unaffordable.
    /// </summary>
    public async Task<MasteryPoints?> GetMasteryPointsAsync(CancellationToken ct = default)
    {
        try
        {
            return await GetAsync(
                "account/mastery/points", Gw2JsonContext.Default.MasteryPoints, ct, Gw2Language.English);
        }
        catch (Gw2ApiException ex) when (ex.Kind is Gw2ErrorKind.MissingScope or Gw2ErrorKind.NotFound)
        {
            return null;
        }
    }

    /// <summary>Requires the "wallet" scope.</summary>
    public Task<List<WalletEntry>> GetWalletAsync(CancellationToken ct = default) =>
        GetAsync("account/wallet", Gw2JsonContext.Default.ListWalletEntry, ct);

    /// <summary>Requires the "inventories" scope.</summary>
    public Task<List<AccountMaterial>> GetAccountMaterialsAsync(CancellationToken ct = default) =>
        GetAsync("account/materials", Gw2JsonContext.Default.ListAccountMaterial, ct);

    /// <summary>
    /// The bank, slot by slot. Requires "inventories".
    ///
    /// Empty slots come back as null and are returned as such: the position of a stack in the
    /// bank is meaningless to this app, but dropping the nulls silently would make an empty
    /// bank indistinguishable from a missing scope.
    /// </summary>
    public Task<List<StorageSlot?>> GetBankAsync(CancellationToken ct = default) =>
        GetAsync("account/bank", Gw2JsonContext.Default.ListStorageSlot, ct);

    /// <summary>
    /// The shared inventory slots — the handful of slots every character can reach.
    /// Requires "inventories". Same shape as the bank, nulls included.
    /// </summary>
    public Task<List<StorageSlot?>> GetSharedInventoryAsync(CancellationToken ct = default) =>
        GetAsync("account/inventory", Gw2JsonContext.Default.ListStorageSlot, ct);

    // --- build reference data ----------------------------------------------

    /// <summary>Specialization names. Unauthenticated and static between patches.</summary>
    public Task<List<Specialization>> GetSpecializationsAsync(IEnumerable<int> ids, CancellationToken ct = default) =>
        GetBulkAsync("specializations", ids, Gw2JsonContext.Default.ListSpecialization, ct);

    /// <summary>
    /// One specialization's English name, which is what the wiki files its painting under:
    /// "Spec image Scrapper.jpg", whatever a German client calls the Schrotter.
    /// </summary>
    public async Task<string?> GetEnglishSpecializationNameAsync(int id, CancellationToken ct = default) =>
        (await GetAsync($"specializations?ids={id}", Gw2JsonContext.Default.ListSpecialization, ct, Gw2Language.English))
        .FirstOrDefault()?.Name;

    public Task<List<Trait>> GetTraitsAsync(IEnumerable<int> ids, CancellationToken ct = default) =>
        GetBulkAsync("traits", ids, Gw2JsonContext.Default.ListTrait, ct);

    public Task<List<Skill>> GetSkillsAsync(IEnumerable<int> ids, CancellationToken ct = default) =>
        GetBulkAsync("skills", ids, Gw2JsonContext.Default.ListSkill, ct);

    /// <summary>
    /// Several professions at once, for their icons. One request covers a whole roster.
    /// </summary>
    public async Task<List<Profession>> GetProfessionsAsync(
        IEnumerable<string> names, CancellationToken ct = default)
    {
        var list = names.Distinct().ToList();
        if (list.Count == 0) return [];

        try
        {
            return await GetAsync(
                $"professions?ids={string.Join(',', list.Select(Uri.EscapeDataString))}",
                Gw2JsonContext.Default.ListProfession, ct);
        }
        catch (Gw2ApiException ex) when (ex.Kind is Gw2ErrorKind.NotFound or Gw2ErrorKind.BadRequest)
        {
            return [];
        }
    }

    /// <summary>One profession, for its weapon-to-skill map.</summary>
    public async Task<Profession?> GetProfessionAsync(string name, CancellationToken ct = default)
    {
        try
        {
            var professions = await GetAsync(
                $"professions?ids={Uri.EscapeDataString(name)}", Gw2JsonContext.Default.ListProfession, ct);
            return professions.FirstOrDefault();
        }
        catch (Gw2ApiException ex) when (ex.Kind is Gw2ErrorKind.NotFound or Gw2ErrorKind.BadRequest)
        {
            return null;
        }
    }

    /// <summary>
    /// The character-creation answers behind a character, by id. Unauthenticated and static.
    /// </summary>
    public async Task<List<BackstoryAnswer>> GetBackstoryAnswersAsync(
        IEnumerable<string> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];

        try
        {
            return await GetAsync(
                $"backstory/answers?ids={string.Join(',', list.Select(Uri.EscapeDataString))}",
                Gw2JsonContext.Default.ListBackstoryAnswer, ct);
        }
        catch (Gw2ApiException ex) when (ex.Kind is Gw2ErrorKind.NotFound or Gw2ErrorKind.BadRequest)
        {
            return [];
        }
    }

    /// <summary>Every stat combination with its attributes, 191 of them. Unauthenticated and static.</summary>
    public Task<List<ItemStat>> GetAllItemStatsAsync(CancellationToken ct = default, string? language = null) =>
        GetAsync("itemstats?ids=all", Gw2JsonContext.Default.ListItemStat, ct, language);

    /// <summary>The account's legendary armory. Needs "inventories" and "unlocks".</summary>
    public Task<List<ArmoryEntry>> GetLegendaryArmoryAsync(CancellationToken ct = default) =>
        GetAsync("account/legendaryarmory", Gw2JsonContext.Default.ListArmoryEntry, ct);

    /// <summary>Named stat combinations, e.g. Berserker's. Unauthenticated and static.</summary>
    public Task<List<ItemStat>> GetItemStatsAsync(IEnumerable<int> ids, CancellationToken ct = default) =>
        GetBulkAsync("itemstats", ids, Gw2JsonContext.Default.ListItemStat, ct);

    /// <summary>Equipped weapons, for the weapon type the item cache does not store.</summary>
    public Task<List<WeaponItem>> GetWeaponItemsAsync(IEnumerable<int> ids, CancellationToken ct = default) =>
        GetBulkAsync("items", ids, Gw2JsonContext.Default.ListWeaponItem, ct);

    /// <summary>Revenant legends, whose skills stand in for the chosen utility bar.</summary>
    public async Task<List<Legend>> GetLegendsAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];

        try
        {
            return await GetAsync(
                $"legends?ids={string.Join(',', list.Select(Uri.EscapeDataString))}",
                Gw2JsonContext.Default.ListLegend, ct);
        }
        catch (Gw2ApiException ex) when (ex.Kind is Gw2ErrorKind.NotFound or Gw2ErrorKind.BadRequest)
        {
            return [];
        }
    }

    /// <summary>
    /// Bulk fetch by id, chunked to the API ceiling. A group that 404s is skipped rather
    /// than failing the screen: one retired trait id should not blank a whole build.
    /// </summary>
    private async Task<List<T>> GetBulkAsync<T>(
        string path, IEnumerable<int> ids, JsonTypeInfo<List<T>> typeInfo, CancellationToken ct,
        string? language = null)
    {
        var result = new List<T>();
        foreach (var chunk in ids.Distinct().Chunk(MaxIdsPerRequest))
        {
            try
            {
                result.AddRange(await GetAsync($"{path}?ids={string.Join(',', chunk)}", typeInfo, ct, language));
            }
            catch (Gw2ApiException ex) when (ex.Kind is Gw2ErrorKind.NotFound or Gw2ErrorKind.BadRequest)
            {
            }
        }

        return result;
    }

    // --- wizard's vault ----------------------------------------------------

    /// <summary>The running season. Unauthenticated, changes a few times a year.</summary>
    public Task<VaultSeason> GetVaultSeasonAsync(CancellationToken ct = default) =>
        GetAsync("wizardsvault", Gw2JsonContext.Default.VaultSeason, ct);

    /// <summary>The vault's shop with this account's purchases. Requires "account".</summary>
    public Task<List<VaultListing>> GetVaultListingsAsync(CancellationToken ct = default) =>
        GetAsync("account/wizardsvault/listings", Gw2JsonContext.Default.ListVaultListing, ct);

    /// <summary>Requires "progression". Track is daily, weekly or special.</summary>
    public Task<VaultTrack> GetVaultTrackAsync(string track, CancellationToken ct = default) =>
        GetAsync($"account/wizardsvault/{track}", Gw2JsonContext.Default.VaultTrack, ct);

    // --- daily reset trackers ----------------------------------------------
    // Each pair is the same shape: a static list of everything, and an account list of what
    // is already done. The difference is what is left today.

    public Task<List<string>> GetWorldBossIdsAsync(CancellationToken ct = default) =>
        GetAsync("worldbosses", Gw2JsonContext.Default.ListString, ct);

    public Task<List<string>> GetMapChestIdsAsync(CancellationToken ct = default) =>
        GetAsync("mapchests", Gw2JsonContext.Default.ListString, ct);

    public Task<List<string>> GetDailyCraftingIdsAsync(CancellationToken ct = default) =>
        GetAsync("dailycrafting", Gw2JsonContext.Default.ListString, ct);

    /// <summary>Map bonus chests looted since reset. Requires "progression".</summary>
    public Task<List<string>> GetCompletedMapChestsAsync(CancellationToken ct = default) =>
        GetAsync("account/mapchests", Gw2JsonContext.Default.ListString, ct);

    /// <summary>Time-gated crafting done since reset. Requires "progression".</summary>
    public Task<List<string>> GetCompletedDailyCraftingAsync(CancellationToken ct = default) =>
        GetAsync("account/dailycrafting", Gw2JsonContext.Default.ListString, ct);

    /// <summary>
    /// World bosses killed since the 00:00 UTC reset. Requires "progression". The API has no
    /// schedule endpoint - only this list of what is already done.
    /// </summary>
    public Task<List<string>> GetCompletedWorldBossesAsync(CancellationToken ct = default) =>
        GetAsync("account/worldbosses", Gw2JsonContext.Default.ListString, ct);

    /// <summary>
    /// Every raid, expanded to its wings and encounters in one request.
    ///
    /// The expansion is necessary rather than convenient: /v2/account/raids reports
    /// encounters, so the bare list of six raid ids cannot be compared against it.
    /// </summary>
    public Task<List<Raid>> GetRaidsAsync(CancellationToken ct = default) =>
        GetAsync("raids?ids=all", Gw2JsonContext.Default.ListRaid, ct);

    /// <summary>Every dungeon with its paths, for the same reason. One request.</summary>
    public Task<List<Dungeon>> GetDungeonsAsync(CancellationToken ct = default) =>
        GetAsync("dungeons?ids=all", Gw2JsonContext.Default.ListDungeon, ct);

    /// <summary>
    /// Raid encounters cleared since the weekly reset — Monday 07:30 UTC, not the daily
    /// 00:00. Requires "progression". Includes checkpoints, which are not encounters.
    /// </summary>
    public Task<List<string>> GetCompletedRaidEncountersAsync(CancellationToken ct = default) =>
        GetAsync("account/raids", Gw2JsonContext.Default.ListString, ct);

    /// <summary>Dungeon paths cleared since the daily reset. Requires "progression".</summary>
    public Task<List<string>> GetCompletedDungeonPathsAsync(CancellationToken ct = default) =>
        GetAsync("account/dungeons", Gw2JsonContext.Default.ListString, ct);

    // --- trading post ------------------------------------------------------

    /// <summary>
    /// Closed trades from the last 90 days, newest first. Requires "tradingpost".
    /// The API caches commerce responses for five minutes, so polling faster is wasted.
    /// </summary>
    public Task<List<Transaction>> GetTransactionHistoryAsync(string side, CancellationToken ct = default) =>
        GetAsync($"commerce/transactions/history/{side}", Gw2JsonContext.Default.ListTransaction, ct);

    /// <summary>Open orders that have not filled yet. Requires "tradingpost".</summary>
    public Task<List<Transaction>> GetCurrentTransactionsAsync(string side, CancellationToken ct = default) =>
        GetAsync($"commerce/transactions/current/{side}", Gw2JsonContext.Default.ListTransaction, ct);

    /// <summary>What the trading post is holding for you. Requires "tradingpost".</summary>
    public Task<DeliveryBox> GetDeliveryAsync(CancellationToken ct = default) =>
        GetAsync("commerce/delivery", Gw2JsonContext.Default.DeliveryBox, ct);

    /// <summary>
    /// Current market prices, chunked to the id ceiling. Unauthenticated, and the reason the
    /// orders screen can say whether you have been undercut.
    /// </summary>
    public async Task<List<ItemPrice>> GetPricesAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var result = new List<ItemPrice>();
        foreach (var chunk in ids.Distinct().Chunk(MaxIdsPerRequest))
        {
            // Items that have never been traded 404 as a group; skip rather than fail the screen.
            try
            {
                result.AddRange(await GetAsync(
                    $"commerce/prices?ids={string.Join(',', chunk)}", Gw2JsonContext.Default.ListItemPrice, ct));
            }
            catch (Gw2ApiException ex) when (ex.Kind is Gw2ErrorKind.NotFound or Gw2ErrorKind.BadRequest)
            {
            }
        }

        return result;
    }

    /// <summary>
    /// Whole order books, for the handful of orders whose depth is worth knowing.
    ///
    /// Deliberately not a screen-wide fetch: an item like Glob of Ectoplasm has some 2,700
    /// price steps, and forty mixed items already weigh half a megabyte. Callers ask for the
    /// few orders where the queue changes a decision.
    /// </summary>
    public async Task<List<ItemListings>> GetListingsAsync(
        IEnumerable<int> ids, CancellationToken ct = default)
    {
        var result = new List<ItemListings>();

        foreach (var chunk in ids.Distinct().Chunk(MaxIdsPerRequest))
        {
            // An item with an empty book 404s as a group, exactly as prices do.
            try
            {
                result.AddRange(await GetAsync(
                    $"commerce/listings?ids={string.Join(',', chunk)}",
                    Gw2JsonContext.Default.ListItemListings, ct));
            }
            catch (Gw2ApiException ex) when (ex.Kind is Gw2ErrorKind.NotFound or Gw2ErrorKind.BadRequest)
            {
            }
        }

        return result;
    }

    /// <summary>Temporary: raw JSON, for discovering a response shape.</summary>
    public async Task<string> GetRawAsync(string path, CancellationToken ct = default)
    {
        await PaceAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, Address(path, null));
        if (_apiKey is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await http.SendAsync(request, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    // --- plumbing ----------------------------------------------------------

    private async Task<T> GetAsync<T>(
        string path, JsonTypeInfo<T> typeInfo, CancellationToken ct, string? language = null)
    {
        await PaceAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, Address(path, language));

        // Header auth, not ?access_token= — query strings leak into logs and proxies.
        if (_apiKey is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new Gw2ApiException(L.T("Could not reach the Guild Wars 2 API."), Gw2ErrorKind.Network);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw Translate(response.StatusCode, await ReadErrorTextAsync(response, ct));

            return await response.Content.ReadFromJsonAsync(typeInfo, ct)
                   ?? throw new Gw2ApiException(L.F("Empty response from /{0}.", path), Gw2ErrorKind.Network);
        }
    }

    /// <summary>
    /// The path with the pinned schema and the language on it.
    ///
    /// A query parameter rather than Accept-Language, though the API honours both: it is in
    /// the address, so a request in a log says which language it asked for.
    /// </summary>
    private string Address(string path, string? language)
    {
        var separator = path.Contains('?') ? '&' : '?';

        return $"{path}{separator}v={SchemaVersion}&lang={language ?? Language}";
    }

    /// <summary>The error body is tiny; reading it is what makes 400s intelligible.</summary>
    private static async Task<string?> ReadErrorTextAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync(Gw2JsonContext.Default.ApiError, ct);
            return error?.Text;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>"invalid key", "invalid access token", "endpoint requires authentication".</summary>
    private static bool IsAboutTheKey(string? text) =>
        text is not null &&
        (text.Contains("key", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("access token", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("authentication", StringComparison.OrdinalIgnoreCase));

    private static Gw2ApiException Translate(HttpStatusCode status, string? detail) => status switch
    {
        // A key the API does not recognise comes back as 400 "invalid key", not 401.
        HttpStatusCode.BadRequest when IsAboutTheKey(detail) =>
            new Gw2ApiException(L.T("API key is missing, malformed or revoked."), Gw2ErrorKind.InvalidKey),
        HttpStatusCode.BadRequest =>
            new Gw2ApiException(L.F("The API rejected the request: {0}.", detail ?? L.T("bad request")), Gw2ErrorKind.BadRequest),
        HttpStatusCode.Unauthorized => new Gw2ApiException(L.T("API key is missing, malformed or revoked."), Gw2ErrorKind.InvalidKey),
        HttpStatusCode.Forbidden => new Gw2ApiException(L.T("API key lacks the permission this endpoint needs."), Gw2ErrorKind.MissingScope),
        HttpStatusCode.NotFound => new Gw2ApiException(L.T("Unknown endpoint or id."), Gw2ErrorKind.NotFound),
        HttpStatusCode.TooManyRequests => new Gw2ApiException(L.T("Rate limited — slow down and retry."), Gw2ErrorKind.RateLimited),
        HttpStatusCode.ServiceUnavailable => new Gw2ApiException(L.T("The API is temporarily unavailable."), Gw2ErrorKind.ServiceUnavailable),
        _ => new Gw2ApiException(L.F("Unexpected response: {0} {1}.", (int)status, status), Gw2ErrorKind.Network)
    };
}
