using GuildWarsCompanion.Gw2;

namespace GuildWarsCompanion.Services;

/// <summary>
/// One thing checked, and what to do if it failed.
///
/// Deliberately free of anything from MAUI. Every finding is drawn the same brass, which is a
/// decision belonging to the view — and keeping it there is what lets the checker, and its
/// fourteen game rules, be tested without a UI framework.
/// </summary>
public sealed record CheckFinding(string Title, string Detail);

/// <summary>The result of checking a character over.</summary>
public sealed record BuildCheck(
    string Score, IReadOnlyList<CheckFinding> Findings, string? AllClear, string Caveat);

/// <summary>
/// Checks a character for gaps, not for taste.
///
/// Nothing in the Guild Wars 2 API ranks one build against another — there is no meta data,
/// no benchmark, no notion of a good choice. So this reports only facts with fixes: an empty
/// slot, a missing rune, a specialization line left unspent. Every finding is something the
/// game itself would agree is incomplete, which is why it can never go stale.
/// </summary>
public sealed class BuildChecker
{
    /// <summary>Weapons that occupy both hands, and therefore take two sigils.</summary>
    private static readonly HashSet<string> TwoHanded = new(StringComparer.OrdinalIgnoreCase)
    {
        "Greatsword", "Hammer", "Longbow", "Shortbow", "Rifle", "Staff",
        "Spear", "Trident", "Harpoon Gun", "Speargun"
    };

    private static readonly string[] ArmourSlots =
        ["Helm", "Shoulders", "Coat", "Gloves", "Leggings", "Boots"];

    private static readonly string[] TrinketSlots =
        ["Backpack", "Amulet", "Ring1", "Ring2", "Accessory1", "Accessory2"];

    private const int MaxLevel = 80;

    public BuildCheck Run(
        Character character,
        IReadOnlyDictionary<int, WeaponItem> details,
        IReadOnlyList<SpecializationChoice> specializations,
        SkillBar? bar,
        bool revenant)
    {
        var worn = (character.Equipment ?? [])
            .Where(e => e.IsWorn && e.Slot is not null)
            .ToDictionary(e => e.Slot!, e => e);

        var findings = new List<CheckFinding>();
        var passed = 0;
        var total = 0;

        void Check(bool ok, string title, string detail)
        {
            total++;
            if (ok) passed++;
            else findings.Add(new CheckFinding(title, detail));
        }

        // --- gear present ---------------------------------------------------

        var missingArmour = ArmourSlots.Where(s => !worn.ContainsKey(s)).ToList();
        Check(missingArmour.Count == 0, L.T("Armour incomplete"),
            L.F("{0} of 6 pieces missing: {1}.", missingArmour.Count,
                string.Join(", ", missingArmour.Select(SlotWord))));

        var missingTrinkets = TrinketSlots.Where(s => !worn.ContainsKey(s)).ToList();
        Check(missingTrinkets.Count == 0, L.T("Trinkets incomplete"),
            L.F("{0} of 6 missing: {1}.", missingTrinkets.Count,
                string.Join(", ", missingTrinkets.Select(SlotWord))));

        var mainHand = worn.GetValueOrDefault("WeaponA1");
        Check(mainHand is not null, L.T("No main-hand weapon"),
            L.T("The first weapon set has nothing in the main hand."));

        // --- runes ----------------------------------------------------------

        var armourWorn = ArmourSlots.Where(worn.ContainsKey).Select(s => worn[s]).ToList();
        var runes = armourWorn
            .Select(piece => UpgradeOfType(piece, details, "Rune"))
            .ToList();

        var withoutRune = runes.Count(r => r is null);
        Check(armourWorn.Count == 0 || withoutRune == 0, L.T("Rune missing"),
            L.F("{0} of {1} armour pieces have no rune.", withoutRune, armourWorn.Count));

        var runeNames = runes.OfType<WeaponItem>().Select(r => r.Name).Distinct().ToList();
        Check(runeNames.Count <= 1, L.T("Rune set mixed"),
            runeNames.Count > 1
                ? L.F("{0} different runes: {1}. A set of six gives the full bonus.",
                    runeNames.Count, string.Join(", ", runeNames.Take(3)))
                : string.Empty);

        // --- sigils ---------------------------------------------------------

        var weaponSlots = new[] { "WeaponA1", "WeaponA2", "WeaponB1", "WeaponB2" };
        var sigilGaps = new List<string>();

        foreach (var slot in weaponSlots.Where(worn.ContainsKey))
        {
            var piece = worn[slot];
            var type = details.GetValueOrDefault(piece.Id)?.Details?.Type;
            var expected = type is not null && TwoHanded.Contains(type) ? 2 : 1;
            var actual = (piece.Upgrades ?? [])
                .Count(id => details.GetValueOrDefault(id)?.Details?.Type == "Sigil");

            if (actual < expected)
                sigilGaps.Add($"{(type is not null ? WeaponTypeName(type) : SlotWord(slot))} ({actual}/{expected})");
        }

        Check(sigilGaps.Count == 0, L.T("Sigil missing"),
            L.F("Empty sigil slots on: {0}.", string.Join(", ", sigilGaps)));

        // Identical sigils on the same weapon set do not stack, so the second one is doing
        // nothing. This is a game rule rather than an opinion, which is why it belongs here.
        foreach (var (first, slots) in new[]
                 {
                     (true, new[] { "WeaponA1", "WeaponA2" }),
                     (false, new[] { "WeaponB1", "WeaponB2" })
                 })
        {
            var sigils = slots
                .Where(worn.ContainsKey)
                .SelectMany(slot => worn[slot].Upgrades ?? [])
                .Where(id => details.GetValueOrDefault(id)?.Details?.Type == "Sigil")
                .ToList();

            var duplicated = sigils
                .GroupBy(id => id)
                .Where(g => g.Count() > 1)
                .Select(g => details.GetValueOrDefault(g.Key)?.Name ?? L.F("item {0}", g.Key))
                .ToList();

            if (slots.Any(worn.ContainsKey))
                Check(duplicated.Count == 0, L.T("Duplicate sigil wasted"),
                    // Two whole sentences rather than "first" dropped into one: the ordinal
                    // declines in German.
                    first
                        ? L.F("The first weapon set carries {0} twice. Identical sigils do not stack.",
                            string.Join(", ", duplicated))
                        : L.F("The second weapon set carries {0} twice. Identical sigils do not stack.",
                            string.Join(", ", duplicated)));
        }

        // --- gear level -----------------------------------------------------

        var outdated = worn.Values
            .Select(piece => details.GetValueOrDefault(piece.Id))
            .OfType<WeaponItem>()
            .Where(item => item.Level > 0 && item.Level < character.Level - 10)
            .ToList();

        Check(outdated.Count == 0, L.T("Gear behind your level"),
            L.F("{0} pieces are more than 10 levels below {1}.", outdated.Count, character.Level));

        // --- specializations ------------------------------------------------

        var chosen = specializations.Count(s => s.Id is not null);
        var expectedLines = character.Level >= MaxLevel ? 3 : Math.Min(3, character.Level / 21 + 1);
        Check(chosen >= expectedLines, L.T("Specialization line unused"),
            L.F("{0} of {1} lines chosen at level {2}.", chosen, expectedLines, character.Level));

        var incompleteTraits = specializations
            .Where(s => s.Id is not null)
            .Count(s => (s.Traits ?? []).Count(t => t is not null) < 3);
        Check(incompleteTraits == 0, L.T("Traits not fully picked"),
            L.F("{0} chosen line(s) have fewer than three traits.", incompleteTraits));

        // --- skills ---------------------------------------------------------
        // A revenant's bar comes from its legend, so empty slots there mean nothing.

        if (!revenant)
        {
            Check(bar?.Heal is not null, L.T("No healing skill"), L.T("The heal slot is empty."));

            var utilities = (bar?.Utilities ?? []).Count(u => u is not null);
            Check(utilities >= 3, L.T("Utility slot empty"), L.F("{0} of 3 utility skills slotted.", utilities));

            Check(bar?.Elite is not null || character.Level < MaxLevel,
                L.T("No elite skill"), L.T("The elite slot is empty."));
        }

        return new BuildCheck(
            $"{passed} / {total}",
            findings,
            findings.Count == 0 ? L.T("Nothing missing. Every slot filled and every line spent.") : null,
            L.T("Checks completeness only. Nothing in the API ranks builds, so this never judges your choices."));
    }

    /// <summary>The upgrade of a given kind on a piece, if any.</summary>
    /// <summary>
    /// An API slot id as a finding names it. English keeps the id itself ("Helm", "Ring1"), as it
    /// always has; another language gets the slot's name.
    /// </summary>
    private static string SlotWord(string slot) => slot switch
    {
        "Helm" => L.T("Helm"),
        "Shoulders" => L.T("Shoulders"),
        "Coat" => L.T("Coat"),
        "Gloves" => L.T("Gloves"),
        "Leggings" => L.T("Leggings"),
        "Boots" => L.T("Boots"),
        "Backpack" => L.T("Backpack"),
        "Amulet" => L.T("Amulet"),
        "Ring1" => L.T("Ring1"),
        "Ring2" => L.T("Ring2"),
        "Accessory1" => L.T("Accessory1"),
        "Accessory2" => L.T("Accessory2"),
        "WeaponA1" => L.T("WeaponA1"),
        "WeaponA2" => L.T("WeaponA2"),
        "WeaponB1" => L.T("WeaponB1"),
        "WeaponB2" => L.T("WeaponB2"),
        _ => slot
    };

    /// <summary>A weapon type id from the API, as the game names it in the phone's language.</summary>
    internal static string WeaponTypeName(string type) => type switch
    {
        "Axe" => L.T("Axe"),
        "Dagger" => L.T("Dagger"),
        "Mace" => L.T("Mace"),
        "Pistol" => L.T("Pistol"),
        "Scepter" => L.T("Scepter"),
        "Sword" => L.T("Sword"),
        "Focus" => L.T("Focus"),
        "Shield" => L.T("Shield"),
        "Torch" => L.T("Torch"),
        "Warhorn" => L.T("Warhorn"),
        "Greatsword" => L.T("Greatsword"),
        "Hammer" => L.T("Hammer"),
        // The API spells the bows "LongBow" and "ShortBow" on an item.
        "Longbow" or "LongBow" => L.T("Longbow"),
        "Rifle" => L.T("Rifle"),
        "Shortbow" or "ShortBow" => L.T("Shortbow"),
        "Staff" => L.T("Staff"),
        "Spear" => L.T("Spear"),
        "Trident" => L.T("Trident"),
        "Harpoon Gun" => L.T("Harpoon Gun"),
        "Speargun" => L.T("Speargun"),
        _ => type
    };

    private static WeaponItem? UpgradeOfType(
        EquipmentPiece piece, IReadOnlyDictionary<int, WeaponItem> details, string kind) =>
        (piece.Upgrades ?? [])
            .Select(id => details.GetValueOrDefault(id))
            .FirstOrDefault(item => item?.Details?.Type == kind);
}
