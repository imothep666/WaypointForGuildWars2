using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>One thing to buy, with what it costs right now.</summary>
public sealed record ShoppingRow(string Name, string Quantity, string UnitPrice, string Total);

/// <summary>What it would cost to finish what the character has already started.</summary>
public sealed record ShoppingList(
    IReadOnlyList<ShoppingRow> Rows, string? GrandTotal, string? Nothing, string Caveat);

/// <summary>
/// Prices the gaps the build check found — but only where the item to buy follows from the
/// character's own gear rather than from anyone's opinion.
///
/// Completing a rune set is derivable: five pieces already carry Superior Rune of Balthazar,
/// so the sixth is not a matter of taste. Choosing which rune a build "should" run is not
/// derivable from anything the API knows, and this deliberately does not guess.
/// </summary>
public sealed class GearAdvisor(Gw2Client client)
{
    private static readonly string[] ArmourSlots =
        ["Helm", "Shoulders", "Coat", "Gloves", "Leggings", "Boots"];

    private static readonly string[][] WeaponSets =
        [["WeaponA1", "WeaponA2"], ["WeaponB1", "WeaponB2"]];

    private static readonly HashSet<string> TwoHanded = new(StringComparer.OrdinalIgnoreCase)
    {
        "Greatsword", "Hammer", "Longbow", "Shortbow", "Rifle", "Staff",
        "Spear", "Trident", "Harpoon Gun", "Speargun"
    };

    public async Task<ShoppingList> BuildAsync(
        Character character,
        IReadOnlyDictionary<int, WeaponItem> details,
        CancellationToken ct = default)
    {
        var worn = (character.Equipment ?? [])
            .Where(e => e.IsWorn && e.Slot is not null)
            .ToDictionary(e => e.Slot!, e => e);

        var wanted = new List<(int ItemId, string Name, int Quantity)>();

        // Runes: the set the character already favours, topped up to six.
        var armour = ArmourSlots.Where(worn.ContainsKey).Select(s => worn[s]).ToList();
        var runeIds = armour
            .Select(piece => (piece.Upgrades ?? [])
                .FirstOrDefault(id => details.GetValueOrDefault(id)?.Details?.Type == "Rune"))
            .ToList();

        var majorityRune = runeIds
            .Where(id => id != 0)
            .GroupBy(id => id)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        if (majorityRune is not null)
        {
            var shortfall = ArmourSlots.Length - majorityRune.Count();
            if (shortfall > 0)
                wanted.Add((
                    majorityRune.Key,
                    details.GetValueOrDefault(majorityRune.Key)?.Name ?? $"item {majorityRune.Key}",
                    shortfall));
        }

        // Sigils: same rule, per weapon set, counting the slots a two-hander opens up.
        foreach (var slots in WeaponSets)
        {
            var present = slots.Where(worn.ContainsKey).ToList();
            if (present.Count == 0) continue;

            var capacity = present.Sum(slot =>
            {
                var type = details.GetValueOrDefault(worn[slot].Id)?.Details?.Type;
                return type is not null && TwoHanded.Contains(type) ? 2 : 1;
            });

            var sigils = present
                .SelectMany(slot => worn[slot].Upgrades ?? [])
                .Where(id => details.GetValueOrDefault(id)?.Details?.Type == "Sigil")
                .ToList();

            var majoritySigil = sigils
                .GroupBy(id => id)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            if (majoritySigil is null) continue;

            var shortfall = capacity - sigils.Count;
            if (shortfall > 0)
                wanted.Add((
                    majoritySigil.Key,
                    details.GetValueOrDefault(majoritySigil.Key)?.Name ?? $"item {majoritySigil.Key}",
                    shortfall));
        }

        const string caveat =
            "Only what your own gear already determines. Nothing here suggests which rune or " +
            "sigil a build should run — the API has no view on that.";

        if (wanted.Count == 0)
            return new ShoppingList([], null,
                "Nothing to complete from what you are already wearing.", caveat);

        var prices = (await client.GetPricesAsync(wanted.Select(w => w.ItemId), ct))
            .ToDictionary(p => p.Id);

        var rows = new List<ShoppingRow>();
        long grand = 0;

        foreach (var (itemId, name, quantity) in wanted)
        {
            // The asking price is what buying one right now actually costs.
            var ask = prices.GetValueOrDefault(itemId)?.Sells?.UnitPrice;
            if (ask is null or 0)
            {
                rows.Add(new ShoppingRow(name, $"×{quantity}", "not traded", string.Empty));
                continue;
            }

            var total = ask.Value * quantity;
            grand += total;

            rows.Add(new ShoppingRow(
                name,
                $"×{quantity}",
                Coin.Format(ask.Value),
                Coin.Format(total)));
        }

        return new ShoppingList(
            rows,
            grand > 0 ? Coin.Format(grand) : null,
            null,
            caveat);
    }
}
