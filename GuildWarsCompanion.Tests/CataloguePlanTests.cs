using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// What a catalogue fill fetches, and in what order.
///
/// Both properties matter for a job that runs to 371 requests on a phone: refetching what is
/// already stored would double an already long download, and the order decides whether an
/// interrupted fill left the app more useful than it found it.
/// </summary>
public class CataloguePlanTests
{
    private static IReadOnlySet<int> Set(params int[] ids) => ids.ToHashSet();

    [Fact]
    public void Only_what_is_missing_is_fetched()
    {
        var missing = CataloguePlan.Missing([1, 2, 3, 4], Set(2, 4), Set());

        Assert.Equal([1, 3], missing);
    }

    [Fact]
    public void Nothing_missing_means_no_requests()
    {
        Assert.Empty(CataloguePlan.Missing([1, 2], Set(1, 2), Set()));
    }

    [Fact]
    public void Tradable_items_are_fetched_first()
    {
        // The whole point: a fill that is cancelled or loses its connection part way should
        // still have made the trading post search work, because that is what it was for.
        var missing = CataloguePlan.Missing([10, 20, 30, 40], Set(), Set(30, 40));

        Assert.Equal([30, 40, 10, 20], missing);
    }

    [Fact]
    public void Order_is_stable_within_each_group()
    {
        // A resumed fill should pick up predictably rather than reshuffling what is left.
        var missing = CataloguePlan.Missing([50, 10, 30, 20], Set(), Set(30, 10));

        Assert.Equal([10, 30, 20, 50], missing);
    }

    [Fact]
    public void A_repeated_id_is_fetched_once()
    {
        Assert.Equal([7], CataloguePlan.Missing([7, 7, 7], Set(), Set()));
    }

    [Fact]
    public void Tradable_ids_the_account_already_has_do_not_come_back()
    {
        // Being tradable promotes an id up the queue; it does not put it back on it.
        Assert.Empty(CataloguePlan.Missing([5], Set(5), Set(5)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(200, 1)]
    [InlineData(201, 2)]
    [InlineData(74_057, 371)]   // the real catalogue, and the number measured against the API
    public void Request_count_matches_the_batch_ceiling(int ids, int batches) =>
        Assert.Equal(batches, CataloguePlan.BatchCount(ids));

    [Fact]
    public void Batches_never_exceed_what_the_api_accepts()
    {
        // 200 ids per request is the API's own limit; a batch over it is rejected outright.
        Assert.Equal(200, CataloguePlan.BatchSize);

        var missing = CataloguePlan.Missing(Enumerable.Range(1, 450), Set(), Set());
        var batches = missing.Chunk(CataloguePlan.BatchSize).ToList();

        Assert.Equal(3, batches.Count);
        Assert.All(batches, batch => Assert.True(batch.Length <= CataloguePlan.BatchSize));
        Assert.Equal(450, batches.Sum(b => b.Length));
    }
}
