using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>One search result: an item, how much of it you have, and where.</summary>
public sealed record FoundItem(
    int ItemId, string Name, string? IconUrl, string? Rarity,
    int Total, string CountText, string LocationText, string ValueText)
{
    public Color RarityColor => Theme.RarityColor(Rarity);
}

/// <summary>One place, and what it is holding.</summary>
public sealed record PlaceTotal(string Name, string ItemsText, string ValueText, long RawValue);

/// <summary>
/// Everything the account is holding, broken down by where it sits. Two numbers that a
/// player actually acts on: how much stuff, and what the tradable part of it is worth.
/// </summary>
public sealed record StorageOverview(
    IReadOnlyList<PlaceTotal> Places,
    string TotalText,
    string Note,
    bool HasAnything,
    long RawTotal = 0,
    bool Priced = false);

/// <summary>
/// Storage across the whole account: material storage, the bank, shared slots, every
/// character's bags, and what each character is wearing.
///
/// Two things make this worth a service of its own. The API answers "what is in this place"
/// but never "where is this item", so the only way to answer the question a player actually
/// asks is to read every place and invert it. And because the item names have to be resolved
/// to search them, the search covers <em>what you own</em> rather than what the app happens
/// to have looked at before — which is what makes it trustworthy.
///
/// The snapshot is held in memory: one read answers every subsequent search instantly, and
/// storage does not change while the phone is in your hand.
/// </summary>
public sealed class StorageService(Gw2Client client, ItemLookup items, OfflineReader offline)
{
    private List<Holding>? _holdings;
    private Dictionary<int, Item> _items = [];
    private Dictionary<int, long> _unitValue = [];
    private List<string> _unavailable = [];

    /// <summary>Whether the last read managed to price anything.</summary>
    private bool _priced;

    public bool HasSnapshot => _holdings is not null;

    /// <summary>
    /// Reads every place, then resolves names and prices for everything found.
    ///
    /// A place the key cannot reach is recorded and skipped rather than failing the screen:
    /// a key without "characters" should still be able to search the bank.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var (holdings, unavailable) = await ReadPlacesAsync(ct);

        _holdings = StorageTally.Collate(holdings).ToList();
        _unavailable = unavailable;

        var ids = _holdings.Select(h => h.ItemId).Distinct().ToList();
        _items = await items.ResolveAsync(ids, ct);
        _unitValue = await ValueAsync(ids, ct);
        _priced = _unitValue.Count > 0;
    }

    /// <summary>
    /// How many of each item the account holds, everywhere, and nothing else — no names, no
    /// prices. For a screen that only needs to count, like the goals on Today: pricing every
    /// stack in the bank on each refresh to answer "how much ore do I have" would be most of the
    /// cost of the screen for none of its answer.
    ///
    /// The places the key cannot reach are named, so a count can say it is partial.
    /// </summary>
    public async Task<(IReadOnlyDictionary<int, int> Counts, IReadOnlyList<string> Unreadable)> CountAsync(
        CancellationToken ct = default)
    {
        var (holdings, unavailable) = await ReadPlacesAsync(ct);

        var counts = holdings
            .GroupBy(holding => holding.ItemId)
            .ToDictionary(group => group.Key, group => group.Sum(h => h.Count));

        return (counts, unavailable);
    }

    /// <summary>Every place things are kept, read once, each one allowed to fail on its own.</summary>
    private async Task<(List<Holding> Holdings, List<string> Unavailable)> ReadPlacesAsync(CancellationToken ct)
    {
        var holdings = new List<Holding>();
        var unavailable = new List<string>();

        await ReadAsync("material storage", unavailable, async () =>
        {
            foreach (var material in (await offline.ReadAsync(
                         "account/materials", Gw2JsonContext.Default.ListAccountMaterial,
                         client.GetAccountMaterialsAsync, ct)).Value)
                holdings.Add(new Holding(StoragePlace.Materials, string.Empty, material.Id, material.Count));
        });

        await ReadAsync("the bank", unavailable, async () =>
        {
            foreach (var slot in (await offline.ReadAsync(
                         "account/bank", Gw2JsonContext.Default.ListStorageSlot,
                         client.GetBankAsync, ct)).Value)
                if (slot is not null)
                    holdings.Add(new Holding(StoragePlace.Bank, string.Empty, slot.Id, slot.Count));
        });

        await ReadAsync("shared slots", unavailable, async () =>
        {
            foreach (var slot in (await offline.ReadAsync(
                         "account/inventory", Gw2JsonContext.Default.ListStorageSlot,
                         client.GetSharedInventoryAsync, ct)).Value)
                if (slot is not null)
                    holdings.Add(new Holding(StoragePlace.Shared, string.Empty, slot.Id, slot.Count));
        });

        await ReadAsync("your characters", unavailable, async () =>
        {
            foreach (var character in (await offline.ReadAsync(
                         "characters", Gw2JsonContext.Default.ListCharacter,
                         client.GetCharactersAsync, ct)).Value)
            {
                foreach (var slot in character.Bags?
                             .Where(bag => bag is not null)
                             .SelectMany(bag => bag!.Inventory ?? []) ?? [])
                    if (slot is not null)
                        holdings.Add(new Holding(
                            StoragePlace.Character, character.Name, slot.Id, slot.Count));

                // Worn gear is not in a bag, and answering "none anywhere" for something the
                // character is wearing would be plainly wrong.
                //
                // Only what is actually worn: the equipment array also carries the contents
                // of every equipment template, so a legendary drawn from the armory appears
                // once per tab and counting all of them would invent copies.
                foreach (var piece in (character.Equipment ?? []).Where(p => p.IsWorn))
                    holdings.Add(new Holding(StoragePlace.Equipped, character.Name, piece.Id, 1));
            }
        });

        return (holdings, unavailable);
    }

    /// <summary>
    /// What one of each item is worth to sell right now, after fees. Buy orders rather than
    /// asking prices: this is what the pile is worth, not what patience might get for it.
    /// Untradable items simply have no entry.
    /// </summary>
    private async Task<Dictionary<int, long>> ValueAsync(List<int> ids, CancellationToken ct)
    {
        var value = new Dictionary<int, long>();

        try
        {
            foreach (var price in await client.GetPricesAsync(ids, ct))
                if (price.Buys?.UnitPrice is > 0)
                    value[price.Id] = price.Buys.UnitPrice;
        }
        catch (Gw2ApiException)
        {
            // A valuation is a bonus on this screen; losing it must not lose the search.
        }

        return value;
    }

    private static async Task ReadAsync(string what, List<string> unavailable, Func<Task> read)
    {
        try
        {
            await read();
        }
        catch (Gw2ApiException ex) when (ex.Kind is Gw2ErrorKind.MissingScope or Gw2ErrorKind.NotFound)
        {
            unavailable.Add(what);
        }
    }

    /// <summary>
    /// Items you own whose name contains <paramref name="term"/>, best match first.
    ///
    /// Matched against the resolved names of things actually held, so "none" means none —
    /// not "none that the app has seen before".
    /// </summary>
    public IReadOnlyList<FoundItem> Find(string term, int limit = 40)
    {
        if (_holdings is null || term.Trim().Length < 2) return [];

        var needle = term.Trim();

        return _holdings
            .GroupBy(h => h.ItemId)
            .Select(group => (group, name: _items.GetValueOrDefault(group.Key)?.Name))
            .Where(x => x.name is not null &&
                        x.name.Contains(needle, StringComparison.OrdinalIgnoreCase))
            // A name that starts with the term is the likelier target than one that merely
            // contains it, and a shorter name is likelier than a longer one.
            .OrderBy(x => x.name!.StartsWith(needle, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.name!.Length)
            .ThenBy(x => x.name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(x => Describe(x.group.Key, x.group.ToList()))
            .ToList();
    }

    private FoundItem Describe(int itemId, List<Holding> holdings)
    {
        var item = _items.GetValueOrDefault(itemId);
        var total = StorageTally.Total(holdings);
        var unit = _unitValue.GetValueOrDefault(itemId);

        return new FoundItem(
            itemId,
            item?.Name ?? $"Item {itemId}",
            item?.Icon,
            item?.Rarity,
            total,
            total == 1 ? "1" : total.ToString("N0"),
            StorageTally.Describe(holdings),
            unit > 0 ? Coin.Format(PriceRules.AfterFees(unit * total)) : string.Empty);
    }

    /// <summary>Where everything is, and what the tradable part of it is worth.</summary>
    public StorageOverview Overview()
    {
        if (_holdings is null)
            return new StorageOverview([], string.Empty, string.Empty, false);

        long grand = 0;
        var untradable = 0;

        var places = _holdings
            .GroupBy(h => (h.Place, h.Where))
            .Select(group =>
            {
                long value = 0;
                foreach (var holding in group)
                {
                    var unit = _unitValue.GetValueOrDefault(holding.ItemId);
                    if (unit > 0) value += PriceRules.AfterFees(unit * holding.Count);
                    else untradable++;
                }

                grand += value;
                var count = group.Sum(h => h.Count);

                return (group.Key.Place, Total: new PlaceTotal(
                    StorageTally.PlaceHeading(group.Key.Place, group.Key.Where),
                    $"{count:N0} {(count == 1 ? "item" : "items")}",
                    value > 0 ? Coin.Format(value) : string.Empty,
                    value));
            })
            .OrderBy(x => x.Place)
            .ThenByDescending(x => x.Total.RawValue)
            .Select(x => x.Total)
            .ToList();

        // A total of nothing is not the same as a total that could not be worked out, and
        // showing "0c" for a full account reads as a bug or a robbery.
        if (!_priced)
            return new StorageOverview(
                places,
                string.Empty,
                "Values need a connection — this is what you are holding, not what it is worth.",
                places.Count > 0);

        var note = "Sell-order value of everything tradable, after the 15% trading post fee.";
        if (untradable > 0)
            note += $" {untradable:N0} stacks are account-bound and have no market value.";
        if (_unavailable.Count > 0)
            note += $" Could not read {string.Join(", ", _unavailable)} — your API key is " +
                    "missing a permission.";

        return new StorageOverview(
            places, Coin.Format(grand), note, places.Count > 0, grand, Priced: true);
    }

    /// <summary>
    /// Where one item is, phrased as a sentence, or null if storage has not been read this
    /// run. "None anywhere" is a real answer and is returned as such — it is what a player
    /// wants to know before buying another.
    /// </summary>
    public string? WhereIs(int itemId) =>
        _holdings is null
            ? null
            : StorageTally.Describe(_holdings.Where(h => h.ItemId == itemId));

    /// <summary>
    /// How many of one item the account holds, or null if storage has not been read this run.
    ///
    /// Null and zero are different answers and the difference matters: zero means "go and buy
    /// some", null means "the app does not know yet".
    /// </summary>
    public int? HeldCount(int itemId) =>
        _holdings is null
            ? null
            : _holdings.Where(h => h.ItemId == itemId).Sum(h => h.Count);

    /// <summary>How many distinct items the snapshot covers, for telling the user what was searched.</summary>
    public int DistinctItems => _holdings?.Select(h => h.ItemId).Distinct().Count() ?? 0;

    /// <summary>Places the key could not open, so a total can admit what it is missing.</summary>
    public IReadOnlyList<string> Unreadable => _unavailable;

    /// <summary>
    /// How much of everything the account holds, totalled across every place.
    ///
    /// Crafting does not care where a stack sits — materials come out of the bank and the
    /// material store alike — so the places are summed rather than listed.
    /// </summary>
    public IReadOnlyDictionary<int, int> Holdings =>
        _holdings?
            .GroupBy(holding => holding.ItemId)
            .ToDictionary(group => group.Key, group => group.Sum(h => h.Count))
        ?? new Dictionary<int, int>();
}
