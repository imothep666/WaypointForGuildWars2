namespace GuildWarsCompanion;

/// <summary>
/// Colours the game itself teaches players to read. Rarity is the one piece of item
/// metadata a GW2 player decodes without thinking, so material rows carry it as a spine
/// of colour down their leading edge — it is data, not decoration.
/// </summary>
public static class Theme
{
    private static readonly Color Junk = Color.FromArgb("#8A8A8A");
    private static readonly Color Basic = Color.FromArgb("#C8CDD2");
    private static readonly Color Fine = Color.FromArgb("#62A4DA");
    private static readonly Color Masterwork = Color.FromArgb("#1A9306");
    private static readonly Color Rare = Color.FromArgb("#FCD00B");
    private static readonly Color Exotic = Color.FromArgb("#FFA405");
    private static readonly Color Ascended = Color.FromArgb("#FB3E8D");
    private static readonly Color Legendary = Color.FromArgb("#4C139D");
    private static readonly Color Unknown = Color.FromArgb("#3A4653");

    /// <summary>The one accent, for anything the player still has to go and collect.</summary>
    public static readonly Color Brass = Color.FromArgb("#C8A34E");

    /// <summary>Primary text, for code that composes rows outside XAML.</summary>
    public static readonly Color Vellum = Color.FromArgb("#E6DECB");

    /// <summary>Secondary text, for code that composes rows outside XAML.</summary>
    public static readonly Color MistColor = Color.FromArgb("#7E8B9B");

    /// <summary>An equipment slot with nothing in it.</summary>
    public static readonly Color EmptySlot = Color.FromArgb("#26313D");

    /// <summary>Done and already taken — present, but no longer asking for attention.</summary>
    public static readonly Color Banked = Color.FromArgb("#2C3946");

    // The nine professions, in the colours the game uses for them.
    private static readonly Dictionary<string, Color> Professions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Guardian"] = Color.FromArgb("#72C1D9"),
        ["Warrior"] = Color.FromArgb("#FFD166"),
        ["Engineer"] = Color.FromArgb("#D09C59"),
        ["Ranger"] = Color.FromArgb("#8CDC82"),
        ["Thief"] = Color.FromArgb("#C08F95"),
        ["Elementalist"] = Color.FromArgb("#F68A87"),
        ["Mesmer"] = Color.FromArgb("#B679D5"),
        ["Necromancer"] = Color.FromArgb("#52A76F"),
        ["Revenant"] = Color.FromArgb("#D16E5A")
    };

    public static Color ProfessionColor(string? profession) =>
        profession is not null && Professions.TryGetValue(profession, out var color) ? color : Unknown;

    /// <summary>
    /// A dye's rarity, in the tiers a player already reads.
    ///
    /// Dyes have their own five names — Starter, Common, Uncommon, Rare, Exclusive — rather
    /// than the item rarities above, so they are mapped onto the same ladder of colour instead
    /// of being given a sixth palette to learn. Exclusive takes the ascended pink because it is
    /// the top of this ladder, not because it is ascended.
    /// </summary>
    public static Color DyeRarityColor(string? rarity) => rarity switch
    {
        "Starter" => Junk,
        "Common" => Basic,
        "Uncommon" => Fine,
        "Rare" => Rare,
        "Exclusive" => Ascended,
        _ => Unknown
    };

    public static Color RarityColor(string? rarity) => rarity switch
    {
        "Junk" => Junk,
        "Basic" => Basic,
        "Fine" => Fine,
        "Masterwork" => Masterwork,
        "Rare" => Rare,
        "Exotic" => Exotic,
        "Ascended" => Ascended,
        "Legendary" => Legendary,
        _ => Unknown
    };
}
