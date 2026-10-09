using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Fetching what the account has unlocked, in two steps that cost very different amounts.
///
/// The split is the whole design. Counting a category is two requests of ids and nothing else —
/// cheap enough to do for all twelve when the screen opens, even for skins, where the total is
/// fourteen thousand integers. Naming the entries is a bulk fetch in chunks, so it happens only
/// for the one category somebody actually opened.
///
/// Both halves are cached for the life of the screen's session. Cosmetics are unlocked one at a
/// time by a person, not by the minute, and a pull-to-refresh is there for the moment after
/// somebody buys one.
/// </summary>
public sealed class UnlockService(Gw2Client api, ApiKeySession session)
{
    private readonly Dictionary<UnlockKind, UnlockTally> _tallies = [];
    private readonly Dictionary<UnlockKind, IReadOnlyList<UnlockEntry>> _entries = [];

    /// <summary>Whether the key can answer any of this at all.</summary>
    public bool CanRead => session.HasPermission(Gw2Permissions.Unlocks);

    /// <summary>Forgets everything, so the next look asks the API again.</summary>
    public void Forget()
    {
        _tallies.Clear();
        _entries.Clear();
    }

    /// <summary>
    /// How much of every category the account has.
    ///
    /// One category failing does not cost the rest. The endpoints are independent and an
    /// account with, say, no jade bot unlocks at all has been seen to answer oddly; a page that
    /// showed nothing because one row of twelve went wrong would be worse than a page with
    /// eleven honest numbers and one blank.
    /// </summary>
    public async Task<IReadOnlyList<UnlockTally>> TalliesAsync(CancellationToken ct)
    {
        var found = new List<UnlockTally>();

        foreach (var category in Unlocks.All)
        {
            if (_tallies.TryGetValue(category.Kind, out var cached))
            {
                found.Add(cached);
                continue;
            }

            try
            {
                var total = await api.GetIdsAsync(category.Catalogue, ct);
                var owned = await OwnedAsync(category, ct);

                var tally = new UnlockTally(category, owned.Count, total.Count);

                _tallies[category.Kind] = tally;
                found.Add(tally);
            }
            catch (Gw2ApiException)
            {
                // Counted as unknown rather than as zero. "0 of 0" is a statement about the
                // account; a category that could not be read is a statement about the request,
                // and the screen says so instead of inventing a number.
                found.Add(new UnlockTally(category, 0, 0));
            }
        }

        return found;
    }

    /// <summary>
    /// Every entry in one category, paired with whether the account has it.
    ///
    /// Dyes go their own way because they are the one category with something better than a
    /// name: /v2/colors carries the channels, so the list can show the actual colour instead of
    /// the word for it.
    /// </summary>
    public async Task<IReadOnlyList<UnlockEntry>> EntriesAsync(
        UnlockCategory category, CancellationToken ct)
    {
        if (!category.Browsable) return [];

        if (_entries.TryGetValue(category.Kind, out var cached)) return cached;

        var ids = await api.GetIdsAsync(category.Catalogue, ct);
        var owned = await OwnedAsync(category, ct);

        var catalogue = category.Kind == UnlockKind.Dye
            ? (await api.GetColorsAsync(ids, ct))
                .Select(dye => new UnlockEntry(dye.Id, dye.Name, false, Unlocks.Of(dye), Unlocks.RarityOf(dye)))
                .ToList()
            : (await api.GetUnlockItemsAsync(category.Catalogue, ids, ct))
                .Select(item => new UnlockEntry(
                    item.Id, item.Name is { Length: > 0 } name ? name : $"#{item.Id}", false))
                .ToList();

        var paired = Unlocks.Pair(catalogue, owned);

        _entries[category.Kind] = paired;

        return paired;
    }

    /// <summary>
    /// The account's ids for a category, whatever shape that endpoint answers in.
    ///
    /// Finishers are the only one that does not return plain ids; see
    /// <see cref="Gw2Client.GetAccountFinishersAsync"/> for what it returns instead.
    /// </summary>
    private async Task<IReadOnlyList<int>> OwnedAsync(UnlockCategory category, CancellationToken ct) =>
        category.Kind == UnlockKind.Finisher
            ? (await api.GetAccountFinishersAsync(ct)).Select(finisher => finisher.Id).ToList()
            : await api.GetAccountUnlocksAsync(category.Account, ct);
}
