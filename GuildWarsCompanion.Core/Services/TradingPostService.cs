using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>One open order, with the market beside it so the number means something.</summary>
public sealed record OrderRow(
    int ItemId,
    string ItemName,
    string? IconUrl,
    string? Rarity,
    string Quantity,
    string UnitPrice,
    string Total,
    string? MarketNote,
    bool IsBeaten)
{
    public Color RarityColor => Theme.RarityColor(Rarity);

    /// <summary>Brass when the market has moved past you; quiet otherwise.</summary>
    public Color NoteColor => IsBeaten ? Theme.Brass : Theme.MistColor;
}

/// <summary>Selling and Buying, or Sold and Bought — the screens share this shape.</summary>
public sealed class OrderSection(string name, string summary, long coin, IEnumerable<OrderRow> rows)
    : List<OrderRow>(rows)
{
    /// <summary>English, always: the shell tells the sides apart by it.</summary>
    public string Name { get; } = name;

    /// <summary>The name as the screen shows it, in the phone's language.</summary>
    public string Title => L.T(Name);

    public string Summary { get; } = summary;

    /// <summary>What every order or trade in it comes to, at its own price.</summary>
    public CoinPurse Coin { get; } = new(coin);
}

/// <summary>What the trading post is holding.</summary>
public sealed record DeliveryView(CoinPurse Coins, bool HasCoins, IReadOnlyList<OrderRow> Items);

/// <summary>
/// The trading post, from the player's side: what is listed, what has closed, and what is
/// waiting to be collected.
///
/// Commerce responses are cached by the API for five minutes, so a refresh inside that
/// window returns the same data — worth knowing before chasing a number that will not move.
/// </summary>
public sealed class TradingPostService(Gw2Client client, ItemLookup lookup, OfflineReader offline)
{
    // --- open orders -------------------------------------------------------

    public async Task<IReadOnlyList<OrderSection>> GetOrdersAsync(CancellationToken ct = default)
    {
        // Your own open orders and closed trades are worth keeping: they are the part of
        // the trading post that is about you rather than about the market.
        var sells = await OrdersAsync("current/sells", "sells", current: true, ct);
        var buys = await OrdersAsync("current/buys", "buys", current: true, ct);

        var all = sells.Concat(buys).ToList();
        if (all.Count == 0) return [];

        var ids = all.Select(t => t.ItemId).ToList();

        // Names come from the cache when there is no connection; the orders themselves are
        // the point of the screen.
        Dictionary<int, Item> items;
        try
        {
            items = await lookup.ResolveAsync(ids, ct);
        }
        catch (Gw2ApiException)
        {
            items = [];
        }

        // Prices are never kept — what the market is doing now is the one thing a stored copy
        // cannot tell you. Without them the screen still shows what you have listed and at
        // what price, which is most of the answer.
        Dictionary<int, ItemPrice> prices;
        try
        {
            prices = (await client.GetPricesAsync(ids, ct)).ToDictionary(p => p.Id);
        }
        catch (Gw2ApiException)
        {
            prices = [];
        }

        // The queue is only worth a request where it answers something: an order that has
        // been passed, or one at the best price that still is not moving.
        var (books, capped) = await BooksAsync(sells, buys, prices, ct);

        var sections = new List<OrderSection>();

        if (sells.Count > 0)
            sections.Add(Section("Selling", sells, items, prices, books, capped, selling: true));

        if (buys.Count > 0)
            sections.Add(Section("Buying", buys, items, prices, books, capped, selling: false));

        return sections;
    }

    /// <summary>
    /// How many order books one screen will pull. Books are heavy — a busy item runs to
    /// thousands of price steps — so this is a real cost, and going over it is said out loud
    /// rather than passed off as a complete answer.
    /// </summary>
    private const int BooksPerScreen = 25;

    /// <summary>
    /// Fetches the order books worth having: orders the market has passed, and orders sitting
    /// at the best price. An order asking well above the market is not queued behind anything,
    /// it is waiting for a different market, and depth would say nothing about it.
    /// </summary>
    private async Task<(Dictionary<int, ItemListings> Books, bool Capped)> BooksAsync(
        IReadOnlyList<Transaction> sells, IReadOnlyList<Transaction> buys,
        Dictionary<int, ItemPrice> prices, CancellationToken ct)
    {
        bool Contested(Transaction order, bool selling)
        {
            var best = selling
                ? prices.GetValueOrDefault(order.ItemId)?.Sells?.UnitPrice
                : prices.GetValueOrDefault(order.ItemId)?.Buys?.UnitPrice;

            return best is not null &&
                   (OrderBook.IsBeaten(order.Price, best, selling) || best == order.Price);
        }

        var contested = sells.Where(order => Contested(order, true))
            .Concat(buys.Where(order => Contested(order, false)))
            // The biggest stakes first, since this is capped.
            .OrderByDescending(order => order.Price * order.Quantity)
            .Select(order => order.ItemId)
            .Distinct()
            .ToList();

        var wanted = contested.Take(BooksPerScreen).ToList();
        var capped = contested.Count > wanted.Count;

        if (wanted.Count == 0) return ([], false);

        try
        {
            return ((await client.GetListingsAsync(wanted, ct)).ToDictionary(book => book.Id),
                capped);
        }
        catch (Gw2ApiException)
        {
            // The orders and the undercut flag survive without it; only the queue is lost.
            return ([], false);
        }
    }

    /// <summary>
    /// One side of the ledger, with what needs attention at the top. Sorting by price alone
    /// buries a beaten order in the middle of a long list, which is where it goes unnoticed.
    /// </summary>
    private static OrderSection Section(
        string name, IReadOnlyList<Transaction> orders, Dictionary<int, Item> items,
        Dictionary<int, ItemPrice> prices, Dictionary<int, ItemListings> books, bool capped,
        bool selling)
    {
        var rows = orders
            .Select(order => ToRow(order, items, prices, books, selling))
            .OrderByDescending(row => row.IsBeaten)
            .ToList();

        var coin = orders.Sum(order => order.Price * order.Quantity);
        var summary = OrderBook.Summarise(orders.Count, coin, rows.Count(row => row.IsBeaten), selling, capped);

        return new OrderSection(name, summary, coin, rows);
    }

    /// <summary>What the trading post is holding of yours, in coin.</summary>
    public sealed record MarketHoldings(
        long BuyOrderCoin, long ListedValue, long DeliveryCoin, long DeliveryValue, bool Priced);

    /// <summary>
    /// The part of an account that is sitting at the trading post rather than in a bag.
    ///
    /// Three different kinds of money, and they are not worth the same:
    /// <list type="bullet">
    /// <item>Coin in buy orders is recoverable in full — cancelling a buy order refunds it.</item>
    /// <item>Listed items are valued like any other stack, at what a buyer is offering now
    /// after fees, not at the price you are asking. What you hope for is not what you have.</item>
    /// <item>Coin already delivered is simply coin.</item>
    /// </list>
    /// </summary>
    public async Task<MarketHoldings> GetHoldingsAsync(CancellationToken ct = default)
    {
        var sells = await OrdersAsync("current/sells", "sells", current: true, ct);
        var buys = await OrdersAsync("current/buys", "buys", current: true, ct);

        var box = (await offline.ReadAsync(
            "commerce/delivery", Gw2JsonContext.Default.DeliveryBox,
            client.GetDeliveryAsync, ct)).Value;

        var waiting = box.Items ?? [];
        var orderCoin = buys.Sum(order => order.Price * order.Quantity);

        var ids = sells.Select(order => order.ItemId)
            .Concat(waiting.Select(item => item.Id))
            .Distinct()
            .ToList();

        Dictionary<int, ItemPrice> prices;
        try
        {
            prices = ids.Count == 0 ? [] : (await client.GetPricesAsync(ids, ct)).ToDictionary(p => p.Id);
        }
        catch (Gw2ApiException)
        {
            // Without prices the coin is still known; the item side is not, and saying so
            // beats totalling a fraction of an account as though it were all of it.
            return new MarketHoldings(orderCoin, 0, box.Coins, 0, Priced: false);
        }

        long Worth(int itemId, int count)
        {
            var unit = prices.GetValueOrDefault(itemId)?.Buys?.UnitPrice ?? 0;
            return unit > 0 ? PriceRules.AfterFees(unit * count) : 0;
        }

        return new MarketHoldings(
            orderCoin,
            sells.Sum(order => Worth(order.ItemId, order.Quantity)),
            box.Coins,
            waiting.Sum(item => Worth(item.Id, item.Count)),
            Priced: true);
    }

    // --- closed trades -----------------------------------------------------

    public async Task<IReadOnlyList<OrderSection>> GetHistoryAsync(CancellationToken ct = default)
    {
        var sold = await OrdersAsync("history/sells", "sells", current: false, ct);
        var bought = await OrdersAsync("history/buys", "buys", current: false, ct);

        var all = sold.Concat(bought).ToList();
        if (all.Count == 0) return [];

        var items = await lookup.ResolveAsync(all.Select(t => t.ItemId).ToList(), ct);
        var sections = new List<OrderSection>();

        if (sold.Count > 0)
            sections.Add(new OrderSection("Sold", Summarise(sold), Total(sold),
                sold.OrderByDescending(t => t.Purchased).Take(50).Select(t => ToHistoryRow(t, items))));

        if (bought.Count > 0)
            sections.Add(new OrderSection("Bought", Summarise(bought), Total(bought),
                bought.OrderByDescending(t => t.Purchased).Take(50).Select(t => ToHistoryRow(t, items))));

        return sections;
    }

    // --- pickup box --------------------------------------------------------

    public async Task<DeliveryView> GetDeliveryAsync(CancellationToken ct = default)
    {
        var box = (await offline.ReadAsync(
            "commerce/delivery", Gw2JsonContext.Default.DeliveryBox,
            client.GetDeliveryAsync, ct)).Value;
        var waiting = box.Items ?? [];

        var items = await lookup.ResolveAsync(waiting.Select(i => i.Id).ToList(), ct);

        var rows = waiting
            .Select(entry =>
            {
                var item = items.GetValueOrDefault(entry.Id);
                return new OrderRow(
                    entry.Id,
                    item?.Name ?? L.F("Item {0}", entry.Id),
                    item?.Icon,
                    item?.Rarity,
                    entry.Count.ToString("N0"),
                    string.Empty,
                    string.Empty,
                    null,
                    false);
            })
            .ToList();


        return new DeliveryView(new CoinPurse(box.Coins), box.Coins > 0, rows);
    }

    // --- shaping -----------------------------------------------------------

    private static OrderRow ToRow(
        Transaction order,
        Dictionary<int, Item> items,
        Dictionary<int, ItemPrice> prices,
        Dictionary<int, ItemListings> books,
        bool selling)
    {
        var item = items.GetValueOrDefault(order.ItemId);
        var market = prices.GetValueOrDefault(order.ItemId);

        // Selling: someone asking less than you gets bought first. Buying: someone offering
        // more than you gets filled first. Either way, "beaten" means your order waits.
        var best = selling ? market?.Sells?.UnitPrice : market?.Buys?.UnitPrice;
        var beaten = OrderBook.IsBeaten(order.Price, best, selling);

        var book = books.GetValueOrDefault(order.ItemId);
        var side = selling ? book?.Sells : book?.Buys;

        var note = OrderBook.Describe(
            selling, order.Price, best,
            side is null ? null : OrderBook.Ahead(side, order.Price, selling),
            side is null ? null : OrderBook.Tied(side, order.Price, order.Quantity));

        return new OrderRow(
            order.ItemId,
            item?.Name ?? L.F("Item {0}", order.ItemId),
            item?.Icon,
            item?.Rarity,
            $"×{order.Quantity:N0}",
            Coin.Format(order.Price),
            Coin.Format(order.Price * order.Quantity),
            note,
            beaten);
    }

    private static OrderRow ToHistoryRow(Transaction trade, Dictionary<int, Item> items)
    {
        var item = items.GetValueOrDefault(trade.ItemId);

        return new OrderRow(
            trade.ItemId,
            item?.Name ?? L.F("Item {0}", trade.ItemId),
            item?.Icon,
            item?.Rarity,
            $"×{trade.Quantity:N0}",
            Coin.Format(trade.Price),
            Coin.Format(trade.Price * trade.Quantity),
            trade.Purchased?.ToLocalTime().ToString("d MMM HH:mm"),
            false);
    }

    /// <summary>Orders and trades are both worth totalling: that is the number people want.</summary>
    private static string Summarise(IReadOnlyCollection<Transaction> transactions) =>
        $"{transactions.Count} · {Coin.Format(Total(transactions))}";

    private static long Total(IEnumerable<Transaction> transactions) =>
        transactions.Sum(t => t.Price * t.Quantity);

    /// <summary>
    /// One side of the market ledger, kept so the screen still has something to show with no
    /// connection. Prices are deliberately not kept — a stale price presented as the market
    /// would be worse than none.
    /// </summary>
    private async Task<List<Transaction>> OrdersAsync(
        string key, string side, bool current, CancellationToken ct) =>
        (await offline.ReadAsync(
            $"commerce/transactions/{key}", Gw2JsonContext.Default.ListTransaction,
            token => current
                ? client.GetCurrentTransactionsAsync(side, token)
                : client.GetTransactionHistoryAsync(side, token),
            ct)).Value;
}
