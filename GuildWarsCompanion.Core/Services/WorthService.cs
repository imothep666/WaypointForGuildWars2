using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>The gem exchange, as it priced this account's own coin.</summary>
public sealed record GemView(string Buys, string? SellsBack, string Note, bool HasQuote);

/// <summary>What the account adds up to, and what that total leaves out.</summary>
public sealed record WorthView(
    CoinPurse Total, IReadOnlyList<WorthRow> Parts, string Caveat, bool HasTotal);

/// <summary>One line of the breakdown, with its share of the whole.</summary>
public sealed record WorthRow(WorthPart Part, string ShareText)
{
    public string Name => Part.Name;

    public string Where => Part.Where;

    public string ValueText => Part.ValueText;
}

/// <summary>
/// One number for the whole account: coin, everything in storage, and the part of it sitting
/// at the trading post.
///
/// The pieces already exist separately — the storage scan values every stack, the orders
/// screen knows what is listed. What was missing is that nobody added them up, and the answer
/// to "how am I doing" was spread over four screens.
///
/// The scan is the expensive half, so a snapshot already in memory is reused rather than
/// fetched again; opening the wallet after the storage screen costs almost nothing.
/// </summary>
public sealed class WorthService(
    AccountDataService data, StorageService storage, TradingPostService market, Gw2Client client)
{
    private const int CoinCurrencyId = 1;

    /// <summary>
    /// What this account's coin is worth in gems, and what it would be worth coming back.
    ///
    /// Two requests, both public, and both asking about the real amounts rather than a sample:
    /// the exchange prices the trade you ask about, and prices a small one worse, so a rate
    /// taken from someone else's hundred gold would not be this account's rate.
    /// </summary>
    public async Task<GemView> GetGemsAsync(CancellationToken ct = default)
    {
        var wallet = await data.GetWalletAsync(ct);
        var coin = wallet.Value.FirstOrDefault(row => row.CurrencyId == CoinCurrencyId)?.RawValue ?? 0;

        if (coin <= 0)
            return new GemView(string.Empty, null, "Nothing in the wallet to convert.", false);

        var buying = await client.GetGemsForCoinAsync(coin, ct);

        if (!GemExchange.IsQuoted(buying.Quantity, buying.Text))
            return new GemView(string.Empty, null, GemExchange.TooSmall(buying.Text), false);

        // The way back is quoted for exactly the gems the first trade would give, so the two
        // halves are the same size and the loss between them is real.
        var selling = await client.GetCoinForGemsAsync(buying.Quantity, ct);

        var back = GemExchange.IsQuoted(selling.Quantity, selling.Text)
            ? GemExchange.SellsBack(
                buying.Quantity, selling.Quantity,
                GemExchange.RoundTrip(coin, selling.Quantity))
            : null;

        return new GemView(
            GemExchange.Buys(coin, buying.Quantity),
            back,
            "Quoted for your own coin: the exchange prices each trade separately and prices a " +
            "small one worse. It moves through the day, which is why gems are left out of the " +
            "total above.",
            true);
    }

    public async Task<WorthView> GetAsync(CancellationToken ct = default)
    {
        var wallet = await data.GetWalletAsync(ct);
        var coin = wallet.Value.FirstOrDefault(row => row.CurrencyId == CoinCurrencyId)?.RawValue ?? 0;

        if (!storage.HasSnapshot)
            await storage.RefreshAsync(ct);

        var overview = storage.Overview();

        TradingPostService.MarketHoldings held;
        try
        {
            held = await market.GetHoldingsAsync(ct);
        }
        catch (Gw2ApiException)
        {
            // A key without the trading post permission still has a wallet and a bank.
            held = new TradingPostService.MarketHoldings(0, 0, 0, 0, Priced: false);
        }

        var parts = NetWorth.Parts(
            coin, overview.RawTotal, held.ListedValue,
            held.BuyOrderCoin, held.DeliveryCoin, held.DeliveryValue);

        var total = NetWorth.Total(parts);

        var rows = parts
            .Select(part => new WorthRow(part, NetWorth.Share(part.Value, total)))
            .ToList();

        return new WorthView(
            // A purse rather than a formatted string: coin is written in three metals
            // everywhere else on this screen, and one number in flat gold reads as a
            // different kind of thing.
            new CoinPurse(total),
            rows,
            NetWorth.Caveat(overview.Priced, storage.Unreadable),
            total > 0);
    }
}
