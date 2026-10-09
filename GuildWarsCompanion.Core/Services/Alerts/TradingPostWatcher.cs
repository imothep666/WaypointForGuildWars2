using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services.Alerts;

/// <summary>
/// Watches for trades that closed while the player was away.
///
/// The signal is the transaction *history*, not the open orders: an order that disappears
/// from `current` might have sold or might have been cancelled, whereas a row in
/// `history/sells` means the sale happened, and its id makes a natural alert key.
///
/// Commerce responses are cached by the API for five minutes, so there is nothing to gain
/// from polling faster than that.
/// </summary>
public sealed class TradingPostWatcher(Gw2Client client, ItemLookup lookup) : IAccountWatcher
{
    /// <summary>History runs 90 days deep; only recent closes are worth announcing.</summary>
    private static readonly TimeSpan Recent = TimeSpan.FromDays(2);

    public string Name => "tradingpost";

    /// <summary>The game announces a sale or a filled buy order to a logged-in player.</summary>
    public bool GameSaysItToo => true;

    public IReadOnlyList<string> RequiredPermissions { get; } =
        [Gw2Permissions.Account, Gw2Permissions.TradingPost];

    public async Task<IReadOnlyList<AccountAlert>> CheckAsync(CancellationToken ct)
    {
        var alerts = new List<AccountAlert>();
        var cutoff = DateTimeOffset.UtcNow - Recent;

        foreach (var (side, verb) in new[] { ("sells", "sold"), ("buys", "bought") })
        {
            List<Transaction> history;
            try
            {
                history = await client.GetTransactionHistoryAsync(side, ct);
            }
            catch (Gw2ApiException ex) when (ex.Kind is Gw2ErrorKind.MissingScope)
            {
                continue;
            }

            var closed = history
                .Where(t => t.Purchased is not null && t.Purchased >= cutoff)
                .ToList();
            if (closed.Count == 0) continue;

            var names = await lookup.ResolveNamesAsync(closed.Select(t => t.ItemId).ToList(), ct);

            foreach (var trade in closed)
            {
                var name = names.GetValueOrDefault(trade.ItemId) ?? $"Item {trade.ItemId}";
                var total = Coin.Format(trade.Price * trade.Quantity);

                alerts.Add(new AccountAlert(
                    $"tp:{side}:{trade.Id}",
                    trade.Quantity > 1 ? $"{trade.Quantity} × {name} {verb}" : $"{name} {verb}",
                    side == "sells"
                        ? $"{total} is waiting for pickup at the trading post."
                        : $"{total} spent. Collect it at any trading post."));
            }
        }

        return alerts;
    }

}
