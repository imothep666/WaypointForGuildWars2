using Microsoft.Data.Sqlite;

namespace GuildWarsCompanion.Data;

/// <summary>
/// The account at one moment: each character's playtime and level, and the wallet. What a
/// session recap is the difference of.
/// </summary>
public sealed record LedgerPoint(
    DateTimeOffset At,
    IReadOnlyDictionary<string, (long Age, int Level)> Characters,
    IReadOnlyDictionary<int, long> Wallet)
{
    public long TotalAge => Characters.Values.Sum(character => character.Age);
}

/// <summary>
/// Ledger points, a fortnight of them. Wiped with the key like the activity store: another
/// account's playtime says nothing about this one.
/// </summary>
public sealed class LedgerStore(string databasePath)
{
    public static readonly TimeSpan Kept = TimeSpan.FromDays(14);

    private readonly string _connectionString =
        new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        if (!_initialized)
        {
            await _gate.WaitAsync(ct);
            try
            {
                if (!_initialized)
                {
                    await using var setup = new SqliteConnection(_connectionString);
                    await setup.OpenAsync(ct);
                    await using var command = setup.CreateCommand();
                    command.CommandText =
                        """
                        CREATE TABLE IF NOT EXISTS points (id INTEGER PRIMARY KEY AUTOINCREMENT, at INTEGER NOT NULL);
                        CREATE TABLE IF NOT EXISTS point_characters (
                            point_id INTEGER NOT NULL, name TEXT NOT NULL, age INTEGER NOT NULL, level INTEGER NOT NULL);
                        CREATE TABLE IF NOT EXISTS point_wallet (
                            point_id INTEGER NOT NULL, currency_id INTEGER NOT NULL, value INTEGER NOT NULL);
                        CREATE INDEX IF NOT EXISTS ix_points_at ON points (at);
                        """;
                    await command.ExecuteNonQueryAsync(ct);
                    _initialized = true;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task<DateTimeOffset?> LatestAtAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(at) FROM points";

        return await command.ExecuteScalarAsync(ct) is long at ? DateTimeOffset.FromUnixTimeSeconds(at) : null;
    }

    public async Task SaveAsync(LedgerPoint point, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        long id;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO points (at) VALUES ($at); SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$at", point.At.ToUnixTimeSeconds());
            id = (long)(await command.ExecuteScalarAsync(ct))!;
        }

        foreach (var (name, (age, level)) in point.Characters)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO point_characters (point_id, name, age, level) VALUES ($id, $name, $age, $level)";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$age", age);
            command.Parameters.AddWithValue("$level", level);
            await command.ExecuteNonQueryAsync(ct);
        }

        foreach (var (currency, value) in point.Wallet)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO point_wallet (point_id, currency_id, value) VALUES ($id, $currency, $value)";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$currency", currency);
            command.Parameters.AddWithValue("$value", value);
            await command.ExecuteNonQueryAsync(ct);
        }

        // What is older than a fortnight goes with each new point, so the file never grows past it.
        await using (var prune = connection.CreateCommand())
        {
            prune.CommandText =
                """
                DELETE FROM point_characters WHERE point_id IN (SELECT id FROM points WHERE at < $cut);
                DELETE FROM point_wallet WHERE point_id IN (SELECT id FROM points WHERE at < $cut);
                DELETE FROM points WHERE at < $cut;
                """;
            prune.Parameters.AddWithValue("$cut", (point.At - Kept).ToUnixTimeSeconds());
            await prune.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    /// <summary>Every point kept, oldest first.</summary>
    public async Task<List<LedgerPoint>> LoadAsync(CancellationToken ct = default)
    {
        var times = new Dictionary<long, DateTimeOffset>();
        var characters = new Dictionary<long, Dictionary<string, (long, int)>>();
        var wallets = new Dictionary<long, Dictionary<int, long>>();

        await using var connection = await OpenAsync(ct);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, at FROM points ORDER BY at, id";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetInt64(0);
                times[id] = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1));
                characters[id] = [];
                wallets[id] = [];
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT point_id, name, age, level FROM point_characters";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                if (characters.TryGetValue(reader.GetInt64(0), out var into))
                    into[reader.GetString(1)] = (reader.GetInt64(2), reader.GetInt32(3));
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT point_id, currency_id, value FROM point_wallet";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                if (wallets.TryGetValue(reader.GetInt64(0), out var into))
                    into[reader.GetInt32(1)] = reader.GetInt64(2);
        }

        return times
            .OrderBy(pair => pair.Value).ThenBy(pair => pair.Key)
            .Select(pair => new LedgerPoint(pair.Value, characters[pair.Key], wallets[pair.Key]))
            .ToList();
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM point_characters; DELETE FROM point_wallet; DELETE FROM points;";
        await command.ExecuteNonQueryAsync(ct);
    }
}
