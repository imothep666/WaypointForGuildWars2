using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The stored-only scope, which lets a screen draw what it read last time before asking for the
/// fresh answer. Its ways to go wrong are asking the network anyway, which is the wait it exists
/// to remove, and staying open after its screen's pass, which would freeze every later read.
/// </summary>
public sealed class OfflineReaderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"gw2-offline-{Guid.NewGuid():N}.db3");
    private readonly OfflineReader _reader;
    private int _fetched;

    public OfflineReaderTests() => _reader = new OfflineReader(new SnapshotStore(_path));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    private Task<List<string>> Fetch(CancellationToken _)
    {
        _fetched++;
        return Task.FromResult<List<string>>(["fresh"]);
    }

    private Task<Fetched<List<string>>> Read() =>
        _reader.ReadAsync("key", Gw2JsonContext.Default.ListString, Fetch);

    private async Task<Fetched<List<string>>> ReadStored()
    {
        using var _ = OfflineReader.StoredOnly();
        return await Read();
    }

    [Fact]
    public async Task Inside_the_scope_the_stored_copy_answers_and_the_network_is_not_asked()
    {
        await Read();

        var kept = await ReadStored();

        Assert.Equal(["fresh"], kept.Value);
        Assert.NotNull(kept.FetchedAt);
        Assert.Equal(1, _fetched);
    }

    [Fact]
    public async Task Nothing_stored_fails_as_an_offline_read_does()
    {
        var failure = await Assert.ThrowsAsync<Gw2ApiException>(ReadStored);

        Assert.Equal(Gw2ErrorKind.Network, failure.Kind);
        Assert.Equal(0, _fetched);
    }

    /// <summary>The scope ends with the method that opened it: the fresh pass after it must fetch.</summary>
    [Fact]
    public async Task The_scope_does_not_outlive_its_pass()
    {
        await Read();
        await ReadStored();

        var fresh = await Read();

        Assert.Null(fresh.FetchedAt);
        Assert.Equal(2, _fetched);
    }
}
