namespace GuildWarsCompanion.Services;

/// <summary>
/// Reading the gem exchange.
///
/// The exchange publishes no rate — it prices the trade you ask about — and the price is not
/// linear: a small conversion gets a worse one than a large. So nothing here multiplies a
/// per-gem figure by an amount; every number shown is one the exchange actually quoted for
/// that exact trade.
///
/// The round trip is the number worth knowing, and it is the one the game never shows you:
/// gold to gems and back does not return what you put in, and the gap is large.
/// </summary>
public static class GemExchange
{
    /// <summary>
    /// What comes back from converting to gems and straight back again, as a percentage of
    /// what went in.
    ///
    /// Both figures have to come from real quotes of the matching size — quoting a buy from
    /// one amount against a sale of another would make the loss look like whatever the two
    /// sample sizes happened to differ by.
    /// </summary>
    public static string? RoundTrip(long coinIn, long coinBack)
    {
        if (coinIn <= 0 || coinBack <= 0) return null;

        return $"{coinBack * 100.0 / coinIn:0}%";
    }

    /// <summary>
    /// "7g 79s 70c buys 18 gems" — the trade as the exchange priced it.
    /// </summary>
    public static string Buys(long copper, int gems) =>
        $"{Coin.Format(copper)} buys {gems:N0} gem{(gems == 1 ? string.Empty : "s")}";

    /// <summary>
    /// What those same gems fetch on the way back, and what fraction of the original that is.
    /// </summary>
    public static string SellsBack(int gems, long copper, string? roundTrip) =>
        roundTrip is null
            ? $"{gems:N0} gems sell for {Coin.Format(copper)}"
            : $"{gems:N0} gems sell back for {Coin.Format(copper)} — {roundTrip} of what went in";

    /// <summary>
    /// Whether a quote is an answer at all. A trade too small to make comes back as a message
    /// with no quantity, on a 200 rather than an error, so an absent quantity is the check.
    /// </summary>
    public static bool IsQuoted(int quantity, string? text) =>
        quantity > 0 && string.IsNullOrEmpty(text);

    /// <summary>
    /// Why the screen has nothing to say. The exchange refuses trades under roughly a silver,
    /// which is a real answer rather than a failure.
    /// </summary>
    public static string TooSmall(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? "Not enough coin to convert."
            : char.ToUpperInvariant(text[0]) + text[1..] + " to convert.";
}
