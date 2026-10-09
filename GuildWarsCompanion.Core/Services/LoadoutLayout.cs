namespace GuildWarsCompanion.Services;

/// <summary>
/// Where each slot sits on a body. Positions are fractions of the figure's box, so the
/// paper doll scales with the screen; the tiles themselves stay a fixed touch size.
///
/// Three plans, because three body shapes hang armour differently: the upright races
/// (human, norn, sylvari) share one, charr lean forward on digitigrade legs, and asura are
/// mostly head.
/// </summary>
public static class LoadoutLayout
{
    /// <summary>Side of a slot tile, in device-independent pixels.</summary>
    public const double TileSize = 44;

    // Positions are centres-of-free-space fractions, so two tiles collide unless they differ
    // by more than TileSize / (box - TileSize) on one axis — about 0.16 across and 0.11 down
    // at the 320x430 box the Gear tab draws. Gloves and the swap weapon sat inside that
    // margin on the humanoid and charr plans, which read as one tile eating the other.

    private static readonly Dictionary<string, (double X, double Y)> Humanoid = new()
    {
        ["Helm"] = (0.50, 0.03),
        ["Shoulders"] = (0.74, 0.16),
        ["Coat"] = (0.50, 0.28),
        ["Gloves"] = (0.20, 0.56),
        ["Leggings"] = (0.50, 0.58),
        ["Boots"] = (0.50, 0.87),
        ["WeaponA1"] = (0.06, 0.26),
        ["WeaponA2"] = (0.94, 0.26),
        ["WeaponB1"] = (0.06, 0.42),
        ["WeaponB2"] = (0.94, 0.42),
        ["Backpack"] = (0.26, 0.16)
    };

    private static readonly Dictionary<string, (double X, double Y)> Charr = new()
    {
        ["Helm"] = (0.50, 0.01),
        ["Shoulders"] = (0.79, 0.19),
        ["Coat"] = (0.50, 0.32),
        ["Gloves"] = (0.14, 0.58),
        ["Leggings"] = (0.50, 0.62),
        ["Boots"] = (0.50, 0.90),
        ["WeaponA1"] = (0.04, 0.28),
        ["WeaponA2"] = (0.96, 0.28),
        ["WeaponB1"] = (0.04, 0.44),
        ["WeaponB2"] = (0.96, 0.44),
        ["Backpack"] = (0.21, 0.19)
    };

    private static readonly Dictionary<string, (double X, double Y)> Asura = new()
    {
        ["Helm"] = (0.50, 0.05),
        ["Shoulders"] = (0.72, 0.30),
        ["Coat"] = (0.50, 0.40),
        ["Gloves"] = (0.24, 0.60),
        ["Leggings"] = (0.50, 0.65),
        ["Boots"] = (0.50, 0.86),
        ["WeaponA1"] = (0.06, 0.36),
        ["WeaponA2"] = (0.94, 0.36),
        ["WeaponB1"] = (0.06, 0.52),
        ["WeaponB2"] = (0.94, 0.52),
        ["Backpack"] = (0.28, 0.30)
    };

    /// <summary>Slots drawn on the figure, in the order they should be laid out.</summary>
    public static IReadOnlyList<string> PlacedSlots { get; } =
        ["Helm", "Shoulders", "Coat", "Gloves", "Leggings", "Boots",
         "WeaponA1", "WeaponA2", "WeaponB1", "WeaponB2", "Backpack"];

    /// <summary>
    /// Trinkets have no place on a silhouette — nothing about a body says "second ring" —
    /// so they sit in a strip underneath instead of being pinned somewhere arbitrary.
    /// </summary>
    public static IReadOnlyList<string> StripSlots { get; } =
        ["Amulet", "Ring1", "Ring2", "Accessory1", "Accessory2"];

    public static string ImageFor(string? race) => BodyPlan(race) switch
    {
        "charr" => "body_charr.png",
        "asura" => "body_asura.png",
        _ => "body_humanoid.png"
    };

    public static (double X, double Y)? PositionFor(string? race, string slot)
    {
        var map = BodyPlan(race) switch
        {
            "charr" => Charr,
            "asura" => Asura,
            _ => Humanoid
        };

        return map.TryGetValue(slot, out var position) ? position : null;
    }

    /// <summary>Norn and sylvari are built like humans; only charr and asura are not.</summary>
    private static string BodyPlan(string? race) => race switch
    {
        "Charr" => "charr",
        "Asura" => "asura",
        _ => "humanoid"
    };
}
