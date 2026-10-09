using Microsoft.Data.Sqlite;

namespace GuildWarsCompanion.Data;

/// <summary>An item somebody is working towards, and how many of it.</summary>
public sealed record Goal(long Id, int ItemId, string Name, string? Icon, int Quantity);

/// <summary>
/// The pinned goals, in their own database for the same reason as the watch list: the alerts
/// database is wiped when another key signs in, and what somebody is saving up for is theirs, not
/// the key's.
/// </summary>
public sealed class GoalStore(string databasePath)
{
    private readonly string _connectionString =
        new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    /// <summary>Moves on every pin and unpin, so Today can tell its goals are out of date without
    /// asking the database.</summary>
    public int Version { get; private set; }

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
                CREATE TABLE IF NOT EXISTS goals (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    item_id INTEGER NOT NULL UNIQUE,
                    name TEXT NOT NULL,
                    icon TEXT,
                    quantity INTEGER NOT NULL DEFAULT 1);
                """;
            await command.ExecuteNonQueryAsync(ct);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>In the order they were pinned: the oldest goal is usually the one being worked on.</summary>
    public async Task<List<Goal>> ListAsync(CancellationToken ct = default)
    {
        var goals = new List<Goal>();

        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, item_id, name, icon, quantity FROM goals ORDER BY id";

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            goals.Add(new Goal(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4)));

        return goals;
    }

    public async Task<bool> IsPinnedAsync(int itemId, CancellationToken ct = default) =>
        await QuantityAsync(itemId, ct) is not null;

    /// <summary>How many of the item the goal is for; null when it is not pinned.</summary>
    public async Task<int?> QuantityAsync(int itemId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT quantity FROM goals WHERE item_id = $item";
        command.Parameters.AddWithValue("$item", itemId);

        return await command.ExecuteScalarAsync(ct) is long quantity ? (int)quantity : null;
    }

    /// <summary>Sets how many, at least one: a goal of none is unpinning, which is its own button.</summary>
    public async Task SetQuantityAsync(int itemId, int quantity, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE goals SET quantity = $quantity WHERE item_id = $item";
        command.Parameters.AddWithValue("$item", itemId);
        command.Parameters.AddWithValue("$quantity", Math.Max(1, quantity));
        await command.ExecuteNonQueryAsync(ct);

        Version++;
    }

    /// <summary>One goal per item: pinning it again keeps the first.</summary>
    public async Task PinAsync(int itemId, string name, string? icon, int quantity = 1, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR IGNORE INTO goals (item_id, name, icon, quantity) VALUES ($item, $name, $icon, $quantity)";
        command.Parameters.AddWithValue("$item", itemId);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$icon", (object?)icon ?? DBNull.Value);
        command.Parameters.AddWithValue("$quantity", Math.Max(1, quantity));
        await command.ExecuteNonQueryAsync(ct);

        Version++;
    }

    public async Task UnpinAsync(int itemId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM goals WHERE item_id = $item";
        command.Parameters.AddWithValue("$item", itemId);
        await command.ExecuteNonQueryAsync(ct);

        Version++;
    }
}
