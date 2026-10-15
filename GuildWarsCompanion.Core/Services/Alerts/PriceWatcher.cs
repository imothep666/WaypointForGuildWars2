using GuildWarsCompanion.Data;
using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services.Alerts;

/// <summary>
/// Watches the market for prices the player asked to be told about.
///
/// This is the one watcher that needs no permissions: commerce prices are unauthenticated, so
/// price alerts work on a phone with no API key signed in at all.
///
/// It notifies on the *transition* into a met condition and re-arms when the price moves back,
/// rather than repeating every half hour for as long as the condition holds. That state lives
/// in the watch itself, which is also why this watcher writes to the store.
/// </summary>
public sealed class PriceWatcher(Gw2Client client, WatchStore store) : IAccountWatcher
{
    public string Name => "prices";

    /// <summary>None: prices are public.</summary>
    public IReadOnlyList<string> RequiredPermissions { get; } = [];

    /// <summary>
    /// No baseline. Each watch already remembers whether it was met, so this reports only
    /// crossings — and the first crossing is exactly the one the user set the watch for.
    /// </summary>
    public bool NeedsBaseline => false;

    public async Task<IReadOnlyList<AccountAlert>> CheckAsync(CancellationToken ct)
    {
        var watches = await store.ListAsync(ct);
        if (watches.Count == 0) return [];

        var prices = (await client.GetPricesAsync(watches.Select(w => w.ItemId), ct))
            .ToDictionary(p => p.Id);

        var alerts = new List<AccountAlert>();

        foreach (var watch in watches)
        {
            var market = prices.GetValueOrDefault(watch.ItemId);
            var current = watch.WatchSellPrice ? market?.Sells?.UnitPrice : market?.Buys?.UnitPrice;
            var met = PriceRules.IsMet(watch, current);

            if (met == watch.Triggered) continue;

            await store.SetTriggeredAsync(watch.Id, met, ct);

            // Only the crossing into "met" is news; falling back out just re-arms quietly.
            if (!met) continue;

            alerts.Add(new AccountAlert(
                $"price:{watch.Id}:{current}",
                L.F("{0} is at {1}", watch.Name, Coin.Format(current!.Value)),
                L.F("{0} — you asked to be told.", PriceRules.Describe(watch)))
            {
                // The watch list, and the link for the trading post's search from the shade itself:
                // a price that has arrived is a reason to go and buy or sell.
                Route = "//tradingpost/watch",
                TradingPostItem = watch.ItemId,
                TradingPostName = watch.Name
            });
        }

        return alerts;
    }
}
