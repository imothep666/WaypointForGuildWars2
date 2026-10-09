using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>What a dye is called and how it comes out, once.</summary>
public sealed record DyeUse(int Id, string Name, string Hex, int Uses)
{
    public string UsesText => Uses > 1 ? $"×{Uses}" : string.Empty;

    public bool IsRepeated => Uses > 1;
}

/// <summary>
/// Reading the colours a character is actually wearing.
///
/// Every armour piece carries four dye channels, and the same dye is a different colour on
/// cloth, leather and metal — the API gives each material its own rgb. Which one applies is
/// decided by the wearer's armour weight, so the profession has to be part of the question.
/// Picking the wrong material is a silent error: the swatch is a plausible colour, just not
/// the one on the character.
/// </summary>
public static class Dyes
{
    private static readonly HashSet<string> Light =
        new(StringComparer.OrdinalIgnoreCase) { "Elementalist", "Mesmer", "Necromancer" };

    private static readonly HashSet<string> Medium =
        new(StringComparer.OrdinalIgnoreCase) { "Engineer", "Ranger", "Thief" };

    /// <summary>
    /// The armour weight a profession wears, as the colour endpoint names its materials.
    /// Heavy is the default because the three heavy professions are the ones left over, and a
    /// profession this does not recognise is more likely new than light.
    /// </summary>
    public static string MaterialFor(string? profession) =>
        Light.Contains(profession ?? string.Empty) ? "cloth"
        : Medium.Contains(profession ?? string.Empty) ? "leather"
        : "metal";

    /// <summary>
    /// "#RRGGBB" for one dye on one material, falling back to the dye's base colour when the
    /// material is missing rather than dropping the swatch entirely.
    /// </summary>
    public static string? Hex(DyeColor? dye, string material)
    {
        var rgb = material switch
        {
            "cloth" => dye?.Cloth?.Rgb,
            "leather" => dye?.Leather?.Rgb,
            _ => dye?.Metal?.Rgb
        } ?? dye?.BaseRgb;

        return rgb is { Count: 3 } ? $"#{rgb[0]:X2}{rgb[1]:X2}{rgb[2]:X2}" : null;
    }

    /// <summary>Every dye channel worn, in the order the pieces come.</summary>
    public static IReadOnlyList<int> Channels(IEnumerable<EquipmentPiece>? worn) =>
        (worn ?? []).SelectMany(piece => piece.Dyes ?? [])
            .OfType<int>()
            .ToList();

    /// <summary>
    /// The character's palette, most-worn first.
    ///
    /// Counting matters: a dye used on seven pieces is the character's colour, and one used
    /// once is a trim. Listing them in the order the pieces happen to arrive would bury the
    /// first under the second.
    /// </summary>
    public static IReadOnlyList<DyeUse> Palette(
        IEnumerable<EquipmentPiece>? worn,
        IReadOnlyDictionary<int, DyeColor> colors,
        string? profession)
    {
        var material = MaterialFor(profession);

        return Channels(worn)
            .GroupBy(id => id)
            .Select(group =>
            {
                var dye = colors.GetValueOrDefault(group.Key);
                var hex = Hex(dye, material);

                return hex is null || dye is null
                    ? null
                    : new DyeUse(group.Key, dye.Name, hex, group.Count());
            })
            .OfType<DyeUse>()
            .OrderByDescending(use => use.Uses)
            .ThenBy(use => use.Name, StringComparer.CurrentCulture)
            .ToList();
    }
}
