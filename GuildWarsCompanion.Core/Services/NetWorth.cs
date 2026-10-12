namespace GuildWarsCompanion.Services;

/// <summary>One slice of what an account is worth.</summary>
public sealed record WorthPart(string Name, long Value, string Where)
{
    public string ValueText => Coin.Format(Value);

    public bool HasValue => Value > 0;
}

/// <summary>
/// Adding an account up into one number.
///
/// The number is only honest if it says what it counted. Three quiet ways to get it wrong:
/// valuing listed items at your asking price rather than at what a buyer is offering, which
/// flatters anyone with an optimistic listing; forgetting the coin tied up in buy orders,
/// which is yours and comes back in full when an order is cancelled; and totalling whatever
/// happened to load, so a missing permission silently shrinks the account instead of being
/// mentioned.
/// </summary>
public static class NetWorth
{
    /// <summary>
    /// The parts, largest first, with the empty ones left out.
    ///
    /// Ordering by size rather than by category puts the answer to "where is my money" at the
    /// top, which is the only reason to break a single number apart at all.
    /// </summary>
    public static IReadOnlyList<WorthPart> Parts(
        long coin, long stored, long listed, long buyOrders, long deliveryCoin, long deliveryItems)
    {
        var parts = new List<WorthPart>
        {
            new(L.T("Coin"), coin, L.T("in the wallet")),
            new(L.T("Items"), stored, L.T("bags, bank and material storage")),
            new(L.T("Listed"), listed, L.T("waiting to sell, at buy-order value")),
            new(L.T("Buy orders"), buyOrders, L.T("coin the trading post is holding")),
            new(L.T("Delivery box"), deliveryCoin + deliveryItems, L.T("sold and bought, not collected"))
        };

        return parts.Where(part => part.HasValue)
            .OrderByDescending(part => part.Value)
            .ToList();
    }

    public static long Total(IEnumerable<WorthPart> parts) => parts.Sum(part => part.Value);

    /// <summary>
    /// A part's share, for the parts big enough to matter. Anything under a percent reads as
    /// "0%", which says less than nothing next to a real figure.
    /// </summary>
    public static string Share(long value, long total)
    {
        if (total <= 0 || value <= 0) return string.Empty;

        var share = value * 100.0 / total;

        return share < 1 ? "<1%" : $"{share:0}%";
    }

    /// <summary>
    /// What the number does not include, said plainly.
    ///
    /// Every line here is something a player could otherwise assume was counted. Gems are the
    /// awkward one: they convert to coin at a rate that moves, so counting them would make the
    /// total drift on its own, and leaving them out silently would understate a big holding.
    /// </summary>
    public static string Caveat(bool priced, IReadOnlyCollection<string> unreadable)
    {
        if (!priced)
            return L.T("Values need a connection — this is coin only, not what your items are worth.");

        var note = L.T("Everything tradable at what buyers are offering now, after the 15% fee. " +
                       "Account-bound items, karma and gems are not counted.");

        if (unreadable.Count > 0)
            note += " " + L.F("Could not read {0}, so this is short by whatever is in there.",
                string.Join(", ", unreadable));

        return note;
    }
}
