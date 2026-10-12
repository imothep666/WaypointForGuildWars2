using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Turns item ids into item records: cache first, network only for the genuinely unknown.
///
/// Every screen that shows an item needs this — storage, equipment, trading post, alerts —
/// so it lives in one place rather than being copied into each service.
///
/// An item stored in another language counts as unknown, so a phone whose language changed
/// renames what it shows as it shows it rather than waiting for a catalogue download.
/// </summary>
public sealed class ItemLookup(Gw2Client client, Gw2Cache cache)
{
    public async Task<Dictionary<int, Item>> ResolveAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        var known = await cache.GetItemsAsync(ids, ct, currentOnly: true);

        var missing = ids.Distinct().Where(id => !known.ContainsKey(id)).ToList();
        if (missing.Count == 0) return known;

        // Whatever is held for the rest, in whichever language it was stored.
        var held = await cache.GetItemsAsync(missing, ct);

        List<Item> fetched;
        try
        {
            fetched = await client.GetItemsAsync(missing, ct);
        }
        catch (Gw2ApiException) when (held.Count == missing.Count)
        {
            // Offline, and every one of them has a name already: the old language's name is
            // still a name, and the screen is better for it than for an error.
            foreach (var (id, item) in held) known[id] = item;

            return known;
        }

        await cache.SaveItemsAsync(fetched, ct);
        foreach (var item in fetched)
            known[item.Id] = item;

        await RememberEnglishAsync(fetched.Where(item => !held.ContainsKey(item.Id)), ct);

        return known;
    }

    /// <summary>
    /// The English names of items seen for the first time, so a search typed in English finds
    /// them too. A row already held has its English name from when it was first stored.
    ///
    /// Best effort: a name that will not come is a search that misses one item until the
    /// catalogue is downloaded, not a reason to fail the screen that asked.
    /// </summary>
    private async Task RememberEnglishAsync(IEnumerable<Item> firstSeen, CancellationToken ct)
    {
        if (client.Language == Gw2Language.English) return;

        var ids = firstSeen.Select(item => item.Id).ToList();
        if (ids.Count == 0) return;

        try
        {
            await cache.SaveEnglishItemNamesAsync(await client.GetEnglishItemsAsync(ids, ct), ct);
        }
        catch (Gw2ApiException)
        {
        }
    }

    /// <summary>
    /// Items whose name contains the term, from the local catalogue.
    ///
    /// The API has no item search, so this reaches only as far as the catalogue has been
    /// filled — everything once Settings has downloaded it, and whatever the app has happened
    /// to see before that.
    /// </summary>
    public async Task<List<Item>> SearchAsync(string term, int limit = 20, CancellationToken ct = default) =>
        await InThisLanguageAsync(await cache.SearchItemsAsync(term, limit, tradableOnly: false, ct), ct);

    /// <summary>The same, among what the trading post trades.</summary>
    public async Task<List<Item>> SearchTradableAsync(string term, int limit = 25, CancellationToken ct = default) =>
        await InThisLanguageAsync(await cache.SearchItemsAsync(term, limit, tradableOnly: true, ct), ct);

    /// <summary>
    /// Search results named in the phone's language, in the order the search ranked them.
    ///
    /// The catalogue can still be in the language it was downloaded in, and a German phone
    /// searching it would otherwise be shown English names until the whole of it is fetched
    /// again. A page of results is one request. Offline, they keep the names they have.
    /// </summary>
    private async Task<List<Item>> InThisLanguageAsync(List<Item> found, CancellationToken ct)
    {
        if (found.Count == 0) return found;

        try
        {
            var named = await ResolveAsync(found.Select(item => item.Id).ToList(), ct);

            return found.Select(item => named.GetValueOrDefault(item.Id, item)).ToList();
        }
        catch (Gw2ApiException)
        {
            return found;
        }
    }

    /// <summary>
    /// What these items are called in English, where that is stored: for the wiki, whose files
    /// and pages are named in English whatever language the phone is in.
    /// </summary>
    public Task<Dictionary<int, string>> EnglishNamesAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct = default) =>
        cache.GetEnglishItemNamesAsync(ids, ct);

    /// <summary>Names only, for places that show a line of text rather than a tile.</summary>
    public async Task<Dictionary<int, string>> ResolveNamesAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct = default) =>
        (await ResolveAsync(ids, ct)).ToDictionary(pair => pair.Key, pair => pair.Value.Name);
}
