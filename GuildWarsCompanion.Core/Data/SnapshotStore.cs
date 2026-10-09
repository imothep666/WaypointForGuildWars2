using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Data.Sqlite;

namespace GuildWarsCompanion.Data;

/// <summary>
/// The last successful response from each endpoint, so the app has something to show when
/// there is no connection.
///
/// Raw API payloads are stored, not the view models built from them. View models carry
/// colours and formatted strings — they are shaped for a screen, not for keeping — and
/// serialising one would either fail on <c>Color</c> or freeze this month's presentation into
/// next month's data. Payloads are also exactly what the source-generated serialiser already
/// knows how to read and write.
///
/// This is per-account data: it is cleared when a different API key is signed in, alongside
/// the alert and activity stores.
/// </summary>
public sealed class SnapshotStore(string databasePath)
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
                CREATE TABLE IF NOT EXISTS snapshots (
                    key TEXT PRIMARY KEY,
                    payload TEXT NOT NULL,
                    fetched_at INTEGER NOT NULL);
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
    /// Records a successful response. A failure to write is swallowed: losing tomorrow's
    /// offline copy is not worth failing the request that just succeeded.
    /// </summary>
    public async Task SaveAsync<T>(
        string key, T payload, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload, typeInfo);

            await using var connection = await OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO snapshots (key, payload, fetched_at) VALUES ($key, $payload, $at)
                ON CONFLICT(key) DO UPDATE SET payload = $payload, fetched_at = $at
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$payload", json);
            command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or NotSupportedException)
        {
            // Nothing the user can act on, and the live data is already in hand.
        }
    }

    /// <summary>
    /// The stored response and when it was taken, or null if there is none — or if what was
    /// stored can no longer be read, which happens when a model changes shape between
    /// releases. A stale schema is treated as no data rather than as a crash.
    /// </summary>
    public async Task<(T Value, DateTimeOffset FetchedAt)?> LoadAsync<T>(
        string key, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
    {
        try
        {
            await using var connection = await OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload, fetched_at FROM snapshots WHERE key = $key";
            command.Parameters.AddWithValue("$key", key);

            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;

            var value = JsonSerializer.Deserialize(reader.GetString(0), typeInfo);
            if (value is null) return null;

            return (value, DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1)));
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Stores a payload and reports when it last <em>changed</em> — not when it was last
    /// fetched.
    ///
    /// The difference is the whole point. The Wizard's Vault endpoints keep serving the
    /// previous cycle's objectives until the player logs into the game, so "fetched a moment
    /// ago" says nothing about whether the data is current. "Unchanged since before the last
    /// reset" says everything.
    ///
    /// Identical payloads leave the timestamp alone, so it always marks the last real change.
    /// </summary>
    public async Task<DateTimeOffset> RecordAsync<T>(
        string key, T payload, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        try
        {
            var json = JsonSerializer.Serialize(payload, typeInfo);

            await using var connection = await OpenAsync(ct);

            await using (var read = connection.CreateCommand())
            {
                read.CommandText = "SELECT payload, fetched_at FROM snapshots WHERE key = $key";
                read.Parameters.AddWithValue("$key", key);

                await using var reader = await read.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct) && reader.GetString(0) == json)
                    return DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1));
            }

            await using var write = connection.CreateCommand();
            write.CommandText =
                """
                INSERT INTO snapshots (key, payload, fetched_at) VALUES ($key, $payload, $at)
                ON CONFLICT(key) DO UPDATE SET payload = $payload, fetched_at = $at
                """;
            write.Parameters.AddWithValue("$key", key);
            write.Parameters.AddWithValue("$payload", json);
            write.Parameters.AddWithValue("$at", now.ToUnixTimeSeconds());
            await write.ExecuteNonQueryAsync(ct);

            return now;
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or NotSupportedException)
        {
            // Treated as "changed just now": better to say nothing than to claim data is
            // stale because the bookkeeping failed.
            return now;
        }
    }

    /// <summary>
    /// Forgets everything. Called when a different key is signed in: another account's wallet
    /// is not a useful thing to show while offline.
    /// </summary>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM snapshots";
        await command.ExecuteNonQueryAsync(ct);
    }
}
