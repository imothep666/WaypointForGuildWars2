using GuildWarsCompanion.Gw2;
using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// The build check: fourteen rules about whether a character is finished being kitted out.
///
/// Every rule is a fact the game itself would agree with — an empty slot, a rune missing, a
/// second identical sigil that does nothing. None of them is an opinion about what is good,
/// which is what stops the feature going stale with the meta. These tests exist because the
/// rules encode real game knowledge that is easy to break and impossible to spot: a wrong
/// sigil count produces a finding that reads perfectly plausibly.
/// </summary>
public class BuildCheckerTests
{
    private static readonly string[] Armour =
        ["Helm", "Shoulders", "Coat", "Gloves", "Leggings", "Boots"];

    private static readonly string[] Trinkets =
        ["Backpack", "Amulet", "Ring1", "Ring2", "Accessory1", "Accessory2"];

    private const int Rune = 1000, Sigil = 2000, OtherSigil = 2001, Sword = 3000, Greatsword = 3001;

    /// <summary>Item ids the checker can look up: runes, sigils and the weapons themselves.</summary>
    private static Dictionary<int, WeaponItem> Details(int level = 80) => new()
    {
        [Rune] = new WeaponItem(Rune, "Superior Rune of the Pack", level, new WeaponDetails("Rune", null)),
        [Sigil] = new WeaponItem(Sigil, "Superior Sigil of Force", level, new WeaponDetails("Sigil", null)),
        [OtherSigil] = new WeaponItem(OtherSigil, "Superior Sigil of Air", level, new WeaponDetails("Sigil", null)),
        [Sword] = new WeaponItem(Sword, "Sword", level, new WeaponDetails("Sword", null)),
        [Greatsword] = new WeaponItem(Greatsword, "Greatsword", level, new WeaponDetails("Greatsword", null))
    };

    private static EquipmentPiece Piece(string slot, int id = Sword, params int[] upgrades) =>
        new(id, slot, "Equipped", upgrades.Length == 0 ? null : upgrades, null);

    /// <summary>A character with nothing wrong: every slot filled, every line spent.</summary>
    private static (Character Character, List<SpecializationChoice> Specs, SkillBar Bar) Complete()
    {
        var equipment = new List<EquipmentPiece>();

        foreach (var slot in Armour) equipment.Add(Piece(slot, Sword, Rune));
        foreach (var slot in Trinkets) equipment.Add(Piece(slot));

        // Two one-handed sets, each carrying two different sigils.
        equipment.Add(Piece("WeaponA1", Sword, Sigil));
        equipment.Add(Piece("WeaponA2", Sword, OtherSigil));
        equipment.Add(Piece("WeaponB1", Sword, Sigil));
        equipment.Add(Piece("WeaponB2", Sword, OtherSigil));

        var character = new Character(
            "Tester", "Human", "Female", "Thief", 80, 3600, 0, DateTimeOffset.UtcNow,
            equipment, 0, null, null, null);

        var specs = Enumerable.Range(1, 3)
            .Select(i => new SpecializationChoice(i, [1, 2, 3]))
            .ToList();

        return (character, specs, new SkillBar(1, [1, 2, 3], 9, null));
    }

    private static BuildCheck Run(
        Character character,
        List<SpecializationChoice> specs,
        SkillBar? bar,
        Dictionary<int, WeaponItem>? details = null,
        bool revenant = false) =>
        new BuildChecker().Run(character, details ?? Details(), specs, bar, revenant);

    private static bool Found(BuildCheck check, string title) =>
        check.Findings.Any(f => f.Title == title);

    // --- the happy path -----------------------------------------------------

    [Fact]
    public void A_fully_kitted_character_has_nothing_to_report()
    {
        var (character, specs, bar) = Complete();

        var check = Run(character, specs, bar);

        Assert.Empty(check.Findings);
        Assert.NotNull(check.AllClear);
    }

    [Fact]
    public void The_score_counts_every_check_not_just_the_failures()
    {
        var (character, specs, bar) = Complete();

        var check = Run(character, specs, bar);

        // "n / n" when nothing is wrong — the denominator is how many rules ran.
        var parts = check.Score.Split('/', StringSplitOptions.TrimEntries);
        Assert.Equal(parts[0], parts[1]);
        Assert.True(int.Parse(parts[1]) >= 10, "the checker should be running its full rule set");
    }

    [Fact]
    public void The_caveat_is_always_present()
    {
        // The screen must never imply the app is judging build quality.
        var (character, specs, bar) = Complete();

        Assert.False(string.IsNullOrWhiteSpace(Run(character, specs, bar).Caveat));
    }

    // --- gear present -------------------------------------------------------

    [Fact]
    public void Missing_armour_is_reported_with_the_slots_named()
    {
        var (character, specs, bar) = Complete();
        var stripped = character with
        {
            Equipment = character.Equipment!.Where(e => e.Slot != "Helm").ToList()
        };

        var check = Run(stripped, specs, bar);

        Assert.True(Found(check, "Armour incomplete"));
        Assert.Contains("Helm", check.Findings.First(f => f.Title == "Armour incomplete").Detail);
    }

    [Fact]
    public void An_empty_main_hand_is_reported()
    {
        var (character, specs, bar) = Complete();
        var stripped = character with
        {
            Equipment = character.Equipment!.Where(e => e.Slot != "WeaponA1").ToList()
        };

        Assert.True(Found(Run(stripped, specs, bar), "No main-hand weapon"));
    }

    [Fact]
    public void Gear_only_counts_when_it_is_actually_worn()
    {
        // The equipment array also carries every template's contents; something sitting in
        // the armory is not equipped and must not satisfy a slot.
        var (character, specs, bar) = Complete();

        var armoury = character with
        {
            Equipment = character.Equipment!
                .Select(e => e.Slot == "Coat" ? e with { Location = "Armory" } : e)
                .ToList()
        };

        Assert.True(Found(Run(armoury, specs, bar), "Armour incomplete"));
    }

    // --- runes --------------------------------------------------------------

    [Fact]
    public void Armour_without_runes_is_reported()
    {
        var (character, specs, bar) = Complete();
        var bare = character with
        {
            Equipment = character.Equipment!
                .Select(e => Armour.Contains(e.Slot) ? e with { Upgrades = null } : e)
                .ToList()
        };

        Assert.True(Found(Run(bare, specs, bar), "Rune missing"));
    }

    [Fact]
    public void A_mixed_rune_set_is_reported_because_six_of_one_is_the_bonus()
    {
        var (character, specs, bar) = Complete();
        var details = Details();
        details[1001] = new WeaponItem(1001, "Superior Rune of the Scholar", 80, new WeaponDetails("Rune", null));

        var mixed = character with
        {
            Equipment = character.Equipment!
                .Select(e => e.Slot == "Helm" ? e with { Upgrades = new[] { 1001 } } : e)
                .ToList()
        };

        Assert.True(Found(Run(mixed, specs, bar, details), "Rune set mixed"));
    }

    // --- sigils -------------------------------------------------------------

    [Fact]
    public void A_two_handed_weapon_wants_two_sigils()
    {
        // The rule that is easiest to get wrong and impossible to notice: a greatsword takes
        // both hands, so it carries two sigils where a sword carries one.
        var (character, specs, bar) = Complete();

        var twoHanded = character with
        {
            Equipment = character.Equipment!
                .Where(e => e.Slot != "WeaponA2")
                .Select(e => e.Slot == "WeaponA1"
                    ? new EquipmentPiece(Greatsword, "WeaponA1", "Equipped", [Sigil], null)
                    : e)
                .ToList()
        };

        var check = Run(twoHanded, specs, bar);

        Assert.True(Found(check, "Sigil missing"));
        Assert.Contains("1/2", check.Findings.First(f => f.Title == "Sigil missing").Detail);
    }

    [Fact]
    public void A_two_handed_weapon_with_both_sigils_passes()
    {
        var (character, specs, bar) = Complete();

        var twoHanded = character with
        {
            Equipment = character.Equipment!
                .Where(e => e.Slot != "WeaponA2")
                .Select(e => e.Slot == "WeaponA1"
                    ? new EquipmentPiece(Greatsword, "WeaponA1", "Equipped", [Sigil, OtherSigil], null)
                    : e)
                .ToList()
        };

        Assert.False(Found(Run(twoHanded, specs, bar), "Sigil missing"));
    }

    [Fact]
    public void The_same_sigil_twice_on_one_set_is_wasted()
    {
        // Identical sigils do not stack, so the second is doing nothing. A game rule, not an
        // opinion — which is why it belongs in a completeness check at all.
        var (character, specs, bar) = Complete();

        var doubled = character with
        {
            Equipment = character.Equipment!
                .Select(e => e.Slot == "WeaponA2" ? e with { Upgrades = new[] { Sigil } } : e)
                .ToList()
        };

        Assert.True(Found(Run(doubled, specs, bar), "Duplicate sigil wasted"));
    }

    [Fact]
    public void The_same_sigil_on_different_sets_is_fine()
    {
        // Only one set is drawn at a time, so a sigil on each is not a duplicate.
        var (character, specs, bar) = Complete();

        Assert.False(Found(Run(character, specs, bar), "Duplicate sigil wasted"));
    }

    // --- level ---------------------------------------------------------------

    [Fact]
    public void Gear_far_behind_your_level_is_reported()
    {
        var (character, specs, bar) = Complete();

        Assert.True(Found(Run(character, specs, bar, Details(level: 60)), "Gear behind your level"));
    }

    [Fact]
    public void Gear_slightly_behind_is_left_alone()
    {
        // Ten levels of slack: replacing gear every level is not something to nag about.
        var (character, specs, bar) = Complete();

        Assert.False(Found(Run(character, specs, bar, Details(level: 75)), "Gear behind your level"));
    }

    // --- specializations -----------------------------------------------------

    [Fact]
    public void An_unspent_specialization_line_is_reported()
    {
        var (character, specs, bar) = Complete();

        Assert.True(Found(Run(character, specs.Take(1).ToList(), bar), "Specialization line unused"));
    }

    [Fact]
    public void A_low_level_character_is_not_expected_to_have_three_lines()
    {
        // Lines unlock with level; asking a level 20 for three would be nonsense.
        var (character, specs, bar) = Complete();
        var young = character with { Level = 20 };

        Assert.False(Found(
            Run(young, specs.Take(1).ToList(), bar), "Specialization line unused"));
    }

    [Fact]
    public void A_chosen_line_missing_traits_is_reported()
    {
        var (character, specs, bar) = Complete();
        var half = specs.Select((s, i) => i == 0 ? new SpecializationChoice(s.Id, [1, null, null]) : s)
            .ToList();

        Assert.True(Found(Run(character, half, bar), "Traits not fully picked"));
    }

    // --- skills --------------------------------------------------------------

    [Fact]
    public void An_empty_heal_slot_is_reported()
    {
        var (character, specs, _) = Complete();

        Assert.True(Found(Run(character, specs, new SkillBar(null, [1, 2, 3], 9, null)), "No healing skill"));
    }

    [Fact]
    public void Missing_utilities_are_counted()
    {
        var (character, specs, _) = Complete();

        Assert.True(Found(Run(character, specs, new SkillBar(1, [1, null, null], 9, null)), "Utility slot empty"));
    }

    [Fact]
    public void A_revenant_is_not_asked_about_its_skill_bar()
    {
        // A revenant's utilities come from the equipped legend, so the template leaves them
        // empty and an empty bar means nothing at all.
        var (character, specs, _) = Complete();
        var empty = new SkillBar(null, [null, null, null], null, ["Legend2"]);

        var check = Run(character, specs, empty, revenant: true);

        Assert.False(Found(check, "No healing skill"));
        Assert.False(Found(check, "Utility slot empty"));
        Assert.False(Found(check, "No elite skill"));
    }

    [Fact]
    public void An_elite_is_only_expected_at_maximum_level()
    {
        var (character, specs, _) = Complete();
        var young = character with { Level = 40 };

        Assert.False(Found(
            Run(young, specs, new SkillBar(1, [1, 2, 3], null, null)), "No elite skill"));

        Assert.True(Found(
            Run(character, specs, new SkillBar(1, [1, 2, 3], null, null)), "No elite skill"));
    }
}
