using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// Reading a character's equipment templates.
///
/// A character carries several loadouts and wears one. Everything the app showed before came
/// from the top-level equipment array, which is every tab's contents run together — so a
/// second set of gear was invisible, and a legendary shared between tabs looked duplicated.
/// The tabs are what separate them.
/// </summary>
public static class Loadouts
{
    /// <summary>
    /// The tab to show: the one asked for, else the one being worn, else the first.
    ///
    /// `is_active` is the authority rather than the tab number — the numbers are positions,
    /// and nothing promises the worn one is first.
    /// </summary>
    public static EquipmentTab? Pick(IEnumerable<EquipmentTab>? tabs, int? requested)
    {
        var all = (tabs ?? []).ToList();

        if (all.Count == 0) return null;

        return all.FirstOrDefault(tab => tab.Tab == requested)
               ?? all.FirstOrDefault(tab => tab.IsActive)
               ?? all[0];
    }

    /// <summary>
    /// What to call a tab. Most are unnamed — the API sends an empty string, not null — and
    /// "Tab 2" at least matches what the game's own template picker shows.
    /// </summary>
    public static string Label(EquipmentTab tab) =>
        string.IsNullOrWhiteSpace(tab.Name) ? $"Tab {tab.Tab}" : tab.Name.Trim();

    /// <summary>
    /// True when a template has no PvP gear at all. Untouched templates report an amulet, a
    /// rune and four sigils that are all null, and showing four empty sigil rows for that
    /// would be a screen full of nothing.
    /// </summary>
    public static bool HasPvpGear(PvpLoadout? pvp) =>
        pvp is not null &&
        (pvp.Amulet is not null || pvp.Rune is not null ||
         (pvp.Sigils ?? []).Any(sigil => sigil is not null));

    /// <summary>
    /// Why the gear list is empty, when it is.
    ///
    /// These are two different stories and only one of them is the player's problem. A key
    /// without the 'builds' permission returns no equipment anywhere; an unused template
    /// returns none of its own while every other template loads fine. Blaming the key for an
    /// empty template sends someone off to reissue an API key that was never wrong.
    /// </summary>
    public static string? EmptyNotice(int pieces, int templates)
    {
        if (pieces > 0) return null;

        return templates > 0
            ? "Nothing is equipped in this template."
            : "No equipment returned. The API key needs the 'builds' permission to read it.";
    }

    /// <summary>
    /// The pieces a tab actually wears. Legendary gear reports as drawn from the armory
    /// rather than equipped directly, and it is worn all the same.
    /// </summary>
    public static IReadOnlyList<EquipmentPiece> Worn(EquipmentTab? tab) =>
        (tab?.Equipment ?? []).Where(piece => piece.IsWorn && piece.Slot is not null).ToList();
}
