using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;
using Microsoft.Data.Sqlite;

namespace GuildWarsCompanion.Data;

/// <summary>
/// Local store for the API's static half (items, currencies, maps). Those records only change
/// when ArenaNet ships a build, so caching them turns "resolve 900 material ids" from
/// ~10 requests per screen view into ~10 requests per patch.
///
/// Deliberately hand-written SQL: no ORM reflection, so it survives TrimMode=full.
/// </summary>
public sealed class Gw2Cache(string databasePath)
{
    private readonly string _connectionString =
        new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _gate.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            await using var connection = await OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS items (
                    id INTEGER PRIMARY KEY, name TEXT NOT NULL, icon TEXT, rarity TEXT);
                CREATE TABLE IF NOT EXISTS currencies (
                    id INTEGER PRIMARY KEY, name TEXT NOT NULL, description TEXT NOT NULL,
                    ord INTEGER NOT NULL, icon TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS tradable (id INTEGER PRIMARY KEY);
                CREATE TABLE IF NOT EXISTS recipes (
                    id INTEGER PRIMARY KEY, output_item_id INTEGER NOT NULL,
                    output_count INTEGER NOT NULL, min_rating INTEGER NOT NULL,
                    type TEXT, disciplines TEXT NOT NULL, auto_learned INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS recipe_ingredients (
                    recipe_id INTEGER NOT NULL, item_id INTEGER NOT NULL, count INTEGER NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_recipe_rating ON recipes (min_rating);
                CREATE INDEX IF NOT EXISTS ix_ingredient_recipe ON recipe_ingredients (recipe_id);
                CREATE TABLE IF NOT EXISTS map_detail (
                    map_id INTEGER PRIMARY KEY, payload TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS continents (
                    id INTEGER PRIMARY KEY, payload TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS maps (
                    id INTEGER PRIMARY KEY, name TEXT NOT NULL, type TEXT,
                    region_id INTEGER NOT NULL, region_name TEXT,
                    continent_id INTEGER NOT NULL, continent_name TEXT,
                    default_floor INTEGER NOT NULL,
                    c_left REAL, c_top REAL, c_right REAL, c_bottom REAL,
                    m_left REAL, m_bottom REAL, m_right REAL, m_top REAL);
                """;
            await command.ExecuteNonQueryAsync(ct);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Drops cached static data if the game build changed since we filled it.
    /// Returns true when the cache was invalidated.
    /// </summary>
    public async Task<bool> InvalidateIfStaleAsync(int currentBuild, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var connection = await OpenAsync(ct);

        var stored = await ReadMetaAsync(connection, "build", ct);
        if (stored == currentBuild.ToString()) return false;

        await using (var clear = connection.CreateCommand())
        {
            // Currencies and maps: sixty and eight hundred rows, one request each to refill.
            // A build is exactly when a map is added, renamed, or has its footprint redrawn.
            //
            // Items are deliberately kept. The catalogue runs to 74,000 rows and 371 requests
            // to fill, while ArenaNet ships a build every week or two — wiping it each patch
            // would throw the whole thing away and refetch it, forever. Names and icons for an
            // existing id are effectively immutable; new ids arrive as additions, which is what
            // ItemCatalogue diffs for. A rename is rare enough to be worth a manual rebuild.
            clear.CommandText =
                "DELETE FROM currencies; DELETE FROM maps; " +
                "DELETE FROM map_detail; DELETE FROM continents; " +
                $"DELETE FROM meta WHERE key = '{MapsComplete}';";
            await clear.ExecuteNonQueryAsync(ct);
        }

        await WriteMetaAsync(connection, "build", currentBuild.ToString(), ct);
        return true;
    }

    // --- items -------------------------------------------------------------

    public async Task<Dictionary<int, Item>> GetItemsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        var found = new Dictionary<int, Item>(ids.Count);
        if (ids.Count == 0) return found;

        await using var connection = await OpenAsync(ct);
        foreach (var chunk in ids.Distinct().Chunk(500))
        {
            await using var command = connection.CreateCommand();
            var parameters = new string[chunk.Length];
            for (var i = 0; i < chunk.Length; i++)
            {
                parameters[i] = "$p" + i;
                command.Parameters.AddWithValue("$p" + i, chunk[i]);
            }

            command.CommandText =
                "SELECT id, name, icon, rarity FROM items WHERE id IN (" + string.Join(",", parameters) + ")";

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var item = new Item(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3));
                found[item.Id] = item;
            }
        }

        return found;
    }

    public async Task SaveItemsAsync(IReadOnlyCollection<Item> items, CancellationToken ct = default)
    {
        if (items.Count == 0) return;
        await InitializeAsync(ct);

        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR REPLACE INTO items (id, name, icon, rarity) VALUES ($id, $name, $icon, $rarity)";
        var id = command.Parameters.Add("$id", SqliteType.Integer);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var icon = command.Parameters.Add("$icon", SqliteType.Text);
        var rarity = command.Parameters.Add("$rarity", SqliteType.Text);

        foreach (var item in items)
        {
            id.Value = item.Id;
            name.Value = item.Name;
            icon.Value = (object?)item.Icon ?? DBNull.Value;
            rarity.Value = (object?)item.Rarity ?? DBNull.Value;
            await command.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Every item id already stored, for working out what is still missing.
    ///
    /// Returned whole rather than asked per id: the catalogue compares against 74,000 ids at
    /// once, and that is one scan instead of 74,000 lookups.
    /// </summary>
    public async Task<HashSet<int>> GetKnownItemIdsAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var known = new HashSet<int>();

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM items";

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            known.Add(reader.GetInt32(0));

        return known;
    }

    /// <summary>
    /// Records which items the trading post actually handles, replacing the previous set.
    ///
    /// This is GET /v2/commerce/prices with no ids — the market's own list of what it trades.
    /// Only about 28,000 of the 74,000 items appear, which is what keeps a price-watch search
    /// from offering thousands of soulbound things no watch could ever fire on.
    /// </summary>
    public async Task SaveTradableAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return;
        await InitializeAsync(ct);

        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var clear = connection.CreateCommand())
        {
            clear.CommandText = "DELETE FROM tradable";
            await clear.ExecuteNonQueryAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO tradable (id) VALUES ($id)";
        var id = command.Parameters.Add("$id", SqliteType.Integer);

        foreach (var value in ids)
        {
            id.Value = value;
            await command.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<int> CountTradableAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM tradable";
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    // --- maps --------------------------------------------------------------

    /// <summary>
    /// Every map the game has, as the API last described them.
    ///
    /// Read whole rather than searched in SQL. Eight hundred rows is nothing to hold, and the
    /// search this feeds is typed a letter at a time — a query per keystroke against a database
    /// is a worse answer than one list in hand. See <see cref="Services.MapCatalogue"/>.
    /// </summary>
    public async Task<List<MapInfo>> GetMapsAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var maps = new List<MapInfo>();

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, name, type, region_id, region_name, continent_id, continent_name, " +
            "default_floor, c_left, c_top, c_right, c_bottom, m_left, m_bottom, m_right, m_top " +
            "FROM maps";

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) maps.Add(Read(reader));

        return maps;
    }

    /// <summary>
    /// Whether the whole map list has been stored, rather than the handful of maps somebody has
    /// happened to stand in.
    ///
    /// The distinction is not pedantic and was found on the phone: the picker asked "are there
    /// any map rows?", the compass had just written the one map the character was standing in,
    /// and the answer was a list of maps containing Queensdale. A count cannot tell those two
    /// apart; a flag written only by the full save can.
    /// </summary>
    public async Task<bool> HasAllMapsAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        await using var connection = await OpenAsync(ct);

        return await ReadMetaAsync(connection, MapsComplete, ct) == "yes";
    }

    /// <summary>The meta key recording that <c>ids=all</c> has been stored and kept.</summary>
    private const string MapsComplete = "maps_all";

    /// <summary>One map by id, or null when this phone has never been told about it.</summary>
    public async Task<MapInfo?> GetMapAsync(int mapId, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, name, type, region_id, region_name, continent_id, continent_name, " +
            "default_floor, c_left, c_top, c_right, c_bottom, m_left, m_bottom, m_right, m_top " +
            "FROM maps WHERE id = $id";
        command.Parameters.AddWithValue("$id", mapId);

        await using var reader = await command.ExecuteReaderAsync(ct);

        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    /// <summary>
    /// Stores maps.
    /// </summary>
    /// <param name="replaceAll">
    /// True for the whole list from <c>ids=all</c>, which is the only caller that knows what is
    /// no longer in the game: a map that has left should leave the picker rather than linger.
    /// False for the one map a screen just looked up, which says nothing about the rest.
    /// </param>
    public async Task SaveMapsAsync(
        IReadOnlyCollection<MapInfo> maps, bool replaceAll = false, CancellationToken ct = default)
    {
        if (maps.Count == 0) return;
        await InitializeAsync(ct);

        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        if (replaceAll)
        {
            await using var clear = connection.CreateCommand();
            clear.CommandText = "DELETE FROM maps";
            await clear.ExecuteNonQueryAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR REPLACE INTO maps (id, name, type, region_id, region_name, continent_id, " +
            "continent_name, default_floor, c_left, c_top, c_right, c_bottom, " +
            "m_left, m_bottom, m_right, m_top) " +
            "VALUES ($id, $name, $type, $region, $regionName, $continent, $continentName, $floor, " +
            "$left, $top, $right, $bottom, $mapLeft, $mapBottom, $mapRight, $mapTop)";

        var id = command.Parameters.Add("$id", SqliteType.Integer);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var type = command.Parameters.Add("$type", SqliteType.Text);
        var region = command.Parameters.Add("$region", SqliteType.Integer);
        var regionName = command.Parameters.Add("$regionName", SqliteType.Text);
        var continent = command.Parameters.Add("$continent", SqliteType.Integer);
        var continentName = command.Parameters.Add("$continentName", SqliteType.Text);
        var floor = command.Parameters.Add("$floor", SqliteType.Integer);
        var left = command.Parameters.Add("$left", SqliteType.Real);
        var top = command.Parameters.Add("$top", SqliteType.Real);
        var right = command.Parameters.Add("$right", SqliteType.Real);
        var bottom = command.Parameters.Add("$bottom", SqliteType.Real);
        var mapLeft = command.Parameters.Add("$mapLeft", SqliteType.Real);
        var mapBottom = command.Parameters.Add("$mapBottom", SqliteType.Real);
        var mapRight = command.Parameters.Add("$mapRight", SqliteType.Real);
        var mapTop = command.Parameters.Add("$mapTop", SqliteType.Real);

        foreach (var map in maps)
        {
            id.Value = map.Id;
            name.Value = map.Name;
            type.Value = (object?)map.Type ?? DBNull.Value;
            region.Value = map.RegionId;
            regionName.Value = (object?)map.RegionName ?? DBNull.Value;
            continent.Value = map.ContinentId;
            continentName.Value = (object?)map.ContinentName ?? DBNull.Value;
            floor.Value = map.DefaultFloor;

            Write(map.ContinentRect, left, top, right, bottom);
            Write(map.MapRect, mapLeft, mapBottom, mapRight, mapTop);

            await command.ExecuteNonQueryAsync(ct);
        }

        // Inside the same transaction as the rows it describes, so a save that fails partway
        // cannot leave the flag claiming a list that is not there.
        if (replaceAll) await WriteMetaAsync(connection, MapsComplete, "yes", ct);

        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Everything placed on one map — hearts, waypoints, landmarks, vistas — as the floor
    /// endpoint last published it, or null when this map has never been opened with a connection.
    ///
    /// Kept as the payload rather than as rows. These are read back whole and never queried
    /// across, the shape is nested three deep, and the source-generated serialiser already knows
    /// how to read and write it — so a table per kind would be work spent to make a worse
    /// version of what <see cref="Gw2JsonContext"/> does.
    /// </summary>
    public Task<MapDetail?> GetMapDetailAsync(int mapId, CancellationToken ct = default) =>
        ReadPayloadAsync("map_detail", "map_id", mapId, Gw2JsonContext.Default.MapDetail, ct);

    public Task SaveMapDetailAsync(int mapId, MapDetail detail, CancellationToken ct = default) =>
        WritePayloadAsync("map_detail", "map_id", mapId, detail, Gw2JsonContext.Default.MapDetail, ct);

    /// <summary>
    /// A continent's dimensions and zoom range, which the map screen needs before it can address
    /// a single tile. Two numbers, and without them a phone with a full tile cache still cannot
    /// draw anything.
    /// </summary>
    public Task<ContinentInfo?> GetContinentAsync(int continentId, CancellationToken ct = default) =>
        ReadPayloadAsync("continents", "id", continentId, Gw2JsonContext.Default.ContinentInfo, ct);

    public Task SaveContinentAsync(ContinentInfo continent, CancellationToken ct = default) =>
        WritePayloadAsync(
            "continents", "id", continent.Id, continent, Gw2JsonContext.Default.ContinentInfo, ct);

    /// <summary>
    /// One stored payload, or null for a miss and for anything that will not parse.
    ///
    /// A row written by an older version of a model is not an error worth surfacing: the caller
    /// asks the API instead, exactly as it would for a row that was never there.
    /// </summary>
    private async Task<T?> ReadPayloadAsync<T>(
        string table, string key, int id, JsonTypeInfo<T> typeInfo, CancellationToken ct)
        where T : class
    {
        await InitializeAsync(ct);

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT payload FROM {table} WHERE {key} = $id";
        command.Parameters.AddWithValue("$id", id);

        if (await command.ExecuteScalarAsync(ct) is not string payload) return null;

        try
        {
            return JsonSerializer.Deserialize(payload, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task WritePayloadAsync<T>(
        string table, string key, int id, T value, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        await InitializeAsync(ct);

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"INSERT OR REPLACE INTO {table} ({key}, payload) VALUES ($id, $payload)";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(value, typeInfo));

        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>One row, in the column order every map query above selects.</summary>
    private static MapInfo Read(SqliteDataReader reader) =>
        new(reader.GetInt32(0),
            reader.GetString(1),
            reader.GetInt32(3),
            reader.GetInt32(5),
            reader.GetInt32(7),
            Rect(reader, 8),
            Rect(reader, 12),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(6) ? null : reader.GetString(6));

    /// <summary>
    /// A rectangle back out of four columns, or null when the API never published one.
    ///
    /// Null is a real answer here and is carried as one: a map with no footprint cannot be drawn
    /// or pointed at, and a rectangle of zeroes is the far north-west corner of the continent
    /// rather than an absence.
    /// </summary>
    private static List<List<double>>? Rect(SqliteDataReader reader, int first) =>
        reader.IsDBNull(first) || reader.IsDBNull(first + 1) ||
        reader.IsDBNull(first + 2) || reader.IsDBNull(first + 3)
            ? null
            : [[reader.GetDouble(first), reader.GetDouble(first + 1)],
                [reader.GetDouble(first + 2), reader.GetDouble(first + 3)]];

    private static void Write(
        List<List<double>>? rect,
        SqliteParameter a, SqliteParameter b, SqliteParameter c, SqliteParameter d)
    {
        if (rect is [[var first, var second], [var third, var fourth]])
            (a.Value, b.Value, c.Value, d.Value) = (first, second, third, fourth);
        else
            (a.Value, b.Value, c.Value, d.Value) = (DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value);
    }

    // --- recipes -----------------------------------------------------------

    /// <summary>Recipe ids already stored, for working out what a fill still needs.</summary>
    public async Task<HashSet<int>> GetKnownRecipeIdsAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var known = new HashSet<int>();

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM recipes";

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            known.Add(reader.GetInt32(0));

        return known;
    }

    public async Task<int> CountRecipesAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM recipes";
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// Stores recipes and their ingredients.
    ///
    /// Disciplines are kept as a delimited string and matched with LIKE. A join table would be
    /// tidier, but a recipe belongs to a handful of disciplines at most and the query scans
    /// either way. The delimiters are not decoration: without them "Armorsmith" would match a
    /// future discipline whose name merely contains it.
    /// </summary>
    public async Task SaveRecipesAsync(
        IReadOnlyCollection<Recipe> recipes, CancellationToken ct = default)
    {
        if (recipes.Count == 0) return;
        await InitializeAsync(ct);

        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT OR REPLACE INTO recipes " +
                "(id, output_item_id, output_count, min_rating, type, disciplines, auto_learned) " +
                "VALUES ($id, $out, $count, $rating, $type, $disciplines, $auto)";

            var id = command.Parameters.Add("$id", SqliteType.Integer);
            var output = command.Parameters.Add("$out", SqliteType.Integer);
            var count = command.Parameters.Add("$count", SqliteType.Integer);
            var rating = command.Parameters.Add("$rating", SqliteType.Integer);
            var type = command.Parameters.Add("$type", SqliteType.Text);
            var disciplines = command.Parameters.Add("$disciplines", SqliteType.Text);
            var auto = command.Parameters.Add("$auto", SqliteType.Integer);

            foreach (var recipe in recipes)
            {
                id.Value = recipe.Id;
                output.Value = recipe.OutputItemId;
                count.Value = recipe.OutputItemCount;
                rating.Value = recipe.MinRating;
                type.Value = (object?)recipe.Type ?? DBNull.Value;
                disciplines.Value = "|" + string.Join("|", recipe.Disciplines ?? []) + "|";
                auto.Value = recipe.Flags?.Contains("AutoLearned") == true;
                await command.ExecuteNonQueryAsync(ct);
            }
        }

        await using (var clear = connection.CreateCommand())
        {
            clear.CommandText =
                "DELETE FROM recipe_ingredients WHERE recipe_id IN (" +
                string.Join(",", recipes.Select(r => r.Id)) + ")";
            await clear.ExecuteNonQueryAsync(ct);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO recipe_ingredients (recipe_id, item_id, count) " +
                "VALUES ($recipe, $item, $count)";

            var recipeId = command.Parameters.Add("$recipe", SqliteType.Integer);
            var itemId = command.Parameters.Add("$item", SqliteType.Integer);
            var count = command.Parameters.Add("$count", SqliteType.Integer);

            foreach (var recipe in recipes)
            foreach (var ingredient in (recipe.Ingredients ?? []).Where(i => i.IsItem))
            {
                recipeId.Value = recipe.Id;
                itemId.Value = ingredient.Id;
                count.Value = ingredient.Count;
                await command.ExecuteNonQueryAsync(ct);
            }
        }

        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// What makes an item, from the downloaded catalogue: the lowest-numbered recipe for it, which
    /// is the one the API's own search lists first. Null when nothing in the catalogue makes it —
    /// which is also every Mystic Forge recipe, since the API publishes none of them — or when
    /// the catalogue has not been downloaded.
    /// </summary>
    public async Task<RecipeShape?> RecipeShapeAsync(int outputItemId, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT r.output_count, i.item_id, i.count " +
            "FROM recipes r JOIN recipe_ingredients i ON i.recipe_id = r.id " +
            "WHERE r.id = (SELECT MIN(id) FROM recipes WHERE output_item_id = $item)";
        command.Parameters.AddWithValue("$item", outputItemId);

        var output = 0;
        var ingredients = new List<(int ItemId, int Count)>();

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            output = reader.GetInt32(0);
            ingredients.Add((reader.GetInt32(1), reader.GetInt32(2)));
        }

        return ingredients.Count == 0 ? null : new RecipeShape(output, ingredients);
    }

    /// <summary>
    /// Auto-learned recipes a discipline can make at or near a rating.
    ///
    /// Auto-learned only, because those are the ones every account has by virtue of its
    /// rating — the endpoint that reports individually unlocked recipes lists only what was
    /// bought or discovered, which is a handful. The caller applies the exact grey-recipe
    /// rule; this only narrows the field to something worth walking.
    /// </summary>
    public async Task<List<Recipe>> RecipesForAsync(
        string discipline, int rating, int lookBack, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var found = new Dictionary<int, (Recipe Recipe, List<RecipeIngredient> Ingredients)>();

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT r.id, r.output_item_id, r.output_count, r.min_rating, r.type, " +
            "       i.item_id, i.count " +
            "FROM recipes r " +
            "JOIN recipe_ingredients i ON i.recipe_id = r.id " +
            "WHERE r.auto_learned = 1 " +
            "  AND r.disciplines LIKE $discipline " +
            "  AND r.min_rating <= $rating " +
            "  AND r.min_rating > $floor";

        command.Parameters.AddWithValue("$discipline", "%|" + discipline + "|%");
        command.Parameters.AddWithValue("$rating", rating);
        command.Parameters.AddWithValue("$floor", rating - lookBack);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetInt32(0);

            if (!found.TryGetValue(id, out var entry))
            {
                entry = (new Recipe(
                    id,
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    null,
                    reader.GetInt32(3),
                    null,
                    null), new List<RecipeIngredient>());

                found[id] = entry;
            }

            entry.Ingredients.Add(
                new RecipeIngredient("Item", reader.GetInt32(5), reader.GetInt32(6)));
        }

        return found.Values
            .Select(e => e.Recipe with { Ingredients = e.Ingredients })
            .ToList();
    }

    /// <summary>
    /// Every recipe that can be completed outright from what the account is holding, with how
    /// many batches the materials cover.
    ///
    /// Done in SQL against two temporary tables rather than by reading thirteen thousand
    /// recipes into memory: the phone has the whole catalogue on disk, and the join is what a
    /// database is for. The HAVING is the crux — a recipe qualifies only when the number of
    /// ingredients that matched what is held equals the number it actually takes, so a recipe
    /// missing one of five ingredients is excluded rather than counted as four-fifths there.
    /// </summary>
    public async Task<List<MakeableRow>> MakeableAsync(
        IReadOnlyDictionary<int, int> held,
        IReadOnlyCollection<int> knownRecipeIds,
        int maxRating,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var rows = new List<MakeableRow>();

        if (held.Count == 0) return rows;

        await using var connection = await OpenAsync(ct);

        await using (var create = connection.CreateCommand())
        {
            create.CommandText =
                "CREATE TEMP TABLE held (item_id INTEGER PRIMARY KEY, count INTEGER NOT NULL);" +
                "CREATE TEMP TABLE known (id INTEGER PRIMARY KEY);";
            await create.ExecuteNonQueryAsync(ct);
        }

        await using (var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)
                     await connection.BeginTransactionAsync(ct))
        {
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO held (item_id, count) VALUES ($id, $count)";
                var id = insert.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Integer);
                var count = insert.Parameters.Add("$count", Microsoft.Data.Sqlite.SqliteType.Integer);

                foreach (var (itemId, quantity) in held)
                {
                    id.Value = itemId;
                    count.Value = quantity;
                    await insert.ExecuteNonQueryAsync(ct);
                }
            }

            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = "INSERT OR IGNORE INTO known (id) VALUES ($id)";
                var id = insert.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Integer);

                foreach (var recipeId in knownRecipeIds)
                {
                    id.Value = recipeId;
                    await insert.ExecuteNonQueryAsync(ct);
                }
            }

            await transaction.CommitAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT r.id, r.output_item_id, r.output_count, r.min_rating, r.disciplines, " +
            "       MIN(h.count / i.count) AS batches " +
            "FROM recipes r " +
            "JOIN recipe_ingredients i ON i.recipe_id = r.id " +
            "JOIN held h ON h.item_id = i.item_id AND h.count >= i.count " +
            "WHERE r.min_rating <= $rating " +
            "  AND (r.auto_learned = 1 OR r.id IN (SELECT id FROM known)) " +
            "GROUP BY r.id " +
            "HAVING COUNT(*) = (SELECT COUNT(*) FROM recipe_ingredients x WHERE x.recipe_id = r.id)";

        command.Parameters.AddWithValue("$rating", maxRating);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new MakeableRow(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetInt32(5)));

        return rows;
    }

    /// <summary>
    /// What this item goes into: every recipe that takes it as an ingredient.
    ///
    /// The API can answer this — /v2/recipes/search?input= — but the catalogue is already on
    /// disk and the answer is one index lookup, so it costs no request and works offline. The
    /// name of what comes out is joined here too, since the catalogue has it.
    /// </summary>
    public async Task<List<UsedInRow>> UsedInAsync(
        int itemId, int limit, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var rows = new List<UsedInRow>();

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT r.output_item_id, o.name, i.count, r.output_count, r.min_rating, r.disciplines " +
            "FROM recipe_ingredients i " +
            "JOIN recipes r ON r.id = i.recipe_id " +
            "LEFT JOIN items o ON o.id = r.output_item_id " +
            "WHERE i.item_id = $item " +
            // Soonest to reach first: a recipe at rating 0 is one you can already make.
            "ORDER BY r.min_rating, o.name " +
            "LIMIT $limit";

        command.Parameters.AddWithValue("$item", itemId);
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new UsedInRow(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? string.Empty : reader.GetString(5)));

        return rows;
    }

    /// <summary>How many recipes take this item at all, so a capped list can say so.</summary>
    public async Task<int> UsedInCountAsync(int itemId, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM recipe_ingredients WHERE item_id = $item";
        command.Parameters.AddWithValue("$item", itemId);

        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// Items already seen, matched by name.
    ///
    /// The API has no item search endpoint, so this is the only search available without
    /// downloading a hundred thousand item records onto a phone: everything the app has
    /// already looked up — storage, equipment, trades — is searchable, and that is where the
    /// items a player cares about come from anyway.
    /// </summary>
    public async Task<List<Item>> SearchItemsAsync(
        string term, int limit = 25, bool tradableOnly = false,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var found = new List<Item>();
        if (string.IsNullOrWhiteSpace(term)) return found;

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, icon, rarity FROM items
            WHERE name LIKE $term ESCAPE '\'
              AND ($all = 1 OR id IN (SELECT id FROM tradable))
            ORDER BY (name LIKE $prefix ESCAPE '\') DESC, length(name), name
            LIMIT $limit
            """;

        // Ranking earns its keep once the full catalogue is in and "ore" matches hundreds:
        // a name that starts with what was typed is likelier than one that merely contains it.
        //
        // The user's text is a literal, so LIKE's own wildcards have to be neutralised —
        // otherwise searching for "_" matches every single character.
        var escaped = term.Trim()
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_");

        command.Parameters.AddWithValue("$term", $"%{escaped}%");
        command.Parameters.AddWithValue("$prefix", $"{escaped}%");
        command.Parameters.AddWithValue("$all", tradableOnly ? 0 : 1);
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            found.Add(new Item(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));

        return found;
    }

    /// <summary>How many items the cache can search, for telling the user what it covers.</summary>
    public async Task<int> CountItemsAsync(CancellationToken ct = default)
    {
        // Settings asks this first on a fresh install, before anything else has touched the
        // cache; without it the screen opened on "no such table: items".
        await InitializeAsync(ct);

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM items";
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    // --- currencies --------------------------------------------------------

    public async Task<List<Currency>> GetCurrenciesAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        var currencies = new List<Currency>();

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, description, ord, icon FROM currencies ORDER BY ord";
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            currencies.Add(new Currency(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetInt32(3), reader.GetString(4)));

        return currencies;
    }

    public async Task SaveCurrenciesAsync(IReadOnlyCollection<Currency> currencies, CancellationToken ct = default)
    {
        if (currencies.Count == 0) return;
        await InitializeAsync(ct);

        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR REPLACE INTO currencies (id, name, description, ord, icon)
            VALUES ($id, $name, $description, $ord, $icon)
            """;
        var id = command.Parameters.Add("$id", SqliteType.Integer);
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var description = command.Parameters.Add("$description", SqliteType.Text);
        var ord = command.Parameters.Add("$ord", SqliteType.Integer);
        var icon = command.Parameters.Add("$icon", SqliteType.Text);

        foreach (var currency in currencies)
        {
            id.Value = currency.Id;
            name.Value = currency.Name;
            description.Value = currency.Description;
            ord.Value = currency.Order;
            icon.Value = currency.Icon;
            await command.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    // --- meta --------------------------------------------------------------

    private static async Task<string?> ReadMetaAsync(SqliteConnection connection, string key, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    private static async Task WriteMetaAsync(SqliteConnection connection, string key, string value, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO meta (key, value) VALUES ($key, $value)";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(ct);
    }
}
