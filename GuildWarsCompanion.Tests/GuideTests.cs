using GuildWarsCompanion.Services;
using Xunit;

namespace GuildWarsCompanion.Tests;

/// <summary>
/// Holding a build guide against a character: the build code, MetaBattle's page, and the
/// comparison. The ways to be wrong worth pinning are reading a code's bytes off by one, taking a
/// different name for a different item when it is the same stats, and calling something missing
/// that the account already holds.
/// </summary>
public class GuideTests
{
    // --- the build code ---------------------------------------------------------

    /// <summary>The player's own thief, copied from the game after Secrets of the Obscure.</summary>
    private const string Thief = "[&DQUjHxw9OiqFAIUArBYZAVsBWwFYAFgBKAAoAAAAAAAAAAAAAAAAAAAAAAACNgBVAAA=]";

    [Fact]
    public void A_code_names_the_profession_lines_and_traits()
    {
        var code = BuildCode.Find(Thief)!;

        Assert.Equal(5, code.Profession);
        Assert.Equal("Thief", code.ProfessionName);
        Assert.Equal([35, 28, 58], code.Lines.Select(line => line.Specialization));

        // 0x1F: top, then 3 (bottom), then 1 (top), read from the low bits up.
        Assert.Equal([3, 3, 1], code.Lines[0].Choices);
        Assert.Equal([2, 2, 2], code.Lines[2].Choices);
    }

    [Fact]
    public void A_code_reads_the_land_skills_and_skips_the_underwater_ones()
    {
        var code = BuildCode.Find(Thief)!;

        Assert.Equal(133, code.Heal);
        Assert.Equal([5804, 347, 88], code.Utilities);
        Assert.Equal(40, code.Elite);
    }

    [Fact]
    public void A_code_since_Secrets_of_the_Obscure_carries_its_weapons()
    {
        var code = BuildCode.Find(Thief)!;

        Assert.Equal([54, 85], code.Weapons);
        Assert.Equal(["Pistol", "Rifle"], code.Weapons.Select(BuildCode.WeaponType));
        Assert.Empty(code.Overrides);
    }

    /// <summary>MetaBattle still prints many codes from before the weapons were added.</summary>
    [Fact]
    public void An_older_code_without_weapons_still_reads()
    {
        var code = BuildCode.Find("{{TemplateCode|\ncode = [&DQUcPSMbOh0MAQAAHxYAACAXAAAzAQAAPRYAAAAAAAAAAAAAAAAAAAAAAAA=]\n}}")!;

        Assert.Equal(5, code.Profession);
        Assert.Empty(code.Weapons);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no code here")]
    [InlineData("[&AgGqtgAA]")]
    [InlineData("[&!!!]")]
    public void Anything_else_is_no_code(string text) => Assert.Null(BuildCode.Find(text));

    // --- MetaBattle -------------------------------------------------------------

    [Theory]
    [InlineData("https://metabattle.com/wiki/Build:Deadeye_-_Power_DPS_Rifle", "Build:Deadeye - Power DPS Rifle")]
    [InlineData("see metabattle.com/wiki/Build:Deadeye_-_S/D_D/P_Deadeye#Equipment", "Build:Deadeye - S/D D/P Deadeye")]
    [InlineData("https://metabattle.com/wiki/Build:Deadeye_-_Staff_Duelist_(sPvP)", "Build:Deadeye - Staff Duelist (sPvP)")]
    public void A_link_gives_the_page_title(string link, string title) =>
        Assert.Equal(title, MetaBattle.TitleFrom(link));

    [Fact]
    public void Other_links_are_not_MetaBattle() =>
        Assert.Null(MetaBattle.TitleFrom("https://snowcrows.com/builds/thief/deadeye"));

    private const string Page = """
        {{Build
        | profession = Thief
        | specialization = Deadeye
        }}
        ==Template Code==
        {{TemplateCode|
        code = [&DQUcPSMbOh0MAQAAHxYAACAXAAAzAQAAPRYAAAAAAAAAAAAAAAAAAAAAAAA=]
        }}
        ==Equipment==
        {{PvE equipment
        | stats = Berserker
        | weight = Medium
        | rune = Superior Rune of the Dragonhunter
        | rune-qt = 6
        | weapon1 = Rifle
        | weapon2 =
        | weapon3 = Dagger
        | weapon4 = Dagger
        | sigil1 = Superior Sigil of Force
        | sigil2 = Superior Sigil of Impact
        | sigil3 = Superior Sigil of Force
        | sigil4 = Superior Sigil of Impact
        | relic = Relic of the Deadeye
        <!-- Custom stats -->
        | head =
        | shoulders = Assassin
        | ring1 =
        }}
        * If you do not have stat infusions, switch one accessory to {{tooltip|Assassin stats}}.
        """;

    [Fact]
    public void A_page_gives_its_code_and_gear()
    {
        var guide = MetaBattle.Parse("Build:Deadeye - Power DPS Rifle", Page);

        Assert.Equal("Deadeye - Power DPS Rifle", guide.Title);
        Assert.Equal("Thief", guide.Profession);
        Assert.Equal(5, guide.Code?.Profession);

        var gear = guide.Gear!;

        Assert.False(gear.IsPvp);
        Assert.Equal("Berserker", gear.Stats);
        Assert.Equal("Superior Rune of the Dragonhunter", gear.Rune);
        Assert.Equal("Relic of the Deadeye", gear.Relic);
        Assert.Equal(["Rifle", null, "Dagger", "Dagger"], gear.Weapons);
        Assert.Equal("Superior Sigil of Impact", gear.Sigils[3]);

        // Only the slot given its own stats; empty fields are not stats.
        Assert.Equal(new Dictionary<string, string> { ["Shoulders"] = "Assassin" }, gear.SlotStats);
    }

    /// <summary>Editors fill the equipment's weapon fields two ways; the skill bar decides the hands,
    /// and each sigil follows its weapon.</summary>
    [Fact]
    public void A_one_set_build_written_across_the_fields_is_one_set()
    {
        var gear = MetaBattle.Parse("Build:X", """
            {{Skill bar
            | weapon1 = Pistol
            | weapon2 = Dagger
            }}
            {{PvE equipment
            | stats = Viper
            | weapon1 = Pistol
            | weapon4 = Dagger
            | sigil1 = Superior Sigil of Earth
            | sigil4 = Superior Sigil of Doom
            }}
            """).Gear!;

        Assert.Equal(["Pistol", "Dagger", null, null], gear.Weapons);
        Assert.Equal(["Superior Sigil of Earth", "Superior Sigil of Doom", null, null], gear.Sigils);
    }

    [Fact]
    public void Two_two_handers_are_two_sets()
    {
        var gear = MetaBattle.Parse("Build:X", """
            {{Skill bar
            | weapon1 = Staff
            | weapon2 = Rifle
            }}
            {{PvE equipment
            | weapon1 = Staff
            | sigil1 = Superior Sigil of Force
            | sigil2 = Superior Sigil of Impact
            | weapon3 = Rifle
            | sigil3 = Superior Sigil of Air
            | sigil4 = Superior Sigil of Energy
            }}
            """).Gear!;

        Assert.Equal(["Staff", null, "Rifle", null], gear.Weapons);
        Assert.Equal(["Superior Sigil of Force", "Superior Sigil of Impact", "Superior Sigil of Air", "Superior Sigil of Energy"],
            gear.Sigils);
    }

    [Theory]
    [InlineData("rifle", "Rifle")]
    [InlineData("Short bow", "ShortBow")]
    [InlineData("Longbow", "LongBow")]
    public void A_guide_s_weapon_is_spelled_as_the_API_does(string written, string api) =>
        Assert.Equal(api, GuideService.ApiWeaponType(written));

    [Fact]
    public void A_PvP_page_says_so()
    {
        var guide = MetaBattle.Parse("Build:Deadeye - Staff Duelist (sPvP)",
            "{{PvP equipment\n| weapon1 = Staff\n| sigil1  = Enhancement\n| amulet  = Marauder\n}}");

        Assert.True(guide.Gear?.IsPvp);
        Assert.Null(guide.Code);
    }

    [Fact]
    public void A_page_without_equipment_has_no_gear() =>
        Assert.Null(MetaBattle.Parse("Build:X", "{{Build\n| profession = Thief\n}}").Gear);

    // --- the comparison ---------------------------------------------------------

    private static readonly StatSet Berserker = new("Berserker's", ["Power 0.35", "Precision 0.25", "CritDamage 0.25"]);
    private static readonly StatSet Zojja = new("Zojja's", ["Power 0.35", "Precision 0.25", "CritDamage 0.25"]);
    private static readonly StatSet Assassin = new("Assassin's", ["Precision 0.35", "Power 0.25", "CritDamage 0.25"]);
    private static readonly StatSet Marauder = new("Marauder's", ["Power 0.3", "Precision 0.3", "Vitality 0.165", "CritDamage 0.165"]);

    private static WornPiece Worn(string slot, string? type, StatSet? stats, params int[] upgrades) =>
        new(slot, slot.GetHashCode(), slot + " item", type, stats, upgrades, false);

    private static GuideInput Input(
        Guide? guide = null,
        IReadOnlyList<GuideTraitLine>? guideLines = null,
        IReadOnlyList<GuideTraitLine>? yourLines = null,
        GuideSkillBar? guideSkills = null,
        GuideSkillBar? yourSkills = null,
        IReadOnlyList<string?>? weapons = null,
        bool bySet = true,
        IReadOnlyList<WornPiece>? worn = null,
        Dictionary<string, (string, StatSet?)>? stats = null,
        GuideUpgrade? rune = null,
        IReadOnlyList<OwnedPiece>? owned = null) =>
        new(
            guide ?? new Guide("Test", null, "Thief", null,
                new GuideGear("Berserker", new Dictionary<string, string>(), null, [], [], null, false)),
            guideLines ?? [], yourLines ?? [],
            guideSkills ?? new GuideSkillBar(null, [], null), yourSkills ?? new GuideSkillBar(null, [], null),
            weapons ?? [], bySet,
            (worn ?? []).ToDictionary(piece => piece.Slot),
            stats ?? new Dictionary<string, (string, StatSet?)>(),
            rune, [], null, new Dictionary<int, string>(), owned ?? [], "Medium", []);

    private static GuideLine Line(GuideReport report, string heading, string label) =>
        report.Sections.Single(section => section.Heading == heading).Lines.First(line => line.Label == label);

    [Fact]
    public void A_line_with_one_trait_off_names_the_tier()
    {
        var report = GuideComparison.Report(Input(
            guideLines: [new GuideTraitLine(58, "Deadeye", ["Premeditation", "Iron Sight", "Maleficent Seven"])],
            yourLines: [new GuideTraitLine(58, "Deadeye", ["Premeditation", "One in the Chamber", "Maleficent Seven"])]));

        var line = Line(report, "SPECIALIZATIONS", "Deadeye");

        Assert.Equal(GuideMark.Different, line.Mark);
        Assert.Equal("Tier 2: Iron Sight instead of One in the Chamber", line.Note);
    }

    [Fact]
    public void A_line_not_taken_is_missing() =>
        Assert.Equal(GuideMark.Missing, Line(GuideComparison.Report(Input(
            guideLines: [new GuideTraitLine(28, "Deadly Arts", ["a", "b", "c"])],
            yourLines: [new GuideTraitLine(44, "Trickery", ["a", "b", "c"])])), "SPECIALIZATIONS", "Deadly Arts").Mark);

    /// <summary>Utilities in another order are the same build.</summary>
    [Fact]
    public void Utilities_compare_in_any_order()
    {
        var report = GuideComparison.Report(Input(
            guideSkills: new GuideSkillBar("Hide in Shadows", ["Shadow Gust", "Mercy", "Assassin's Signet"], "Shadow Meld"),
            yourSkills: new GuideSkillBar("Hide in Shadows", ["Mercy", "Haste", "Shadow Gust"], "Thieves Guild")));

        var skills = report.Sections.Single(section => section.Heading == "SKILLS").Lines;

        Assert.Equal(
            [GuideMark.Same, GuideMark.Same, GuideMark.Same, GuideMark.Different, GuideMark.Different],
            skills.Select(line => line.Mark));
        Assert.Equal("Haste", skills[3].Yours);
    }

    [Fact]
    public void Weapon_sets_match_whichever_set_carries_them()
    {
        var report = GuideComparison.Report(Input(
            weapons: ["Rifle", null, "Dagger", "Dagger"],
            worn: [Worn("WeaponA1", "Dagger", Berserker), Worn("WeaponA2", "Dagger", Berserker), Worn("WeaponB1", "Rifle", Berserker)]));

        Assert.All(report.Sections.Single(section => section.Heading == "WEAPONS").Lines,
            line => Assert.Equal(GuideMark.Same, line.Mark));
    }

    [Fact]
    public void A_missing_weapon_is_looked_for_on_the_account()
    {
        var report = GuideComparison.Report(Input(
            weapons: ["Rifle", null],
            worn: [Worn("WeaponA1", "Sword", Berserker)],
            owned: [new OwnedPiece(1, "Berserker's Rifle", "Rifle", null, Berserker, false, "1 in the bank")]));

        var line = Line(report, "WEAPONS", "Weapon set 1");

        Assert.Equal(GuideMark.Different, line.Mark);
        Assert.Equal("1 in the bank: Berserker's Rifle", line.Note);
    }

    /// <summary>A set's stats are on its line. Seen on the phone when they were hand by hand: a
    /// shortbow was "same" against the guide's stats for a dagger.</summary>
    [Fact]
    public void A_weapon_set_carries_its_stats()
    {
        var report = GuideComparison.Report(Input(
            weapons: ["Rifle", null, "Dagger", "Dagger"],
            worn: [Worn("WeaponA1", "Rifle", Berserker), Worn("WeaponB1", "Dagger", Berserker), Worn("WeaponB2", "Dagger", Marauder)],
            stats: new() { [""] = ("Berserker", Berserker) }));

        var rifle = Line(report, "WEAPONS", "Weapon set 1");
        var daggers = Line(report, "WEAPONS", "Weapon set 2");

        Assert.Equal(GuideMark.Same, rifle.Mark);
        Assert.Equal("Rifle · Berserker's", rifle.Guide);

        Assert.Equal(GuideMark.Different, daggers.Mark);
        Assert.Equal("Dagger / Dagger · Berserker's / Marauder's", daggers.Yours);
        Assert.Equal("Not on the account", daggers.Note);

        // And there are no lines for the hands under STATS.
        Assert.DoesNotContain(report.Sections.Single(section => section.Heading == "STATS").Lines,
            line => line.Label.Contains("hand"));
    }

    [Fact]
    public void A_sigil_is_named_by_its_weapon()
    {
        var input = Input(weapons: ["Rifle", null, "Dagger", "Dagger"]) with
        {
            Sigils =
            [
                new GuideUpgrade("Superior Sigil of Force", 24615, 0, null, null),
                new GuideUpgrade("Superior Sigil of Impact", 24868, 0, null, null),
                null,
                new GuideUpgrade("Superior Sigil of Impact", 24868, 0, null, null)
            ]
        };

        var labels = GuideComparison.Report(input).Sections.Single(section => section.Heading == "UPGRADES")
            .Lines.Select(line => line.Label);

        Assert.Equal(["Sigil, Rifle", "Sigil, Rifle", "Sigil, Dagger"], labels);
    }

    /// <summary>The point of comparing attributes: ascended gear carries a name of its own.</summary>
    [Fact]
    public void Zojja_s_is_Berserker_s_by_another_name()
    {
        var report = GuideComparison.Report(Input(
            worn: [Worn("Coat", "Coat", Zojja), Worn("Helm", "Helm", Berserker), Worn("Boots", "Boots", Marauder)],
            stats: new() { [""] = ("Berserker", Berserker) }));

        Assert.Equal(GuideMark.Equivalent, Line(report, "STATS", "Chest").Mark);
        Assert.Equal(GuideMark.Same, Line(report, "STATS", "Head").Mark);
        Assert.Equal(GuideMark.Different, Line(report, "STATS", "Feet").Mark);
    }

    /// <summary>Seen on the phone: the same three attributes, weighted the other way round, are other stats.</summary>
    [Fact]
    public void Assassin_s_is_not_Berserker_s()
    {
        Assert.False(Assassin.SameEffect(Berserker));
        Assert.Equal("Power 0.35", StatSet.Weighted(new GuildWarsCompanion.Gw2.ItemStatAttribute("Power", 0.35)));
    }

    [Fact]
    public void A_slot_with_its_own_stats_is_held_to_them()
    {
        var report = GuideComparison.Report(Input(
            worn: [Worn("Shoulders", "Shoulders", Berserker)],
            stats: new() { [""] = ("Berserker", Berserker), ["Shoulders"] = ("Assassin", Assassin) }));

        Assert.Equal(GuideMark.Different, Line(report, "STATS", "Shoulders").Mark);
    }

    [Fact]
    public void Different_stats_point_at_a_piece_on_the_account_of_the_right_weight()
    {
        var report = GuideComparison.Report(Input(
            worn: [Worn("Boots", "Boots", Marauder)],
            stats: new() { [""] = ("Berserker", Berserker) },
            owned:
            [
                new OwnedPiece(1, "Heavy boots", "Boots", "Heavy", Berserker, false, "1 in the bank"),
                new OwnedPiece(2, "Zojja's Boots", "Boots", "Medium", Zojja, false, "1 on Imosan")
            ]));

        Assert.Equal("1 on Imosan: Zojja's Boots", Line(report, "STATS", "Feet").Note);
    }

    [Fact]
    public void Nothing_held_says_so()
    {
        var report = GuideComparison.Report(Input(
            worn: [Worn("Boots", "Boots", Marauder)],
            stats: new() { [""] = ("Berserker", Berserker) }));

        Assert.Equal("Not on the account", Line(report, "STATS", "Feet").Note);
    }

    [Fact]
    public void A_rune_on_four_pieces_is_two_short_and_priced()
    {
        var rune = new GuideUpgrade("Superior Rune of the Dragonhunter", 74978, 0, null, 15000);

        var report = GuideComparison.Report(Input(
            rune: rune,
            worn: GuideComparison.ArmourSlots.Select((slot, i) => Worn(slot, slot, Berserker, i < 4 ? 74978 : 1)).ToList()));

        var line = Line(report, "UPGRADES", "Rune");

        Assert.Equal(GuideMark.Missing, line.Mark);
        Assert.Equal("Superior Rune of the Dragonhunter × 4", line.Yours);
        Assert.StartsWith("2 to buy, ", line.Note);
    }

    [Fact]
    public void A_rune_held_in_the_bank_says_where()
    {
        var rune = new GuideUpgrade("Superior Rune of the Scholar", 24836, 6, "6 in the bank", 9000);

        var report = GuideComparison.Report(Input(rune: rune,
            worn: GuideComparison.ArmourSlots.Select(slot => Worn(slot, slot, Berserker)).ToList()));

        Assert.Equal("6 in the bank", Line(report, "UPGRADES", "Rune").Note);
    }

    [Fact]
    public void The_score_counts_what_is_as_the_guide_has_it()
    {
        var report = GuideComparison.Report(Input(
            worn: [Worn("Coat", "Coat", Zojja), Worn("Boots", "Boots", Marauder)],
            stats: new() { [""] = ("Berserker", Berserker) }));

        // Every armour and trinket slot is asked for; only the chest is right, by equivalence.
        Assert.Equal("1 of 12 as the guide has them", report.Score);
    }
}
