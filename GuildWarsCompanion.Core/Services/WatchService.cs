using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>A watch with the market beside it, ready to show.</summary>
public sealed record WatchRow(
    long Id, int ItemId, string Name, string? IconUrl, string Condition, string Current, bool IsMet)
{
    /// <summary>Brass once the price is where you wanted it.</summary>
    public Color EdgeColor => IsMet ? Theme.Brass : Theme.EmptySlot;
}

/// <summary>
/// An item the user can start watching, found in the local cache, with the market as it stands —
/// the baseline a threshold is set against. Both prices are null when the market could not be
/// read (offline, say); the watch can still be set.
/// </summary>
public sealed record WatchCandidate(
    int ItemId, string Name, string? IconUrl, string? Rarity, long? Asking = null, long? Offered = null)
{
    public Color RarityColor => Theme.RarityColor(Rarity);

    /// <summary>What sellers ask, beside a search result; blank when nobody is selling.</summary>
    public string AskingText => Asking is { } asking ? Coin.Format(asking) : string.Empty;

    /// <summary>Both sides of the market in a sentence, said once the item is chosen.</summary>
    public string Baseline => (Asking, Offered) switch
    {
        ({ } ask, { } offer) => L.F("Sellers ask {0}, buyers offer {1}.", Coin.Format(ask), Coin.Format(offer)),
        ({ } ask, null) => L.F("Sellers ask {0}. Nobody is buying.", Coin.Format(ask)),
        (null, { } offer) => L.F("Nobody is selling. Buyers offer {0}.", Coin.Format(offer)),
        _ => L.T("No market price to hand.")
    };
}

/// <summary>
/// Price watches: the one thing the game itself cannot tell you while you are away.
///
/// Prices are unauthenticated, so this works with no API key at all — the watch list is the
/// only feature in the app that does.
/// </summary>
public sealed class WatchService(Gw2Client client, WatchStore store, Gw2Cache cache, ItemLookup lookup)
{
    public async Task<IReadOnlyList<WatchRow>> ListAsync(CancellationToken ct = default)
    {
        var watches = await store.ListAsync(ct);
        if (watches.Count == 0) return [];

        // A watch keeps the name its item had when it was set, which may be in the language the
        // phone was in then, so the item's name now wins. Usually from the store; a renamed item
        // is one request for the lot.
        Dictionary<int, Item> named;
        try
        {
            named = await lookup.ResolveAsync(watches.Select(w => w.ItemId).ToList(), ct);
        }
        catch (Gw2ApiException)
        {
            named = [];
        }

        var prices = (await client.GetPricesAsync(watches.Select(w => w.ItemId), ct))
            .ToDictionary(p => p.Id);

        var rows = new List<WatchRow>();

        foreach (var watch in watches)
        {
            var market = prices.GetValueOrDefault(watch.ItemId);
            var current = watch.WatchSellPrice ? market?.Sells?.UnitPrice : market?.Buys?.UnitPrice;

            rows.Add(new WatchRow(
                watch.Id,
                watch.ItemId,
                named.GetValueOrDefault(watch.ItemId)?.Name ?? watch.Name,
                watch.Icon,
                PriceRules.Describe(watch),
                current is null ? L.T("no market") : Coin.Format(current.Value),
                PriceRules.IsMet(watch, current)));
        }

        return rows;
    }

    /// <summary>
    /// Items to choose from, matched by name against the local catalogue — the API has no
    /// item search, so the cache is the index.
    ///
    /// Restricted to what the trading post actually trades. A watch on a soulbound item could
    /// never fire, so offering one is worse than not finding it: the user would set it and
    /// wait for a notification that cannot arrive.
    ///
    /// Each comes with today's prices, read live in one request: away from the game the app is
    /// the only place to see what the market is doing before naming a threshold.
    /// </summary>
    public async Task<IReadOnlyList<WatchCandidate>> SearchAsync(
        string term, CancellationToken ct = default)
    {
        var items = await lookup.SearchTradableAsync(term, 25, ct);
        var prices = await PricesAsync(items.Select(i => i.Id), ct);

        return items
            .Select(item => WithPrice(
                new WatchCandidate(item.Id, item.Name, item.Icon, item.Rarity),
                prices.GetValueOrDefault(item.Id)))
            .ToList();
    }

    /// <summary>Prices for the given items, or none at all when the market cannot be reached.</summary>
    private async Task<Dictionary<int, ItemPrice>> PricesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        try
        {
            return (await client.GetPricesAsync(ids, ct)).ToDictionary(p => p.Id);
        }
        catch (Gw2ApiException)
        {
            // Offline or the API is down: the search still works, only the baseline is missing.
            return [];
        }
    }

    private static WatchCandidate WithPrice(WatchCandidate item, ItemPrice? price) =>
        item with { Asking = price?.Sells?.UnitPrice, Offered = price?.Buys?.UnitPrice };

    /// <summary>
    /// How many items the search can reach. Tradable ones once the catalogue is filled,
    /// otherwise whatever the app has happened to see — the difference is exactly what the
    /// download in Settings is for, so the screen says which number it is quoting.
    /// </summary>
    public async Task<(int Count, bool IsCatalogue)> SearchableCountAsync(
        CancellationToken ct = default)
    {
        var tradable = await cache.CountTradableAsync(ct);

        return tradable > 0
            ? (tradable, true)
            : (await cache.CountItemsAsync(ct), false);
    }

    public Task AddAsync(
        WatchCandidate item, bool watchSellPrice, bool below, long threshold,
        CancellationToken ct = default) =>
        store.AddAsync(item.ItemId, item.Name, item.IconUrl, watchSellPrice, below, threshold, ct);

    public Task RemoveAsync(long id, CancellationToken ct = default) => store.RemoveAsync(id, ct);

    /// <summary>
    /// One item as a candidate, for a watch started from its own page, with the market as it
    /// stands — the numbers a threshold is set against. Null when the API does not know the item.
    /// </summary>
    public async Task<WatchCandidate?> CandidateAsync(int itemId, CancellationToken ct = default)
    {
        if (await client.GetItemDetailAsync(itemId, ct) is not { } item) return null;

        var prices = await PricesAsync([itemId], ct);

        return WithPrice(
            new WatchCandidate(item.Id, item.Name, item.Icon, item.Rarity),
            prices.GetValueOrDefault(itemId));
    }

}
