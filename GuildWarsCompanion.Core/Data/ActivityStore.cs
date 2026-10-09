using Microsoft.Data.Sqlite;

namespace GuildWarsCompanion.Data;

/// <summary>Which character the account has been playing, and when we noticed.</summary>
public sealed record ActiveCharacter(string Name, int Level, DateTimeOffset DetectedAt);

/// <summary>
/// Remembers each character's total playtime between runs.
///
/// The API exposes no "currently logged in" character and no position — only a per-character
/// <c>age</c> in seconds. Snapshotting it turns that into an answer: whichever character's
/// playtime grew since the last look is the one being played. The resolution is however
/// often the app refreshes, which is honest about what it can know.
/// </summary>
public sealed class ActivityStore(string databasePath)
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
                CREATE TABLE IF NOT EXISTS playtime (
                    name TEXT PRIMARY KEY, age_seconds INTEGER NOT NULL, seen_at TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS active (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    name TEXT NOT NULL, level INTEGER NOT NULL, detected_at TEXT NOT NULL);
                """;
            await command.ExecuteNonQueryAsync(ct);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Last known playtime per character, in seconds.</summary>
    public async Task<Dictionary<string, long>> LoadPlaytimeAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<string, long>();

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, age_seconds FROM playtime";
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result[reader.GetString(0)] = reader.GetInt64(1);

        return result;
    }

    public async Task SavePlaytimeAsync(
        IReadOnlyCollection<(string Name, long AgeSeconds)> characters, CancellationToken ct = default)
    {
        if (characters.Count == 0) return;

        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR REPLACE INTO playtime (name, age_seconds, seen_at) VALUES ($name, $age, $at)";
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var age = command.Parameters.Add("$age", SqliteType.Integer);
        var at = command.Parameters.Add("$at", SqliteType.Text);
        var stamp = DateTimeOffset.UtcNow.ToString("O");

        foreach (var character in characters)
        {
            name.Value = character.Name;
            age.Value = character.AgeSeconds;
            at.Value = stamp;
            await command.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<ActiveCharacter?> LoadActiveAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, level, detected_at FROM active WHERE id = 1";
        await using var reader = await command.ExecuteReaderAsync(ct);

        return await reader.ReadAsync(ct)
            ? new ActiveCharacter(reader.GetString(0), reader.GetInt32(1),
                DateTimeOffset.Parse(reader.GetString(2)))
            : null;
    }

    public async Task SaveActiveAsync(string name, int level, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR REPLACE INTO active (id, name, level, detected_at)
            VALUES (1, $name, $level, $at)
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$level", level);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Another account's playtime is not this one's; wipe on a key change.</summary>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM playtime; DELETE FROM active;";
        await command.ExecuteNonQueryAsync(ct);
    }
}
