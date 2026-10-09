namespace GuildWarsCompanion.Services;

/// <summary>
/// A price the player is waiting for.
///
/// <paramref name="Below"/> chooses the direction: watching the sell price fall below a
/// number is "tell me when I can buy it", watching the buy price rise above one is "tell me
/// when it is worth selling".
/// </summary>
public sealed record PriceWatch(
    long Id, int ItemId, string Name, string? Icon, bool WatchSellPrice, bool Below,
    long Threshold, bool Triggered);

/// <summary>
/// Reading a typed price, and deciding whether a watch has been met.
///
/// Kept apart from the service that fetches and stores, so it can be tested without a
/// database or an HTTP client. Both mistakes it can make are silent: a misparsed threshold
/// never fires, and a mis-signed comparison fires constantly.
/// </summary>
public static class PriceRules
{
    /// <summary>
    /// The trading post's cut: 5% to list and 10% when it sells, so a seller keeps 85%.
    /// </summary>
    public const double SellerKeeps = 0.85;

    /// <summary>
    /// What a sale actually puts in your pocket. Quoting a gross number flatters every
    /// valuation in the app and misleads the only decision it informs, so nothing shows a
    /// gross figure.
    /// </summary>
    public static long AfterFees(long gross) => gross <= 0 ? 0 : (long)(gross * SellerKeeps);

    /// <summary>
    /// True when the price is where the player asked for it. A watch with no market data is
    /// never met: an absent price is not a low one.
    /// </summary>
    public static bool IsMet(PriceWatch watch, long? current) =>
        current is not null && current > 0 &&
        (watch.Below ? current <= watch.Threshold : current >= watch.Threshold);

    public static string Describe(PriceWatch watch) =>
        $"{(watch.WatchSellPrice ? "Sell price" : "Buy price")} " +
        $"{(watch.Below ? "at or below" : "at or above")} {Coin.Format(watch.Threshold)}";

    /// <summary>
    /// Parses "1g 20s 5c", "20s", or a bare number of copper. Anything it cannot read with
    /// certainty is refused rather than guessed at — including "1g20", where the trailing
    /// digits could be silver or copper.
    /// </summary>
    public static long? ParsePrice(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var trimmed = text.Trim().ToLowerInvariant().Replace(" ", string.Empty);

        // A bare number is copper, which is how the API itself talks about money.
        if (long.TryParse(trimmed, out var plain)) return plain >= 0 ? plain : null;

        long total = 0;
        var digits = string.Empty;
        var sawUnit = false;

        foreach (var character in trimmed)
        {
            if (char.IsDigit(character))
            {
                digits += character;
                continue;
            }

            if (digits.Length == 0 || !long.TryParse(digits, out var value)) return null;

            var contribution = character switch
            {
                'g' => value * 10_000,
                's' => value * 100,
                'c' => value,
                _ => -1
            };

            if (contribution < 0) return null;

            total += contribution;
            sawUnit = true;
            digits = string.Empty;
        }

        return sawUnit && digits.Length == 0 ? total : null;
    }
}
