using Microsoft.Data.Sqlite;

namespace GuildWarsCompanion.Data;

/// <summary>
/// Remembers which alerts have already been raised, so a reminder fires once and not on
/// every poll.
///
/// Deliberately a separate database from <see cref="Gw2Cache"/>: that one is wiped whenever
/// ArenaNet ships a build, and losing this history would re-notify the user about every
/// trade they made in the last 90 days.
/// </summary>
public sealed class AlertStore(string databasePath)
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
                CREATE TABLE IF NOT EXISTS seen (
                    key TEXT PRIMARY KEY, seen_at TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS watchers (
                    name TEXT PRIMARY KEY, baselined_at TEXT NOT NULL);
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
    /// True once a watcher has taken its opening snapshot. Until then everything it finds is
    /// pre-existing history, not news — see <see cref="MarkBaselinedAsync"/>.
    /// </summary>
    public async Task<bool> IsBaselinedAsync(string watcher, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM watchers WHERE name = $name";
        command.Parameters.AddWithValue("$name", watcher);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    public async Task MarkBaselinedAsync(string watcher, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR REPLACE INTO watchers (name, baselined_at) VALUES ($name, $at)";
        command.Parameters.AddWithValue("$name", watcher);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Of the supplied keys, the ones never recorded before.</summary>
    public async Task<HashSet<string>> UnseenAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        var unseen = new HashSet<string>(keys);
        if (keys.Count == 0) return unseen;

        await using var connection = await OpenAsync(ct);
        foreach (var chunk in keys.Distinct().Chunk(400))
        {
            await using var command = connection.CreateCommand();
            var parameters = new string[chunk.Length];
            for (var i = 0; i < chunk.Length; i++)
            {
                parameters[i] = "$k" + i;
                command.Parameters.AddWithValue("$k" + i, chunk[i]);
            }

            command.CommandText = "SELECT key FROM seen WHERE key IN (" + string.Join(",", parameters) + ")";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                unseen.Remove(reader.GetString(0));
        }

        return unseen;
    }

    public async Task MarkSeenAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        if (keys.Count == 0) return;

        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO seen (key, seen_at) VALUES ($key, $at)";
        var key = command.Parameters.Add("$key", SqliteType.Text);
        var at = command.Parameters.Add("$at", SqliteType.Text);
        var stamp = DateTimeOffset.UtcNow.ToString("O");

        foreach (var value in keys.Distinct())
        {
            key.Value = value;
            at.Value = stamp;
            await command.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    /// <summary>Drops records older than the window so the table stays small.</summary>
    public async Task PruneAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM seen WHERE seen_at < $cutoff";
        command.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.Subtract(olderThan).ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Forgets everything — used when the user signs a different key in.</summary>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM seen; DELETE FROM watchers;";
        await command.ExecuteNonQueryAsync(ct);
    }
}
