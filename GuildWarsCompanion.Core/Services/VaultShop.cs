using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>One reward in the vault's shop, ready to show.</summary>
/// <param name="Left">At most how many more may be bought, when the API reported a purchase of
/// it; null when unlimited, or when it reported none — which says nothing; see <see cref="VaultShop"/>.</param>
/// <param name="Featured">On the vault's banner, where the game paints each reward large.</param>
public sealed record VaultShopRow(
    int ItemId, string Name, string? Icon, int Count, int Cost, int? Limit, int? Left, bool Affordable,
    bool Featured = false)
{
    /// <summary>The name the wiki files the reward's banner painting under; only featured rewards have one.</summary>
    public string? BannerName => Featured ? Name : null;

    public string Title => Count > 1 ? $"{Name} × {Count}" : Name;

    public string CostText => $"{Cost:N0}";

    public string LimitText => Limit switch
    {
        null => "unlimited",
        _ when Left is 0 => "bought out",
        _ when Left is { } left => $"at most {left} of {Limit} left",
        _ => $"limit {Limit}"
    };

    /// <summary>Brass when it can be bought now — enough Acclaim, and not bought out.</summary>
    public Color StatusColor => Affordable && Left is not 0 ? Theme.Brass : Left == 0 ? Theme.Banked : Colors.Transparent;
}

/// <summary>A part of the shop as the game lays it out.</summary>
public sealed class VaultShopSection(string name, string summary, IEnumerable<VaultShopRow> rows)
    : List<VaultShopRow>(rows)
{
    public string Name { get; } = name;
    public string Summary { get; } = summary;
}

/// <summary>
/// The Wizard's Vault shop: what Astral Acclaim can buy this season, what each costs, and what
/// the limits are.
///
/// What is left of a limit depends on <c>purchased</c>, and the API under-reports it. The wiki
/// records it as 0 even after buying, and the player's own account showed both sides at once on
/// 2026-10-05: the "/bless" Emote Tome reported as bought, two Wizard's Ascended Armor Chests
/// bought and reported as 0 of 3. So a reported purchase is believed — the API does not invent
/// them — but only as a floor, and a 0 says nothing: the reward shows its limit as set. A first
/// version believed every 0 once any purchase was reported, and told the player 3 of 3 chests
/// were left.
/// </summary>
public static class VaultShop
{
    /// <summary>Said above the shop, because the reason the numbers are cautious is not visible.</summary>
    public const string Caution =
        "The API does not report every purchase from the vault, so a limit is shown as set unless a " +
        "purchase was reported — and then as \"at most\" what is left.";

    /// <summary>The game's three places, in its order: the banner, the rewards table, the legacy vault.</summary>
    private static readonly (string Type, string Name)[] Places =
        [("Featured", "Featured"), ("Normal", "Rewards"), ("Legacy", "Legacy vault")];

    public static IReadOnlyList<VaultShopSection> Sections(
        IReadOnlyList<VaultListing> listings, Func<int, (string Name, string? Icon)> item, long? acclaim)
    {
        var sections = new List<VaultShopSection>();

        foreach (var (type, name) in Places)
        {
            var rows = listings
                .Where(listing => string.Equals(listing.Type, type, StringComparison.OrdinalIgnoreCase))
                .Select(listing =>
                {
                    var (itemName, icon) = item(listing.ItemId);
                    // Only a reported purchase means anything, and then as a floor on what was bought.
                    int? left = listing is { PurchaseLimit: { } limit, Purchased: > 0 and var bought }
                        ? Math.Max(0, limit - bought)
                        : null;

                    return new VaultShopRow(listing.ItemId, itemName, icon, listing.ItemCount, listing.Cost,
                        listing.PurchaseLimit, left, acclaim is { } balance && balance >= listing.Cost,
                        Featured: type == "Featured");
                })
                .OrderBy(row => row.Cost)
                .ThenBy(row => row.Name, StringComparer.CurrentCulture)
                .ToList();

            if (rows.Count == 0) continue;

            // Just the count: a total of what is left would add up the zeros the API gets wrong,
            // and be the largest wrong number on the screen.
            sections.Add(new VaultShopSection(name, rows.Count == 1 ? "1 reward" : $"{rows.Count} rewards", rows));
        }

        return sections;
    }
}
