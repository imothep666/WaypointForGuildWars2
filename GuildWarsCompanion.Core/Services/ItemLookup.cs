using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Turns item ids into item records: cache first, network only for the genuinely unknown.
///
/// Every screen that shows an item needs this — storage, equipment, trading post, alerts —
/// so it lives in one place rather than being copied into each service.
/// </summary>
public sealed class ItemLookup(Gw2Client client, Gw2Cache cache)
{
    public async Task<Dictionary<int, Item>> ResolveAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        var known = await cache.GetItemsAsync(ids, ct);

        var missing = ids.Distinct().Where(id => !known.ContainsKey(id)).ToList();
        if (missing.Count == 0) return known;

        var fetched = await client.GetItemsAsync(missing, ct);
        await cache.SaveItemsAsync(fetched, ct);
        foreach (var item in fetched)
            known[item.Id] = item;

        return known;
    }

    /// <summary>
    /// Items whose name contains the term, from the local catalogue.
    ///
    /// The API has no item search, so this reaches only as far as the catalogue has been
    /// filled — everything once Settings has downloaded it, and whatever the app has happened
    /// to see before that.
    /// </summary>
    public Task<List<Item>> SearchAsync(string term, int limit = 20, CancellationToken ct = default) =>
        cache.SearchItemsAsync(term, limit, tradableOnly: false, ct);

    /// <summary>Names only, for places that show a line of text rather than a tile.</summary>
    public async Task<Dictionary<int, string>> ResolveNamesAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct = default) =>
        (await ResolveAsync(ids, ct)).ToDictionary(pair => pair.Key, pair => pair.Value.Name);
}
