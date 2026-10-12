using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Where an open order actually stands in the queue.
///
/// "Undercut" on its own is not advice. Undercut by one copper with three items ahead of you
/// is nothing; undercut by one copper with eight thousand ahead of you means the listing will
/// not sell this month. The gap and the depth are what turn the flag into a decision, and both
/// come out of the order book rather than the summary price.
/// </summary>
public static class OrderBook
{
    /// <summary>
    /// How many units are certain to be filled before yours.
    ///
    /// Strictly better only. Orders at your own price are a tie the API cannot break for you —
    /// see <see cref="Tied"/> — and counting them here would overstate the queue.
    /// </summary>
    public static int Ahead(IEnumerable<ListingTier>? tiers, long price, bool selling) =>
        (tiers ?? []).Where(tier => selling ? tier.UnitPrice < price : tier.UnitPrice > price)
            .Sum(tier => tier.Quantity);

    /// <summary>
    /// How many units sit at your exact price, yours excluded.
    ///
    /// Your own listing is in the book, so it has to come back out or every order looks like it
    /// has company. Same-price orders fill oldest first and the API never says when the others
    /// were placed, so this is a tie, not a position.
    /// </summary>
    public static int Tied(IEnumerable<ListingTier>? tiers, long price, int mine) =>
        Math.Max(0, (tiers ?? []).Where(tier => tier.UnitPrice == price)
            .Sum(tier => tier.Quantity) - mine);

    /// <summary>
    /// True when the market has moved past you: someone asking less gets bought first, and
    /// someone offering more gets filled first.
    /// </summary>
    public static bool IsBeaten(long price, long? best, bool selling) =>
        best is not null && (selling ? best < price : best > price);

    /// <summary>
    /// The note under an order: what is wrong with it, and how badly.
    ///
    /// <paramref name="ahead"/> and <paramref name="tied"/> are null when the book was not
    /// fetched — depth costs a request per item, so it is only asked for where it changes
    /// something. A null there prints nothing rather than a confident zero.
    /// </summary>
    public static string? Describe(
        bool selling, long price, long? best, int? ahead = null, int? tied = null)
    {
        if (best is null) return null;

        if (IsBeaten(price, best, selling))
        {
            var gap = Coin.Format(Math.Abs(price - best.Value));
            var beaten = selling ? L.F("undercut by {0}", gap) : L.F("outbid by {0}", gap);

            return ahead is > 0 ? $"{beaten} · {L.F("{0:N0} ahead of you", ahead)}" : beaten;
        }

        var lead = selling ? L.T("best ask") : L.T("best offer");

        // Sitting at the best price and still waiting is the other question worth answering,
        // and the queue is the whole of the answer.
        return tied switch
        {
            > 0 => $"{lead} · {L.F("{0:N0} more at your price", tied)}",
            0 => $"{lead} · {L.T("yours alone")}",
            _ => lead
        };
    }

    /// <summary>
    /// The section heading's count. The number of beaten orders leads the eye to whether the
    /// list needs reading at all.
    ///
    /// <paramref name="capped"/> is said out loud: books are heavy enough to be rationed, and
    /// a rationed screen that looks complete is worse than one that admits the ration.
    /// </summary>
    public static string Summarise(
        int count, long total, int beaten, bool selling, bool capped = false)
    {
        var trouble = beaten > 0
            ? " · " + (selling ? L.F("{0:N0} undercut", beaten) : L.F("{0:N0} outbid", beaten))
            : string.Empty;

        var ration = capped ? " · " + L.T("queue shown for the largest orders only") : string.Empty;

        return $"{count:N0} · {Coin.Format(total)}{trouble}{ration}";
    }
}
