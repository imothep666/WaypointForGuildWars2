namespace GuildWarsCompanion.Services;

/// <summary>
/// Money, the way Guild Wars 2 writes it.
///
/// Lives on its own rather than inside a service so it can be tested without a database or an
/// HTTP client: the gold/silver/copper boundaries are exactly the sort of arithmetic that
/// fails quietly and looks plausible while doing it.
/// </summary>
public static class Coin
{
    /// <summary>The wallet id of Coin itself; everything else is a plain count.</summary>
    public const int CurrencyId = 1;

    /// <summary>
    /// Copper as the game shows it. Leading units are dropped — 45 copper is "45c", not
    /// "0g 00s 45c" — but once a larger unit appears the smaller ones are zero-padded so
    /// columns of prices line up.
    /// </summary>
    public static string Format(long copper)
    {
        var gold = copper / 10_000;
        var silver = copper % 10_000 / 100;
        var rest = copper % 100;

        return gold > 0 ? $"{gold:N0}g {silver:00}s {rest:00}c"
            : silver > 0 ? $"{silver}s {rest:00}c"
            : $"{rest}c";
    }

    /// <summary>Coin is in copper; every other currency is a count.</summary>
    public static string FormatAmount(int currencyId, long value) =>
        currencyId == CurrencyId ? Format(value) : value.ToString("N0");
}

/// <summary>
/// Coin split into its three metals. Every price in Guild Wars 2 is shown this way, so screens
/// lead with it rather than flattening a balance into one number.
/// </summary>
public sealed record CoinPurse(long TotalCopper)
{
    public string Gold => (TotalCopper / 10_000).ToString("N0");
    public string Silver => (TotalCopper % 10_000 / 100).ToString("00");
    public string Copper => (TotalCopper % 100).ToString("00");
}
