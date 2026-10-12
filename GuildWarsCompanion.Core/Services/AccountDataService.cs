using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>One currency line, already formatted for display.</summary>
public sealed record WalletRow(
    int CurrencyId, string Name, string Description, string IconUrl, string Amount, long RawValue);

/// <summary>One stack in material storage, with what it is worth if sold.</summary>
public sealed record MaterialRow(
    int ItemId, string Name, string? IconUrl, int Count, string? Rarity, long? StackValue)
{
    public string CountText => Count.ToString("N0");
    public Color RarityColor => Theme.RarityColor(Rarity);

    /// <summary>Blank for account-bound materials: they have no market to be worth anything on.</summary>
    public string ValueText => StackValue is null ? string.Empty : Coin.Format(StackValue.Value);
}

/// <summary>A material storage tab. Derives from List so CollectionView can group on it.</summary>
public sealed class MaterialSection(string name, IEnumerable<MaterialRow> rows) : List<MaterialRow>(rows)
{
    public string Name { get; } = name;

    public string Summary
    {
        get
        {
            var value = this.Sum(r => r.StackValue ?? 0);
            return value > 0
                ? L.F("{0:N0} items · {1}", this.Sum(r => r.Count), Coin.Format(value))
                : L.F("{0:N0} items", this.Sum(r => r.Count));
        }
    }
}

/// <summary>
/// Material storage plus what it is worth. Two totals, because they answer different
/// questions: what you would get for dumping it all right now, and what patience is worth.
/// </summary>
public sealed record MaterialsView(
    IReadOnlyList<MaterialSection> Sections,
    CoinPurse InstantValue,
    CoinPurse ListedValue,
    string ValueNote,
    bool HasValue);

/// <summary>
/// Turns raw account endpoints into display rows. The account half is fetched every time
/// (it changes constantly); the static half — currency and item metadata — is resolved
/// through <see cref="Gw2Cache"/> so a refresh usually costs exactly one request.
/// </summary>
public sealed class AccountDataService(
    Gw2Client client, Gw2Cache cache, ItemLookup items, OfflineReader offline)
{
    private List<MaterialCategory>? _categories;
    private bool _buildChecked;

    // --- wallet ------------------------------------------------------------

    public async Task<Fetched<IReadOnlyList<WalletRow>>> GetWalletAsync(CancellationToken ct = default)
    {
        await EnsureCacheFreshAsync(ct);

        var wallet = await offline.ReadAsync(
            "account/wallet", Gw2JsonContext.Default.ListWalletEntry, client.GetWalletAsync, ct);

        var currencies = await GetCurrenciesAsync(ct);

        var rows = wallet.Value
            .Select(entry => (entry, currency: currencies.GetValueOrDefault(entry.Id)))
            .Where(x => x.currency is not null)
            .OrderBy(x => x.currency!.Order)
            .Select(x => new WalletRow(
                x.entry.Id,
                x.currency!.Name,
                x.currency.Description,
                x.currency.Icon,
                Coin.FormatAmount(x.entry.Id, x.entry.Value),
                x.entry.Value))
            .ToList();

        return new Fetched<IReadOnlyList<WalletRow>>(rows, wallet.FetchedAt);
    }


    private async Task<Dictionary<int, Currency>> GetCurrenciesAsync(CancellationToken ct)
    {
        var cached = await cache.GetCurrenciesAsync(ct);
        if (cached.Count == 0)
        {
            cached = await client.GetCurrenciesAsync(ct);
            await cache.SaveCurrenciesAsync(cached, ct);
        }

        return cached.ToDictionary(c => c.Id);
    }

    // --- material storage --------------------------------------------------

    public async Task<Fetched<MaterialsView>> GetMaterialsAsync(CancellationToken ct = default)
    {
        await EnsureCacheFreshAsync(ct);

        var materials = await offline.ReadAsync(
            "account/materials", Gw2JsonContext.Default.ListAccountMaterial,
            client.GetAccountMaterialsAsync, ct);

        var stacked = materials.Value.Where(m => m.Count > 0).ToList();

        // One category lookup per app run — the tabs only change with an expansion.
        _categories ??= (await offline.ReadAsync(
            "materials", Gw2JsonContext.Default.ListMaterialCategory,
            client.GetMaterialCategoriesAsync, ct)).Value;

        var categoryNames = _categories.ToDictionary(c => c.Id);

        var ids = stacked.Select(m => m.Id).ToList();

        // Offline, the cache answers for whatever it already knows and the rest fall back to
        // their id. Better a list with a few unnamed rows than no list.
        Dictionary<int, Item> resolved;
        try
        {
            resolved = await items.ResolveAsync(ids, ct);
        }
        catch (Gw2ApiException)
        {
            resolved = await cache.GetItemsAsync(ids, ct);
        }

        // Prices are never cached: a valuation is only worth reading if it is current, and a
        // day-old total presented as money would be worse than no total at all.
        var priced = true;
        Dictionary<int, ItemPrice> prices;
        try
        {
            prices = (await client.GetPricesAsync(ids, ct)).ToDictionary(p => p.Id);
        }
        catch (Gw2ApiException)
        {
            prices = [];
            priced = false;
        }

        long instant = 0, listed = 0, unpriced = 0;

        var sections = stacked
            .GroupBy(m => m.Category)
            .OrderBy(g => categoryNames.GetValueOrDefault(g.Key)?.Order ?? int.MaxValue)
            .Select(group => new MaterialSection(
                categoryNames.GetValueOrDefault(group.Key)?.Name ?? L.F("Category {0}", group.Key),
                group
                    .Select(m =>
                    {
                        var market = prices.GetValueOrDefault(m.Id);
                        var bid = market?.Buys?.UnitPrice;
                        var ask = market?.Sells?.UnitPrice;

                        long? stackValue = null;
                        if (bid is > 0)
                        {
                            stackValue = PriceRules.AfterFees((long)bid.Value * m.Count);
                            instant += stackValue.Value;
                        }

                        if (ask is > 0)
                            listed += PriceRules.AfterFees((long)ask.Value * m.Count);
                        else if (bid is null or 0)
                            unpriced++;

                        return new MaterialRow(
                            m.Id,
                            resolved.GetValueOrDefault(m.Id)?.Name ?? L.F("Item {0}", m.Id),
                            resolved.GetValueOrDefault(m.Id)?.Icon,
                            m.Count,
                            resolved.GetValueOrDefault(m.Id)?.Rarity,
                            stackValue);
                    })
                    .OrderByDescending(r => r.StackValue ?? 0)
                    .ThenByDescending(r => r.Count)))
            .ToList();

        var note = priced
            ? unpriced > 0
                ? L.F("After the 15% trading post fee. {0} account-bound materials have no market value.", unpriced)
                : L.T("After the 15% trading post fee.")
            : L.T("Values need a connection — this is what you are holding, not what it is worth.");

        var view = new MaterialsView(
            sections, new CoinPurse(instant), new CoinPurse(listed), note,
            priced && (instant > 0 || listed > 0));

        return new Fetched<MaterialsView>(view, materials.FetchedAt);
    }


    // --- cache freshness ---------------------------------------------------

    /// <summary>
    /// Compares /v2/build against the cached build once per app run and wipes stale static
    /// data. A failure here is non-fatal: serving slightly stale item names beats an error.
    /// </summary>
    private async Task EnsureCacheFreshAsync(CancellationToken ct)
    {
        if (_buildChecked) return;
        _buildChecked = true;

        try
        {
            var build = await client.GetBuildAsync(ct);
            if (await cache.InvalidateIfStaleAsync(build.Id, ct))
                _categories = null;
        }
        catch (Gw2ApiException)
        {
            _buildChecked = false;
        }
    }
}
