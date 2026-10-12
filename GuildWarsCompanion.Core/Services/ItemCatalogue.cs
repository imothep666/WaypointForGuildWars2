using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>What the catalogue holds right now.</summary>
/// <param name="OtherLanguage">Items still named in the language the phone was in before, which
/// a download renames.</param>
public sealed record CatalogueStatus(int Known, int Tradable, int Recipes, int OtherLanguage);

/// <summary>How far through a fill we are.</summary>
public sealed record CatalogueProgress(int Done, int Total);

/// <summary>
/// Fills the local item catalogue from the API, so searching means searching everything
/// rather than only what the app has happened to look at.
///
/// Built to be interrupted. Every batch is committed as it arrives, and the next run diffs
/// against what is stored, so a lost connection, a cancelled download or a closed app all
/// resume rather than restart. That is also why the item table is no longer wiped when the
/// game build changes — see <see cref="Gw2Cache.InvalidateIfStaleAsync"/>.
///
/// Cost, measured rather than guessed: 74,057 items, 371 requests, 4.6 MB over the wire with
/// gzip, about 12 MB stored.
/// </summary>
public sealed class ItemCatalogue(Gw2Client client, Gw2Cache cache)
{
    /// <summary>Waits between retries if we are rate limited anyway — shared IP, say.</summary>
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)];

    // Pacing lives in Gw2Client now: the API meters per IP, so a limit that only covered
    // this one job left every other screen free to trip it.

    public async Task<CatalogueStatus> StatusAsync(CancellationToken ct = default) =>
        new(await cache.CountItemsAsync(ct),
            await cache.CountTradableAsync(ct),
            await cache.CountRecipesAsync(ct),
            await cache.CountItemsInOtherLanguageAsync(ct));

    /// <summary>
    /// Fetches everything not already held. Reports after each batch so the screen can show
    /// movement across what is a minutes-long job.
    /// </summary>
    public async Task<int> FillAsync(
        IProgress<CatalogueProgress>? progress, CancellationToken ct = default)
    {
        // The market's list first: it is one request, it decides the fetch order below, and
        // it is what makes a price-watch search offer only things a watch could fire on.
        var tradable = await client.GetTradableItemIdsAsync(ct);
        await cache.SaveTradableAsync(tradable, ct);

        var everything = await client.GetItemIdsAsync(ct);
        var known = await cache.GetKnownItemIdsAsync(ct);

        var tradableSet = tradable.ToHashSet();
        var missing = CataloguePlan.Missing(everything, known, tradableSet);
        progress?.Report(new CatalogueProgress(0, missing.Count));

        // English names beside the local ones, so a search typed from a guide or the wiki finds
        // the item on a phone set to German. Nothing to do in English, and nothing for a
        // catalogue filled before there were languages: its names are the English ones already.
        IReadOnlyList<int> english = client.Language == Gw2Language.English
            ? []
            : CataloguePlan.Missing(everything, await cache.GetItemIdsWithEnglishAsync(ct), tradableSet);

        // Recipes are filled in the same pass. They are what turns "your Jeweler is 36" into
        // "make these five things next", and without them the crafting guide has nothing to
        // work from — the account endpoint reports only recipes learned from an item.
        var missingRecipes = await MissingRecipesAsync(ct);
        var total = missing.Count + english.Count + missingRecipes.Count;

        progress?.Report(new CatalogueProgress(0, total));

        var done = 0;

        foreach (var batch in missing.Chunk(CataloguePlan.BatchSize))
        {
            ct.ThrowIfCancellationRequested();

            var items = await FetchAsync(batch, client.GetItemsAsync, ct);
            await cache.SaveItemsAsync(items, ct);

            done += batch.Length;
            progress?.Report(new CatalogueProgress(done, total));
        }

        // After the pass above, so every row it names is there to take its English name.
        foreach (var batch in english.Chunk(CataloguePlan.BatchSize))
        {
            ct.ThrowIfCancellationRequested();

            var items = await FetchAsync(batch, client.GetEnglishItemsAsync, ct);
            await cache.SaveEnglishItemNamesAsync(items, ct);

            done += batch.Length;
            progress?.Report(new CatalogueProgress(done, total));
        }

        foreach (var batch in missingRecipes.Chunk(CataloguePlan.BatchSize))
        {
            ct.ThrowIfCancellationRequested();

                await cache.SaveRecipesAsync(await client.GetRecipesAsync(batch, ct), ct);

            done += batch.Length;
            progress?.Report(new CatalogueProgress(done, total));
        }

        return total;
    }

    /// <summary>Recipe ids not stored yet, in id order so a resumed fill picks up predictably.</summary>
    private async Task<IReadOnlyList<int>> MissingRecipesAsync(CancellationToken ct)
    {
        var everything = await client.GetRecipeIdsAsync(ct);
        var known = await cache.GetKnownRecipeIdsAsync(ct);

        return everything.Distinct().Where(id => !known.Contains(id)).Order().ToList();
    }

    /// <summary>
    /// One batch, paced, and backing off if the API rate-limits us regardless — the bucket is
    /// per IP, so something else on the same connection can spend it.
    ///
    /// Giving up on the first 429 would strand a fill two thirds of the way through.
    /// </summary>
    private static async Task<List<Item>> FetchAsync(
        int[] batch, Func<IEnumerable<int>, CancellationToken, Task<List<Item>>> fetch, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await fetch(batch, ct);
            }
            catch (Gw2ApiException ex)
                when (ex.Kind == Gw2ErrorKind.RateLimited && attempt < Backoff.Length)
            {
                await Task.Delay(Backoff[attempt], ct);
            }
        }
    }
}
