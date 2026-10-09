using GuildWarsCompanion.Services;
using Microsoft.Data.Sqlite;

namespace GuildWarsCompanion.Data;

/// <summary>
/// The watch list, kept in its own database.
///
/// Deliberately not in the alerts database: that one is wiped when a different API key is
/// signed in, and a price you are waiting for has nothing to do with which account you are
/// looking at. Prices are unauthenticated, so watches work with no key at all.
/// </summary>
public sealed class WatchStore(string databasePath)
{
    private readonly string _connectionString =
        new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        await InitializeAsync(ct);
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _gate.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS watches (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    item_id INTEGER NOT NULL,
                    name TEXT NOT NULL,
                    icon TEXT,
                    sell_price INTEGER NOT NULL,
                    below INTEGER NOT NULL,
                    threshold INTEGER NOT NULL,
                    triggered INTEGER NOT NULL DEFAULT 0);
                """;
            await command.ExecuteNonQueryAsync(ct);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<PriceWatch>> ListAsync(CancellationToken ct = default)
    {
        var watches = new List<PriceWatch>();

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, item_id, name, icon, sell_price, below, threshold, triggered FROM watches ORDER BY name";

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            watches.Add(new PriceWatch(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5),
                reader.GetInt64(6),
                reader.GetBoolean(7)));

        return watches;
    }

    public async Task AddAsync(
        int itemId, string name, string? icon, bool watchSellPrice, bool below, long threshold,
        CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO watches (item_id, name, icon, sell_price, below, threshold, triggered)
            VALUES ($item, $name, $icon, $sell, $below, $threshold, 0)
            """;
        command.Parameters.AddWithValue("$item", itemId);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$icon", (object?)icon ?? DBNull.Value);
        command.Parameters.AddWithValue("$sell", watchSellPrice);
        command.Parameters.AddWithValue("$below", below);
        command.Parameters.AddWithValue("$threshold", threshold);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveAsync(long id, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM watches WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Records whether a watch is currently met. The transition is what matters: a watch
    /// notifies when it becomes true and re-arms when the price moves back, rather than
    /// nagging every half hour for as long as the condition holds.
    /// </summary>
    public async Task SetTriggeredAsync(long id, bool triggered, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE watches SET triggered = $triggered WHERE id = $id";
        command.Parameters.AddWithValue("$triggered", triggered);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(ct);
    }
}
